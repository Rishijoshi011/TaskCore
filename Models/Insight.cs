namespace TaskCore.Models
{
    public sealed class Insight
    {
        public string ProcessName { get; set; } = string.Empty;

        public double ImpactScore { get; set; }

        public BehaviorCategory BehaviorCategory { get; set; }

        public bool IsAnomaly { get; set; }

        public double PredictionCpu { get; set; }

        public double PredictionMemory { get; set; }

        public string RecommendationText { get; set; } = string.Empty;
    }
}
