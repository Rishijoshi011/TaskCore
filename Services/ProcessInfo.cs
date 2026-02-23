namespace TaskCore.Services
{
    /// <summary>
    /// Lightweight process information for rule evaluation.
    /// </summary>
    public class ProcessInfo
    {
        public int Id { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public long WorkingSet64 { get; set; }
        public string? MainWindowTitle { get; set; }
    }
}
