using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Transforms.TimeSeries;
using TaskCore.Models;

namespace TaskCore.Services
{
    internal static class ProcessAnalyticsLogger
    {
        private const string LogName = "process-analytics";

        public static void AnalysisStarted(int realtimeCount, int historicalCount)
        {
            TaskCoreLogger.Info(LogName, $"ANALYSIS_START: realtime={realtimeCount}, historical={historicalCount}");
        }

        public static void ProcessAnalyzed(string processName, double impactScore, BehaviorCategory category, bool isAnomaly)
        {
            TaskCoreLogger.Info(LogName, $"PROCESS_ANALYZED: {processName}, impact={impactScore:F2}, category={category}, anomaly={isAnomaly}");
        }

        public static void MlPredictionSuccess(string processName, string metricType, double predicted, int samplesUsed)
        {
            TaskCoreLogger.Info(LogName, $"ML_PREDICTION: {processName}/{metricType} predicted={predicted:F2} using {samplesUsed} samples");
        }

        public static void MlPredictionFallback(string processName, string metricType, int samplesAvailable, string reason)
        {
            TaskCoreLogger.Warn(LogName, $"ML_FALLBACK: {processName}/{metricType} samples={samplesAvailable}, reason={reason}");
        }

        public static void AnomalyDetected(string processName, double zScore, string metricType)
        {
            TaskCoreLogger.Warn(LogName, $"ANOMALY: {processName} {metricType} Z={zScore:F2}");
        }

        public static void TopOffendersSelected(int count, double highestScore)
        {
            TaskCoreLogger.Info(LogName, $"TOP_OFFENDERS: selected={count}, highest_score={highestScore:F2}");
        }

        public static void AnalysisCompleted(int totalInsights, int anomalyCount)
        {
            TaskCoreLogger.Info(LogName, $"ANALYSIS_COMPLETE: total={totalInsights}, anomalies={anomalyCount}");
        }

        public static void Error(string context, Exception ex)
        {
            TaskCoreLogger.Error(LogName, $"ERROR in {context}: {ex.Message}");
        }
    }

    public sealed class ProcessAnalyticsEngine
    {
        private sealed class TimeSeriesData
        {
            public float Value { get; set; }
        }

        private sealed class TimeSeriesForecast
        {
            [VectorType]
            public float[] ForecastedValues { get; set; } = Array.Empty<float>();
        }

        public sealed class Options
        {
            public int MaxHistorySamples { get; set; } = 60;

            public double AnomalyZThreshold { get; set; } = 2.0;

            public double HeavyCpuThreshold { get; set; } = 60.0;

            public double HeavyMemoryThresholdNormalized { get; set; } = 60.0;

            public double IdleCpuThreshold { get; set; } = 1.0;

            public double IdleMemoryThresholdNormalized { get; set; } = 5.0;

            public int UnstableRestartThreshold { get; set; } = 3;

            public int PredictionMinutes { get; set; } = 5;

            public int PredictionSamples { get; set; } = 10;

            public int TopOffenders { get; set; } = 10;
        }

        private readonly Options _options;
        private readonly MLContext _ml;
        
        // Prediction cache to avoid recomputing for stable processes
        private readonly Dictionary<string, PredictionCacheEntry> _predictionCache = new();
        private readonly TimeSpan _cacheTtl = TimeSpan.FromSeconds(60);
        
        private sealed class PredictionCacheEntry
        {
            public double PredictedValue { get; set; }
            public double Slope { get; set; }
            public int HistoryHash { get; set; }
            public DateTime Timestamp { get; set; }
            public bool UsedMl { get; set; }
        }

        public ProcessAnalyticsEngine(Options? options = null)
        {
            _options = options ?? new Options();
            _ml = new MLContext(seed: 1);
        }

        public List<Insight> Analyze(IEnumerable<ProcessMetrics> processes, DateTime now)
        {
            return Analyze(processes, Enumerable.Empty<ProcessMetrics>(), now);
        }

        public List<Insight> Analyze(IEnumerable<ProcessMetrics> realtime, IEnumerable<ProcessMetrics> historical, DateTime now)
        {
            var realtimeList = realtime?.ToList() ?? new List<ProcessMetrics>();
            var historicalList = historical?.ToList() ?? new List<ProcessMetrics>();
            
            ProcessAnalyticsLogger.AnalysisStarted(realtimeList.Count, historicalList.Count);

            var merged = MergeByProcessName(realtimeList, historicalList);
            var list = merged.ToList();

            if (list.Count == 0)
            {
                ProcessAnalyticsLogger.AnalysisCompleted(0, 0);
                return new List<Insight>();
            }

            var maxMemory = list.Max(p => Math.Max(0, p.MemoryUsage));
            var maxRestarts = list.Max(p => Math.Max(0, p.RestartCount));

            var insights = new List<Insight>(list.Count);
            int anomalyCount = 0;

            foreach (var p in list)
            {
                var cpu = Clamp(p.CpuUsage, 0, 100);
                var memNorm = NormalizeTo0_100(p.MemoryUsage, maxMemory);
                var restartNorm = NormalizeTo0_100(p.RestartCount, maxRestarts);

                var histCpu = TakeTail(p.HistoricalCpuUsage, _options.MaxHistorySamples);
                var histMem = TakeTail(p.HistoricalMemoryUsage, _options.MaxHistorySamples);

                var cpuStats = ComputeStats(histCpu);
                var memStats = ComputeStats(histMem);

                var zCpu = cpuStats.stdDev > 0 ? (cpu - cpuStats.mean) / cpuStats.stdDev : 0;
                var zMem = memStats.stdDev > 0 ? (p.MemoryUsage - memStats.mean) / memStats.stdDev : 0;

                // Context-aware anomaly detection: high usage from games/creative apps is expected
                var expectedBehavior = ProcessBehaviorClassifier.GetExpectedBehavior(p.ProcessName);
                var (isCpuAnomaly, isMemAnomaly, isAnomaly) = EvaluateAnomaly(
                    cpu, p.MemoryUsage, zCpu, zMem, expectedBehavior, cpuStats, memStats);

                if (isCpuAnomaly)
                    ProcessAnalyticsLogger.AnomalyDetected(p.ProcessName, zCpu, "CPU");
                if (isMemAnomaly)
                    ProcessAnalyticsLogger.AnomalyDetected(p.ProcessName, zMem, "Memory");

                var anomalyScore = ComputeAnomalyScore(Math.Max(zCpu, zMem));

                var impactScore = (cpu * 0.3) + (memNorm * 0.3) + (restartNorm * 0.2) + (anomalyScore * 0.2);

                // Only use expensive ML.NET SSA for anomalies or high-impact processes
                // Use fast regression for normal processes (1000x faster)
                var useMlNet = isAnomaly || impactScore > 50;
                var predictionCpu = PredictWithCaching(histCpu, cpu, _options.PredictionMinutes, p.ProcessName, "CPU", useMlNet, out var cpuSlope);
                var predictionMem = PredictWithCaching(histMem, p.MemoryUsage, _options.PredictionMinutes, p.ProcessName, "Memory", useMlNet, out var memSlope);

                var category = Classify(cpu, memNorm, p.RestartCount, isAnomaly);

                var recommendation = GenerateRecommendationText(
                    p.ProcessName,
                    category,
                    isCpuAnomaly,
                    isMemAnomaly,
                    cpuSlope,
                    memSlope,
                    p.RuntimeMinutes);

                var insight = new Insight
                {
                    ProcessName = p.ProcessName,
                    ImpactScore = Math.Round(impactScore, 2),
                    BehaviorCategory = category,
                    IsAnomaly = isAnomaly,
                    PredictionCpu = Math.Round(predictionCpu, 2),
                    PredictionMemory = Math.Round(predictionMem, 2),
                    RecommendationText = recommendation
                };

                insights.Add(insight);
                ProcessAnalyticsLogger.ProcessAnalyzed(p.ProcessName, insight.ImpactScore, category, isAnomaly);

                if (isAnomaly)
                    anomalyCount++;
            }

            var result = insights
                .OrderByDescending(i => i.ImpactScore)
                .ThenBy(i => i.ProcessName)
                .Take(Math.Max(0, _options.TopOffenders))
                .ToList();

            var highestScore = result.Count > 0 ? result[0].ImpactScore : 0;
            ProcessAnalyticsLogger.TopOffendersSelected(result.Count, highestScore);
            ProcessAnalyticsLogger.AnalysisCompleted(insights.Count, anomalyCount);

            return result;
        }

        private IEnumerable<ProcessMetrics> MergeByProcessName(IEnumerable<ProcessMetrics> realtime, IEnumerable<ProcessMetrics> historical)
        {
            var map = new Dictionary<string, ProcessMetrics>(StringComparer.OrdinalIgnoreCase);

            foreach (var h in historical ?? Enumerable.Empty<ProcessMetrics>())
            {
                if (h == null) continue;
                var key = h.ProcessName ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key)) continue;

                map[key] = CloneMetrics(h);
            }

            foreach (var r in realtime ?? Enumerable.Empty<ProcessMetrics>())
            {
                if (r == null) continue;
                var key = r.ProcessName ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key)) continue;

                if (!map.TryGetValue(key, out var existing))
                {
                    map[key] = CloneMetrics(r);
                    continue;
                }

                existing.CpuUsage = r.CpuUsage;
                existing.MemoryUsage = r.MemoryUsage;
                existing.StartTime = r.StartTime;
                existing.RuntimeMinutes = r.RuntimeMinutes;
                existing.RestartCount = r.RestartCount != 0 ? r.RestartCount : existing.RestartCount;

                if ((existing.HistoricalCpuUsage == null || existing.HistoricalCpuUsage.Count == 0) && r.HistoricalCpuUsage != null)
                    existing.HistoricalCpuUsage = new List<double>(r.HistoricalCpuUsage);

                if ((existing.HistoricalMemoryUsage == null || existing.HistoricalMemoryUsage.Count == 0) && r.HistoricalMemoryUsage != null)
                    existing.HistoricalMemoryUsage = new List<double>(r.HistoricalMemoryUsage);
            }

            return map.Values;
        }

        private static ProcessMetrics CloneMetrics(ProcessMetrics src)
        {
            return new ProcessMetrics
            {
                ProcessName = src.ProcessName,
                CpuUsage = src.CpuUsage,
                MemoryUsage = src.MemoryUsage,
                StartTime = src.StartTime,
                RuntimeMinutes = src.RuntimeMinutes,
                RestartCount = src.RestartCount,
                HistoricalCpuUsage = src.HistoricalCpuUsage != null ? new List<double>(src.HistoricalCpuUsage) : new List<double>(),
                HistoricalMemoryUsage = src.HistoricalMemoryUsage != null ? new List<double>(src.HistoricalMemoryUsage) : new List<double>()
            };
        }

        private BehaviorCategory Classify(double cpuPercent, double memNorm, int restartCount, bool isAnomaly)
        {
            if (isAnomaly)
                return BehaviorCategory.Suspicious;

            if (restartCount >= _options.UnstableRestartThreshold)
                return BehaviorCategory.Unstable;

            if (cpuPercent >= _options.HeavyCpuThreshold || memNorm >= _options.HeavyMemoryThresholdNormalized)
                return BehaviorCategory.Heavy;

            if (cpuPercent <= _options.IdleCpuThreshold && memNorm <= _options.IdleMemoryThresholdNormalized)
                return BehaviorCategory.Idle;

            return BehaviorCategory.Normal;
        }

        /// <summary>
        /// Evaluates whether high usage is an anomaly based on context.
        /// Returns tuple: (isCpuAnomaly, isMemAnomaly, isOverallAnomaly)
        /// </summary>
        private (bool cpuAnomaly, bool memAnomaly, bool overallAnomaly) EvaluateAnomaly(
            double cpu, double memory, double zCpu, double zMem, 
            ExpectedBehavior expectedBehavior, (double mean, double stdDev) cpuStats, (double mean, double stdDev) memStats)
        {
            double cpuThreshold = _options.AnomalyZThreshold;
            double memThreshold = _options.AnomalyZThreshold;
            
            // Adjust thresholds based on expected behavior
            switch (expectedBehavior)
            {
                case ExpectedBehavior.Gaming:
                    // Games are expected to use high resources
                    // Only flag as anomaly if extremely high (Z > 4) or if system is unresponsive (CPU > 95% sustained)
                    cpuThreshold = 4.0;  // Very lenient for games
                    memThreshold = 3.5;  // Slightly less lenient for memory
                    break;
                    
                case ExpectedBehavior.Creative:
                    // Creative apps expected moderate-high usage during renders/exports
                    cpuThreshold = 3.5;
                    memThreshold = 3.0;
                    break;
                    
                case ExpectedBehavior.Interactive:
                    // Browsers, video calls - moderate usage expected
                    cpuThreshold = 3.0;
                    memThreshold = 3.0;
                    break;
                    
                default:
                    // Standard apps - use normal threshold
                    cpuThreshold = _options.AnomalyZThreshold;
                    memThreshold = _options.AnomalyZThreshold;
                    break;
            }
            
            // Special case: if current usage is actually LOWER than history but Z-score is high,
            // it means the app is behaving better than usual (not an anomaly)
            bool isCpuAnomaly = zCpu > cpuThreshold && cpu > cpuStats.mean;
            bool isMemAnomaly = zMem > memThreshold && memory > memStats.mean;
            
            // Additional check: if CPU is maxed out for extended period in gaming, it might be
            // optimization issues, but only flag if significantly above expected
            if (expectedBehavior == ExpectedBehavior.Gaming && cpu > 95 && zCpu > 5)
            {
                // Even for games, sustained 95%+ with very high Z-score is suspicious
                isCpuAnomaly = true;
            }
            
            return (isCpuAnomaly, isMemAnomaly, isCpuAnomaly || isMemAnomaly);
        }

        private string GenerateRecommendationText(
            string processName,
            BehaviorCategory category,
            bool isCpuAnomaly,
            bool isMemAnomaly,
            double cpuSlope,
            double memSlope,
            double runtimeMinutes)
        {
            var name = string.IsNullOrWhiteSpace(processName) ? "process" : processName;

            if (isMemAnomaly)
                return $"High memory usage detected for {name}";

            if (isCpuAnomaly)
                return $"High CPU usage detected for {name}";

            if (category == BehaviorCategory.Unstable)
                return $"Process {name} shows unstable behavior due to frequent restarts";

            if (cpuSlope > 0.5)
                return "CPU usage trend is increasing, system performance may decrease";

            if (memSlope > 5)
                return $"Memory usage trend is increasing for {name}";

            if (category == BehaviorCategory.Idle && runtimeMinutes >= 30)
                return $"Process idle for long time, consider closing it";

            if (category == BehaviorCategory.Heavy)
                return $"High resource usage detected for {name}";

            return string.Empty;
        }

        private static List<double> TakeTail(List<double>? values, int maxCount)
        {
            if (values == null || values.Count == 0 || maxCount <= 0)
                return new List<double>();

            if (values.Count <= maxCount)
                return values.ToList();

            return values.Skip(values.Count - maxCount).ToList();
        }

        private static (double mean, double stdDev) ComputeStats(List<double> values)
        {
            if (values == null || values.Count < 2)
                return (values != null && values.Count == 1 ? values[0] : 0, 0);

            var mean = values.Average();
            var variance = values.Select(v => (v - mean) * (v - mean)).Average();
            var stdDev = Math.Sqrt(Math.Max(0, variance));
            return (mean, stdDev);
        }

        private static double ComputeAnomalyScore(double z)
        {
            if (z <= 2)
                return 0;

            var normalized = (z - 2) / 3.0;
            return Clamp(normalized * 100.0, 0, 100);
        }

        private double PredictWithCaching(List<double> history, double current, int horizonMinutes, string processName, string metricType, bool useMlNet, out double slope)
        {
            slope = 0;
            var cacheKey = $"{processName}:{metricType}";
            var historyHash = ComputeHistoryHash(history);
            var now = DateTime.Now;
            
            // Check cache first
            if (_predictionCache.TryGetValue(cacheKey, out var cached) && 
                cached.HistoryHash == historyHash && 
                now - cached.Timestamp < _cacheTtl)
            {
                slope = cached.Slope;
                return cached.PredictedValue;
            }
            
            double predicted;
            bool usedMl = false;
            
            // Only use expensive ML.NET SSA when explicitly requested (anomalies/high impact)
            // Otherwise use fast linear regression
            if (useMlNet && history != null && history.Count >= 8)
            {
                try
                {
                    predicted = PredictWithMlnet(history, current, horizonMinutes, out slope);
                    usedMl = true;
                }
                catch
                {
                    predicted = PredictWithRegression(history ?? new List<double>(), current, _options.PredictionSamples, horizonMinutes, out slope);
                }
            }
            else
            {
                predicted = PredictWithRegression(history ?? new List<double>(), current, _options.PredictionSamples, horizonMinutes, out slope);
            }
            
            // Update cache
            _predictionCache[cacheKey] = new PredictionCacheEntry
            {
                PredictedValue = predicted,
                Slope = slope,
                HistoryHash = historyHash,
                Timestamp = now,
                UsedMl = usedMl
            };
            
            // Clean old cache entries occasionally
            if (_predictionCache.Count > 100)
            {
                var oldEntries = _predictionCache.Where(e => now - e.Value.Timestamp > _cacheTtl).Select(e => e.Key).ToList();
                foreach (var key in oldEntries)
                    _predictionCache.Remove(key);
            }
            
            return predicted;
        }
        
        private static int ComputeHistoryHash(List<double> history)
        {
            if (history == null || history.Count == 0) return 0;
            // Simple hash based on count and last few values
            var hash = history.Count;
            var samples = Math.Min(5, history.Count);
            for (int i = 0; i < samples; i++)
            {
                hash = hash * 31 + history[history.Count - samples + i].GetHashCode();
            }
            return hash;
        }
        
        private double PredictWithMlnet(List<double> history, double current, int horizonMinutes, out double slope)
        {
            slope = 0;
            
            var tail = history.Count > _options.MaxHistorySamples
                ? history.Skip(history.Count - _options.MaxHistorySamples).ToArray()
                : history.ToArray();

            var seriesLength = tail.Length;
            var horizon = Math.Max(1, horizonMinutes);
            var windowSize = Math.Max(4, Math.Min(12, seriesLength / 2));
            var seriesLengthParam = Math.Max(windowSize + 2, Math.Min(60, seriesLength));
            var trainSize = seriesLength;
            var seasonality = Math.Max(2, Math.Min(30, seriesLength / 3));

            var data = tail.Select(v => new TimeSeriesData { Value = (float)v });
            var dataView = _ml.Data.LoadFromEnumerable(data);

            var pipeline = _ml.Forecasting.ForecastBySsa(
                outputColumnName: nameof(TimeSeriesForecast.ForecastedValues),
                inputColumnName: nameof(TimeSeriesData.Value),
                windowSize: windowSize,
                seriesLength: seriesLengthParam,
                trainSize: trainSize,
                horizon: horizon,
                confidenceLevel: 0.95f,
                confidenceLowerBoundColumn: null,
                confidenceUpperBoundColumn: null);

            var model = pipeline.Fit(dataView);
            var engine = model.CreateTimeSeriesEngine<TimeSeriesData, TimeSeriesForecast>(_ml);
            var forecast = engine.Predict();

            var predicted = forecast.ForecastedValues != null && forecast.ForecastedValues.Length > 0
                ? forecast.ForecastedValues[forecast.ForecastedValues.Length - 1]
                : (float)current;

            // Compute slope using regression
            _ = PredictWithRegression(tail.ToList(), current, _options.PredictionSamples, horizonMinutes, out slope);

            return predicted;
        }

        private double PredictWithMlnetOrFallback(List<double> history, double current, int horizonMinutes, string processName, string metricType, out double slope)
        {
            slope = 0;

            // Need a minimum amount of data for SSA. Also keep it bounded.
            if (history == null || history.Count < 8)
            {
                ProcessAnalyticsLogger.MlPredictionFallback(processName, metricType, history?.Count ?? 0, "insufficient_history");
                return PredictWithRegression(history ?? new List<double>(), current, _options.PredictionSamples, horizonMinutes, out slope);
            }

            try
            {
                var predicted = PredictWithMlnet(history, current, horizonMinutes, out slope);
                ProcessAnalyticsLogger.MlPredictionSuccess(processName, metricType, predicted, Math.Min(history.Count, _options.MaxHistorySamples));
                return predicted;
            }
            catch (Exception ex)
            {
                ProcessAnalyticsLogger.MlPredictionFallback(processName, metricType, history.Count, $"exception: {ex.Message}");
                ProcessAnalyticsLogger.Error("PredictWithMlnetOrFallback", ex);
                return PredictWithRegression(history, current, _options.PredictionSamples, horizonMinutes, out slope);
            }
        }

        private static double PredictWithRegression(List<double> history, double current, int lastNSamples, int horizonMinutes, out double slope)
        {
            slope = 0;

            if (history == null || history.Count == 0)
                return current;

            // Use array segment instead of creating new list
            int startIdx = 0;
            int count = history.Count;
            
            if (lastNSamples > 0 && history.Count > lastNSamples)
            {
                startIdx = history.Count - lastNSamples;
                count = lastNSamples;
            }

            if (count < 2)
                return history[history.Count - 1];

            double sumX = 0, sumY = 0, sumXX = 0, sumXY = 0;
            
            for (int i = 0; i < count; i++)
            {
                var x = i;
                var y = history[startIdx + i];
                sumX += x;
                sumY += y;
                sumXX += x * x;
                sumXY += x * y;
            }

            var denom = (count * sumXX) - (sumX * sumX);
            if (Math.Abs(denom) < 1e-9)
                return sumY / count;

            var m = ((count * sumXY) - (sumX * sumY)) / denom;
            var b = (sumY - (m * sumX)) / count;

            slope = m;

            var xFuture = (count - 1) + Math.Max(0, horizonMinutes);
            return (m * xFuture) + b;
        }

        // Keep old method name for compatibility but make it delegate to new method
        private static double PredictValue(List<double> history, double current, int lastNSamples, int horizonMinutes, out double slope)
        {
            return PredictWithRegression(history, current, lastNSamples, horizonMinutes, out slope);
        }

        private static double NormalizeTo0_100(double value, double max)
        {
            if (max <= 0)
                return 0;

            return Clamp((value / max) * 100.0, 0, 100);
        }

        private static double NormalizeTo0_100(int value, int max)
        {
            if (max <= 0)
                return 0;

            return Clamp((value / (double)max) * 100.0, 0, 100);
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
