using System;
using System.IO;

namespace TaskCore.Logging
{
    public static class LogRotationService
    {
        public const long MaxFileSizeBytes = 50 * 1024 * 1024; // 50 MB
        public const long MaxDirectorySizeBytes = 1024L * 1024L * 1024L; // 1 GB
        public const int MaxRotatedFiles = 5;

        public static string GetLogFilePath(string category)
        {
            var sessionDir = SessionManager.SessionDirectory;
            var fileName = $"{category}.log";
            var basePath = Path.Combine(sessionDir, fileName);

            if (!File.Exists(basePath))
                return basePath;

            var info = new FileInfo(basePath);
            if (info.Length < MaxFileSizeBytes)
                return basePath;

            // Rotate existing log files: .4.log -> .5.log, .3.log -> .4.log, etc.
            try
            {
                for (int i = MaxRotatedFiles - 1; i >= 1; i--)
                {
                    var src = Path.Combine(sessionDir, $"{category}.{i}.log");
                    var dst = Path.Combine(sessionDir, $"{category}.{i + 1}.log");
                    if (File.Exists(src))
                    {
                        if (File.Exists(dst)) File.Delete(dst);
                        File.Move(src, dst);
                    }
                }

                var firstRotated = Path.Combine(sessionDir, $"{category}.1.log");
                if (File.Exists(firstRotated)) File.Delete(firstRotated);
                File.Move(basePath, firstRotated);
            }
            catch
            {
            }

            return basePath;
        }
    }
}
