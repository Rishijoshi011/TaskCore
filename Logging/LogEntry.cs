using System;

namespace TaskCore.Logging
{
    public enum LogLevel
    {
        TRACE,
        DEBUG,
        INFO,
        WARN,
        ERROR,
        FATAL,
        PERF
    }

    public readonly struct LogEntry
    {
        public DateTime TimestampUtc { get; }
        public LogLevel Level { get; }
        public int ThreadId { get; }
        public string SessionId { get; }
        public string Category { get; }
        public string Module { get; }
        public string Method { get; }
        public string Message { get; }
        public Exception? Exception { get; }
        public double CpuPercent { get; }
        public double MemoryMb { get; }

        public LogEntry(
            LogLevel level,
            string category,
            string module,
            string method,
            string message,
            Exception? exception = null,
            double cpuPercent = 0,
            double memoryMb = 0)
        {
            TimestampUtc = DateTime.UtcNow;
            Level = level;
            ThreadId = Environment.CurrentManagedThreadId;
            SessionId = SessionManager.CurrentSessionId;
            Category = category;
            Module = module;
            Method = method;
            Message = message;
            Exception = exception;
            CpuPercent = cpuPercent;
            MemoryMb = memoryMb;
        }

        public string ToFormattedString()
        {
            var localTime = TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff");
            var header = $"[{localTime}] [{Level,-5}] [T:{ThreadId:D2}] [S:{SessionId}] [{Category}] [{Module}.{Method}]";
            
            if (CpuPercent > 0 || MemoryMb > 0)
            {
                header += $" [CPU:{CpuPercent:F1}% RAM:{MemoryMb:F0}MB]";
            }

            var result = $"{header} {Message}";

            if (Exception != null)
            {
                result += $"{Environment.NewLine}  ===> EXCEPTION: {Exception.GetType().FullName}: {Exception.Message}{Environment.NewLine}{Exception.StackTrace}";
            }

            return result;
        }
    }
}
