# TaskCore ML Model Documentation

## Overview

TaskCore uses a hybrid machine learning approach combining **Microsoft ML.NET** with custom statistical algorithms for process analytics, anomaly detection, and predictive forecasting. The system is designed for real-time performance monitoring with minimal CPU overhead.

---

## ML Technologies Used

### 1. Microsoft ML.NET
- **Package**: `Microsoft.ML` (latest stable)
- **Purpose**: Time series forecasting using Singular Spectrum Analysis (SSA)
- **Usage**: Primary ML engine for CPU/Memory predictions

### 2. Core ML Algorithm: SSA (Singular Spectrum Analysis)

The system uses ML.NET's `ForecastBySsa` transform for time series prediction:

```csharp
var pipeline = _ml.Forecasting.ForecastBySsa(
    outputColumnName: nameof(TimeSeriesForecast.ForecastedValues),
    inputColumnName: nameof(TimeSeriesData.Value),
    windowSize: windowSize,           // 4-12 samples
    seriesLength: seriesLengthParam,  // windowSize+2 to 60
    trainSize: trainSize,             // Full series length
    horizon: horizon,                 // Prediction minutes
    confidenceLevel: 0.95f,
    confidenceLowerBoundColumn: null,
    confidenceUpperBoundColumn: null);
```

**SSA Parameters**:
- `windowSize`: 4-12 (adaptive based on available data)
- `seriesLength`: 6-60 (bounded by available samples)
- `horizon`: 5 minutes (configurable)
- `confidenceLevel`: 95%

---

## Architecture

### Dual-Prediction Strategy

| Strategy | Algorithm | Use Case | Performance |
|----------|-----------|----------|-------------|
| **Fast Path** | Linear Regression | Normal processes, low impact | ~1000x faster |
| **ML Path** | ML.NET SSA | Anomalies, high impact (>50) | Slower but accurate |

**Adaptive Selection**:
```csharp
var useMlNet = isAnomaly || impactScore > 50;
```

---

## Key Components

### 1. ProcessAnalyticsEngine

Main analytics orchestrator located in `Services/ProcessAnalyticsEngine.cs`.

**Capabilities**:
- Real-time process impact scoring
- Context-aware anomaly detection
- 5-minute resource predictions (CPU/Memory)
- Behavior classification
- Smart caching for prediction results

### 2. Anomaly Detection

**Z-Score Based Detection** with context-aware thresholds:

| App Category | CPU Threshold | Memory Threshold |
|--------------|---------------|------------------|
| Gaming | 4.0 (very lenient) | 3.5 |
| Creative | 3.5 | 3.0 |
| Interactive | 3.0 | 3.0 |
| Standard | 2.0 | 2.0 |

**Formula**:
```
z = (current - mean) / stdDev
Anomaly = z > threshold AND current > mean
```

### 3. Impact Score Calculation

**Weighted Formula**:
```
Impact = (CPU × 0.3) + (MemoryNormalized × 0.3) + (RestartCountNormalized × 0.2) + (AnomalyScore × 0.2)
```

- Range: 0-100
- Anomaly Score: Computed from Z-score (max at Z=5)

### 4. Behavior Classification

Categories determined by resource usage patterns:

| Category | Criteria |
|----------|----------|
| **Idle** | CPU < 1%, Memory < 5% |
| **Light** | CPU < 10%, Memory normalized < 10% |
| **Normal** | Default category |
| **Heavy** | CPU ≥ 60% OR Memory normalized ≥ 60% |
| **Unstable** | Restart count ≥ 3 |

### 5. Process Behavior Classifier

Located in `Services/ProcessBehaviorClassifier.cs`.

Classifies 500+ known applications into:
- **Gaming** (FPS, RPG, MMO, Racing, Fighting, Strategy, Survival, Sports, Simulation, Horror, Indie)
- **Creative** (Adobe, 3D modeling, rendering, video editing, audio production, dev tools)
- **Interactive** (browsers, communication apps, streaming)
- **Standard** (default)

---

## Prediction Pipeline

### Data Flow

```
ProcessMetrics
    ↓
Historical Samples (CPU/Memory up to 60 samples)
    ↓
Prediction Cache Check (TTL: 60 seconds)
    ↓
┌─────────────────┴─────────────────┐
│                                     │
Fast Path (Regression)          ML Path (ML.NET SSA)
│                                     │
Slope + Forecast                  SSA Forecast
│                                     │
└─────────────────┬─────────────────┘
    ↓
Predicted Value (5-min horizon)
```

### Caching Strategy

- **Cache Key**: `{processName}:{metricType}`
- **TTL**: 60 seconds
- **Hash**: Based on last 5 historical values
- **Auto-cleanup**: Removes entries older than TTL when count > 100

### Linear Regression (Fast Path)

Used when ML.NET is not needed:
```csharp
// Simple least-squares regression
slope = ((count * sumXY) - (sumX * sumY)) / denom
intercept = (sumY - slope * sumX) / count
prediction = slope * futureX + intercept
```

---

## Model Training & Data

### Data Sources

1. **Real-time Metrics** (`UsageSessionTracker.cs`)
   - Live CPU/Memory samples every 2 seconds
   - Up to 60 historical samples per process

2. **Historical Sessions** (`UsageTrackingService.cs`)
   - Past 7 days of session data (configurable: 2 days default)
   - Aggregated per application
   - Simulated sample points from session history

### Training Approach

- **Online Learning**: No pre-training required
- **Incremental**: Uses last N samples for predictions
- **Self-Healing**: Falls back to regression if SSA fails

---

## Performance Optimization

### Throttling

- **Insights Refresh**: Max once per 30 seconds
- **ML Predictions**: Only for anomalies/high-impact processes
- **Historical Data**: Limited to 2 days (reduced from 7)

### Resource Limits

| Parameter | Default | Purpose |
|-----------|---------|---------|
| MaxHistorySamples | 60 | Maximum historical data points |
| MaxPredictionCache | 100 | Cached predictions limit |
| TopOffenders | 10 | Maximum insights returned |

---

## Configuration Options

```csharp
public sealed class Options
{
    public int MaxHistorySamples { get; set; } = 60;
    public double AnomalyZThreshold { get; set; } = 2.0;      // Z-score threshold
    public double HeavyCpuThreshold { get; set; } = 60.0;     // Heavy usage %
    public int PredictionMinutes { get; set; } = 5;         // Forecast horizon
    public int PredictionSamples { get; set; } = 10;         // Regression samples
    public int TopOffenders { get; set; } = 10;
}
```

---

## Output: Insights

Each analyzed process generates an `Insight` object:

```csharp
public sealed class Insight
{
    public string ProcessName { get; set; }          // App name
    public double ImpactScore { get; set; }          // 0-100 impact rating
    public BehaviorCategory BehaviorCategory { get; set; }  // Idle/Light/Normal/Heavy/Unstable
    public bool IsAnomaly { get; set; }               // Anomaly flag
    public double PredictionCpu { get; set; }         // 5-min CPU forecast
    public double PredictionMemory { get; set; }      // 5-min Memory forecast
    public string RecommendationText { get; set; }   // Actionable advice
}
```

---

## Error Handling & Fallbacks

| Scenario | Behavior |
|----------|----------|
| Insufficient history (<8 samples) | Use linear regression |
| ML.NET exception | Log + fallback to regression |
| Cache hit | Return cached prediction instantly |
| No historical data | Return current value as prediction |

---

## Logging

Analytics events are logged to `process-analytics` channel:
- `ANALYSIS_START` - Analysis initiated
- `PROCESS_ANALYZED` - Individual process results
- `ML_PREDICTION` - Successful ML forecast
- `ML_FALLBACK` - Regression fallback used
- `ANOMALY` - Anomaly detected with Z-score
- `TOP_OFFENDERS` - Final selected insights
- `ANALYSIS_COMPLETE` - Batch completion

---

## Future Enhancements

Potential improvements:
- Deep learning models (LSTM) for complex patterns
- Per-application model training
- GPU-accelerated predictions
- Anomaly root cause analysis

---

## References

- **ML.NET Documentation**: https://docs.microsoft.com/en-us/dotnet/machine-learning/
- **SSA Algorithm**: https://en.wikipedia.org/wiki/Singular_spectrum_analysis
- **Z-Score Anomaly Detection**: Statistical outlier detection method
