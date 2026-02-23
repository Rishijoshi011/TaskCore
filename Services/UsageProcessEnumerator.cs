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
            var allProcesses = Process.GetProcesses();

            // Filter based on mode - if SelectedOnly, include selected processes even if they look like system processes
            var current = allProcesses
                .Where(p =>
                {
                    var isSystem = isSystemProcess(p);
                    var name = p.ProcessName;

                    // In SelectedOnly mode, always track processes in the user's selected list
                    if (filterMode == 2 && selectedProcesses.Contains(name, StringComparer.OrdinalIgnoreCase))
                        return true;

                    // Otherwise use standard filtering
                    return !isSystem;
                })
                .ToDictionary(p => p.Id, p => p.ProcessName);

            return (allProcesses, current);
        }
    }
}
