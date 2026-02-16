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
                if (string.IsNullOrWhiteSpace(exe)) return false;

                var psi = new ProcessStartInfo(exe)
                {
                    UseShellExecute = true,
                    Verb = "runas"
                };

                Process.Start(psi);
                Environment.Exit(0);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
