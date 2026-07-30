using System;
using System.IO;
using TaskCore.Logging;

namespace TaskCore.Services
{
    public static class RuntimeStorageInitializer
    {
        public static string BaseDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskCore");

        public static void Bootstrap()
        {
            try
            {
                // Ensure all standard runtime directories exist
                var directories = new[]
                {
                    Path.Combine(BaseDirectory, "Config"),
                    Path.Combine(BaseDirectory, "Logs"),
                    Path.Combine(BaseDirectory, "Sessions"),
                    Path.Combine(BaseDirectory, "Performance"),
                    Path.Combine(BaseDirectory, "Automation"),
                    Path.Combine(BaseDirectory, "Diagnostics"),
                    Path.Combine(BaseDirectory, "Temp"),
                    Path.Combine(BaseDirectory, "Cache"),
                    Path.Combine(BaseDirectory, "Archive"),
                    Path.Combine(BaseDirectory, "Exports"),
                    Path.Combine(BaseDirectory, "SessionLogs"),
                    Path.Combine(BaseDirectory, "CrashDumps")
                };

                foreach (var dir in directories)
                {
                    try
                    {
                        Directory.CreateDirectory(dir);
                    }
                    catch
                    {
                    }
                }

                // Ensure AppSettings exist
                AppSettingsService.EnsureDefaultSettingsExist();
            }
            catch (Exception ex)
            {
                TaskCoreLog.Error("Application", $"Failed to bootstrap runtime directories: {ex.Message}");
            }
        }
    }
}
