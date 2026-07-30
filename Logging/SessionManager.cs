using System;
using System.IO;
using System.Text.Json;

namespace TaskCore.Logging
{
    public static class SessionManager
    {
        public static string CurrentSessionId { get; }
        public static string SessionDirectory { get; }
        public static DateTime SessionStartUtc { get; }

        private static readonly string BaseLogsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskCore",
            "Logs");

        static SessionManager()
        {
            SessionStartUtc = DateTime.UtcNow;
            CurrentSessionId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            
            var folderName = $"Session_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{CurrentSessionId}";
            SessionDirectory = Path.Combine(BaseLogsDirectory, "Sessions", folderName);

            EnsureDirectories();
            WriteSessionMetadata(isCleanShutdown: false);
        }

        private static void EnsureDirectories()
        {
            try
            {
                Directory.CreateDirectory(SessionDirectory);
                Directory.CreateDirectory(Path.Combine(BaseLogsDirectory, "Archive"));
                Directory.CreateDirectory(Path.Combine(BaseLogsDirectory, "CrashDumps"));
                Directory.CreateDirectory(Path.Combine(BaseLogsDirectory, "Diagnostics"));
                Directory.CreateDirectory(Path.Combine(BaseLogsDirectory, "Temp"));
            }
            catch
            {
            }
        }

        public static void WriteSessionMetadata(bool isCleanShutdown)
        {
            try
            {
                var metadata = new
                {
                    sessionId = CurrentSessionId,
                    appVersion = "3.0.0.0",
                    startTimeUtc = SessionStartUtc.ToString("o"),
                    endTimeUtc = isCleanShutdown ? DateTime.UtcNow.ToString("o") : null,
                    cleanShutdown = isCleanShutdown,
                    processId = Environment.ProcessId,
                    operatingSystem = Environment.OSVersion.ToString(),
                    dotNetRuntime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                    machineName = Environment.MachineName,
                    userName = Environment.UserName,
                    processorCount = Environment.ProcessorCount
                };

                var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
                var path = Path.Combine(SessionDirectory, "Session.json");
                File.WriteAllText(path, json);
            }
            catch
            {
            }
        }

        public static string GetLogsDirectory() => BaseLogsDirectory;
    }
}
