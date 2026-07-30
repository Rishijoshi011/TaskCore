using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace TaskCore.Services
{
    internal static class UsageProcessEnumerator
    {
        public static (Process[] allProcesses, Dictionary<int, string> current) GetCurrentProcessMap(
            int filterMode,
            List<string> selectedProcesses,
            Func<Process, bool> isSystemProcess)
        {
            Process[] allProcesses;
            try
            {
                allProcesses = Process.GetProcesses();
            }
            catch
            {
                allProcesses = Array.Empty<Process>();
            }

            var current = new Dictionary<int, string>();

            try
            {
                foreach (var p in allProcesses)
                {
                    try
                    {
                        var isSystem = isSystemProcess(p);
                        var name = p.ProcessName;

                        if (filterMode == 2 && selectedProcesses.Contains(name, StringComparer.OrdinalIgnoreCase))
                        {
                            current[p.Id] = name;
                        }
                        else if (!isSystem)
                        {
                            current[p.Id] = name;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            finally
            {
                // CRITICAL: Always dispose Process wrapper handles to prevent handle leaks
                foreach (var p in allProcesses)
                {
                    try { p.Dispose(); } catch { }
                }
            }

            return (allProcesses, current);
        }
    }
}
