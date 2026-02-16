using Microsoft.Win32;
using System;

namespace TaskCore.Services
{
    public static class StartupRegistryService
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "TaskCore";

        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
                var value = key?.GetValue(AppName) as string;
                return !string.IsNullOrWhiteSpace(value);
            }
            catch
            {
                return false;
            }
        }

        public static void SetEnabled(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
                if (key == null) return;

                if (!enabled)
                {
                    try { key.DeleteValue(AppName, false); } catch { }
                    return;
                }

                var exe = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exe)) return;
                var cmd = "\"" + exe + "\"";
                key.SetValue(AppName, cmd);
            }
            catch
            {
            }
        }
    }
}
