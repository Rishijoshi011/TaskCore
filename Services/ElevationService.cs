using System;
using System.Diagnostics;
using System.Security.Principal;

namespace TaskCore.Services
{
    public static class ElevationService
    {
        public static bool IsRunningAsAdmin()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public static bool TryRelaunchAsAdminAndExit()
        {
            try
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exe))
                {
                    Debug.WriteLine("[ElevationService] ProcessPath is null or empty");
                    return false;
                }

                Debug.WriteLine($"[ElevationService] Relaunching: {exe}");

                var psi = new ProcessStartInfo(exe)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = Environment.CurrentDirectory,
                    Arguments = "--elevated"
                };

                var proc = Process.Start(psi);
                if (proc == null)
                {
                    Debug.WriteLine("[ElevationService] Process.Start returned null");
                    return false;
                }

                Debug.WriteLine($"[ElevationService] New process started (PID: {proc.Id}), waiting...");
                
                // Give the new process time to initialize before exiting
                Thread.Sleep(500);
                
                // Check if process is still running
                try
                {
                    proc.Refresh();
                    if (proc.HasExited)
                    {
                        Debug.WriteLine($"[ElevationService] New process exited early with code: {proc.ExitCode}");
                        return false;
                    }
                }
                catch { }

                Debug.WriteLine("[ElevationService] Exiting current process...");
                Environment.Exit(0);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ElevationService] Failed to relaunch: {ex.Message}");
                return false;
            }
        }
    }
}
