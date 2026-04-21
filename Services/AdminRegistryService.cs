using Microsoft.Win32;
using System;

namespace TaskCore.Services
{
    /// <summary>
    /// Manages registry settings for Always Run as Administrator.
    /// Uses AppCompatFlags\Layers to mark the executable to always run elevated.
    /// </summary>
    public static class AdminRegistryService
    {
        // Registry path for application compatibility flags (RUNASADMIN)
        private const string LayersKeyPath = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";

        /// <summary>
        /// Checks if the current executable is set to always run as administrator.
        /// </summary>
        public static bool IsEnabled()
        {
            try
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exePath))
                    return false;

                using var key = Registry.CurrentUser.OpenSubKey(LayersKeyPath, false);
                var value = key?.GetValue(exePath) as string;
                return value?.Contains("RUNASADMIN") == true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Sets or clears the RUNASADMIN flag in registry.
        /// When enabled, Windows will always launch this executable as administrator.
        /// </summary>
        public static void SetEnabled(bool enabled)
        {
            try
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exePath))
                    return;

                using var key = Registry.CurrentUser.CreateSubKey(LayersKeyPath);
                if (key == null) return;

                if (!enabled)
                {
                    // Remove the RUNASADMIN flag
                    try { key.DeleteValue(exePath, false); } catch { }
                    TaskCoreLogger.Info("admin-registry", $"Removed RUNASADMIN flag for: {exePath}");
                }
                else
                {
                    // Set RUNASADMIN flag - this makes Windows always run the exe as admin
                    key.SetValue(exePath, "RUNASADMIN");
                    TaskCoreLogger.Info("admin-registry", $"Set RUNASADMIN flag for: {exePath}");
                }
            }
            catch (Exception ex)
            {
                TaskCoreLogger.Error("admin-registry", $"Failed to set RUNASADMIN: {ex.Message}");
            }
        }
    }
}
