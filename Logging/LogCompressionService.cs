using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace TaskCore.Logging
{
    public static class LogCompressionService
    {
        public static void CompressOldSessions()
        {
            try
            {
                var baseLogs = SessionManager.GetLogsDirectory();
                var sessionsDir = Path.Combine(baseLogs, "Sessions");
                var archiveDir = Path.Combine(baseLogs, "Archive");

                if (!Directory.Exists(sessionsDir)) return;
                Directory.CreateDirectory(archiveDir);

                var activeSessionDir = SessionManager.SessionDirectory;
                var sessionDirectories = Directory.GetDirectories(sessionsDir);

                foreach (var dir in sessionDirectories)
                {
                    // Skip current active session
                    if (string.Equals(dir, activeSessionDir, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var folderName = Path.GetFileName(dir);
                    var archiveFile = Path.Combine(archiveDir, $"{folderName}.zip");

                    // Compress completed old session directory if not already archived
                    if (!File.Exists(archiveFile))
                    {
                        try
                        {
                            ZipFile.CreateFromDirectory(dir, archiveFile, CompressionLevel.Optimal, includeBaseDirectory: false);
                            
                            // Remove uncompressed directory ONLY after zip file is verified created and non-empty
                            if (File.Exists(archiveFile) && new FileInfo(archiveFile).Length > 0)
                            {
                                Directory.Delete(dir, recursive: true);
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
        }
    }
}
