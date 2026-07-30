using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using TaskCore.Logging;

namespace TaskCore.Services
{
    public sealed class QuickActionResult
    {
        public string ActionName { get; }
        public bool Success { get; }
        public int ExitCode { get; }
        public string Message { get; }
        public TimeSpan Duration { get; }
        public string Output { get; }

        public QuickActionResult(string actionName, bool success, int exitCode, string message, TimeSpan duration, string output = "")
        {
            ActionName = actionName;
            Success = success;
            ExitCode = exitCode;
            Message = message;
            Duration = duration;
            Output = output;
        }
    }

    public static class QuickActionsService
    {
        [System.Runtime.InteropServices.DllImport("psapi.dll")]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        public static async Task<QuickActionResult> TrimRamAsync()
        {
            var startTime = DateTime.UtcNow;
            return await Task.Run(() =>
            {
                long freedBytes = 0;
                int trimmedCount = 0;
                var processes = Process.GetProcesses();

                foreach (var p in processes)
                {
                    try
                    {
                        var before = p.WorkingSet64;
                        if (EmptyWorkingSet(p.Handle))
                        {
                            p.Refresh();
                            var after = p.WorkingSet64;
                            if (before > after)
                            {
                                freedBytes += (before - after);
                            }
                            trimmedCount++;
                        }
                    }
                    catch { }
                    finally { p.Dispose(); }
                }

                var duration = DateTime.UtcNow - startTime;
                var freedMb = freedBytes / (1024.0 * 1024.0);
                var msg = $"Trimmed working set of {trimmedCount} processes. Freed ~{freedMb:F1} MB RAM.";
                TaskCoreLog.Info("QuickActions", $"[TrimRam] {msg} in {duration.TotalMilliseconds:F0}ms");
                return new QuickActionResult("TrimRam", true, 0, msg, duration);
            });
        }

        public static async Task<QuickActionResult> ClearWindowsUpdateCacheAsync()
        {
            if (!ElevationService.IsRunningAsAdmin())
            {
                return new QuickActionResult("ClearWindowsUpdateCache", false, -1, "Clearing Windows Update Cache requires Administrator privileges.", TimeSpan.Zero);
            }

            var startTime = DateTime.UtcNow;
            return await Task.Run(async () =>
            {
                await ExecuteCommandAsync("WU_Stop1", "net.exe", "stop wuauserv", requireAdmin: true);
                await ExecuteCommandAsync("WU_Stop2", "net.exe", "stop bits", requireAdmin: true);

                long freedBytes = 0;
                int deletedFiles = 0;
                var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                var downloadDir = Path.Combine(winDir, "SoftwareDistribution", "Download");

                if (Directory.Exists(downloadDir))
                {
                    try
                    {
                        foreach (var file in Directory.GetFiles(downloadDir, "*.*", SearchOption.AllDirectories))
                        {
                            try
                            {
                                var info = new FileInfo(file);
                                freedBytes += info.Length;
                                File.Delete(file);
                                deletedFiles++;
                            }
                            catch { }
                        }
                    }
                    catch { }
                }

                await ExecuteCommandAsync("WU_Start1", "net.exe", "start wuauserv", requireAdmin: true);
                await ExecuteCommandAsync("WU_Start2", "net.exe", "start bits", requireAdmin: true);

                var duration = DateTime.UtcNow - startTime;
                var freedMb = freedBytes / (1024.0 * 1024.0);
                var msg = $"Cleared Windows Update cache ({freedMb:F1} MB freed across {deletedFiles} files).";
                TaskCoreLog.Info("QuickActions", $"[ClearWindowsUpdateCache] {msg} in {duration.TotalMilliseconds:F0}ms");
                return new QuickActionResult("ClearWindowsUpdateCache", true, 0, msg, duration);
            });
        }

        public static async Task<QuickActionResult> OptimizeDisksAsync()
        {
            if (!ElevationService.IsRunningAsAdmin())
            {
                return new QuickActionResult("OptimizeDisks", false, -1, "Disk optimization & TRIM requires Administrator privileges.", TimeSpan.Zero);
            }

            return await ExecuteCommandAsync("OptimizeDisks", "defrag.exe", "C: /O", requireAdmin: true, timeoutMs: 60000);
        }

        public static async Task<QuickActionResult> ResetFirewallRulesAsync()
        {
            if (!ElevationService.IsRunningAsAdmin())
            {
                return new QuickActionResult("ResetFirewallRules", false, -1, "Resetting Windows Firewall requires Administrator privileges.", TimeSpan.Zero);
            }

            return await ExecuteCommandAsync("ResetFirewallRules", "netsh.exe", "advfirewall reset", requireAdmin: true);
        }

        public static async Task<QuickActionResult> RestartGpuDriverAsync()
        {
            if (!ElevationService.IsRunningAsAdmin())
            {
                return new QuickActionResult("RestartGpuDriver", false, -1, "Restarting GPU driver requires Administrator privileges.", TimeSpan.Zero);
            }

            return await ExecuteCommandAsync(
                "RestartGpuDriver",
                "powershell.exe",
                "-NoProfile -Command \"Get-PnpDevice -Class DisplayAdapter -Status OK | Disable-PnpDevice -Confirm:$false; Start-Sleep 2; Get-PnpDevice -Class DisplayAdapter | Enable-PnpDevice -Confirm:$false\"",
                requireAdmin: true,
                timeoutMs: 30000);
        }

        public static async Task<QuickActionResult> RestartExplorerAsync()
        {
            var startTime = DateTime.UtcNow;
            try
            {
                // Kill explorer.exe processes
                var processes = Process.GetProcessesByName("explorer");
                foreach (var p in processes)
                {
                    try
                    {
                        p.Kill();
                        await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
                    }
                    catch { }
                    finally { p.Dispose(); }
                }

                // Restart explorer.exe
                Process.Start("explorer.exe");

                var duration = DateTime.UtcNow - startTime;
                TaskCoreLog.Info("QuickActions", $"[RestartExplorer] Succeeded in {duration.TotalMilliseconds:F0}ms");
                return new QuickActionResult("RestartExplorer", true, 0, "Explorer restarted successfully", duration);
            }
            catch (Exception ex)
            {
                var duration = DateTime.UtcNow - startTime;
                TaskCoreLog.Error("QuickActions", $"[RestartExplorer] Failed: {ex.Message}", ex);
                return new QuickActionResult("RestartExplorer", false, -1, $"Failed to restart Explorer: {ex.Message}", duration);
            }
        }

        public static async Task<QuickActionResult> FlushDnsAsync()
        {
            return await ExecuteCommandAsync("FlushDns", "ipconfig.exe", "/flushdns", requireAdmin: false);
        }

        public static async Task<QuickActionResult> ClearTempAsync()
        {
            var startTime = DateTime.UtcNow;
            return await Task.Run(() =>
            {
                long freedBytes = 0;
                int deletedFiles = 0;
                int deletedDirs = 0;
                var errors = new List<string>();

                var tempPaths = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"),
                    Path.GetTempPath()
                };

                foreach (var tempPath in tempPaths)
                {
                    if (string.IsNullOrWhiteSpace(tempPath) || !Directory.Exists(tempPath))
                        continue;

                    try
                    {
                        var files = Directory.GetFiles(tempPath, "*.*", SearchOption.TopDirectoryOnly);
                        foreach (var file in files)
                        {
                            try
                            {
                                var fi = new FileInfo(file);
                                var len = fi.Length;
                                File.Delete(file);
                                freedBytes += len;
                                deletedFiles++;
                            }
                            catch (Exception ex)
                            {
                                errors.Add($"File {Path.GetFileName(file)}: {ex.Message}");
                            }
                        }

                        var dirs = Directory.GetDirectories(tempPath);
                        foreach (var dir in dirs)
                        {
                            try
                            {
                                Directory.Delete(dir, recursive: true);
                                deletedDirs++;
                            }
                            catch (Exception ex)
                            {
                                errors.Add($"Dir {Path.GetFileName(dir)}: {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Path {tempPath}: {ex.Message}");
                    }
                }

                var duration = DateTime.UtcNow - startTime;
                var freedMb = freedBytes / (1024.0 * 1024.0);
                var summary = $"Cleared {freedMb:F1} MB ({deletedFiles} files, {deletedDirs} dirs, {errors.Count} locked files)";

                TaskCoreLog.Info("QuickActions", $"[ClearTemp] {summary} in {duration.TotalMilliseconds:F0}ms");
                return new QuickActionResult("ClearTemp", true, 0, summary, duration);
            });
        }

        public static async Task<QuickActionResult> RestartAudioServiceAsync()
        {
            return await ExecuteServiceRestartAsync("RestartAudioService", "audiosrv");
        }

        public static async Task<QuickActionResult> RestartPrintSpoolerAsync()
        {
            return await ExecuteServiceRestartAsync("RestartPrintSpooler", "spooler");
        }

        public static async Task<QuickActionResult> RestartWindowsSearchAsync()
        {
            return await ExecuteServiceRestartAsync("RestartWindowsSearch", "wsearch");
        }

        public static async Task<QuickActionResult> ResetNetworkStackAsync()
        {
            if (!ElevationService.IsRunningAsAdmin())
            {
                return new QuickActionResult("ResetNetworkStack", false, -1, "Resetting network stack requires Administrator privileges.", TimeSpan.Zero);
            }

            var startTime = DateTime.UtcNow;
            return await Task.Run(async () =>
            {
                var commands = new[]
                {
                    ("netsh.exe", "winsock reset"),
                    ("netsh.exe", "int ip reset"),
                    ("ipconfig.exe", "/flushdns"),
                    ("ipconfig.exe", "/release"),
                    ("ipconfig.exe", "/renew")
                };

                var results = new List<string>();
                bool allSuccess = true;

                foreach (var (exe, args) in commands)
                {
                    var res = await ExecuteCommandAsync("ResetNetworkStackSub", exe, args, requireAdmin: true);
                    results.Add($"{exe} {args} => Exit {res.ExitCode}");
                    if (!res.Success) allSuccess = false;
                }

                var duration = DateTime.UtcNow - startTime;
                var msg = allSuccess ? "Network stack reset completed successfully" : "Network stack reset completed with errors";
                TaskCoreLog.Info("QuickActions", $"[ResetNetworkStack] {msg} ({string.Join(", ", results)})");
                return new QuickActionResult("ResetNetworkStack", allSuccess, allSuccess ? 0 : 1, msg, duration, string.Join("; ", results));
            });
        }

        public static async Task<QuickActionResult> RenewIpAsync()
        {
            var startTime = DateTime.UtcNow;
            var resRelease = await ExecuteCommandAsync("RenewIp_Release", "ipconfig.exe", "/release", requireAdmin: false);
            await Task.Delay(1000);
            var resRenew = await ExecuteCommandAsync("RenewIp_Renew", "ipconfig.exe", "/renew", requireAdmin: false);

            var duration = DateTime.UtcNow - startTime;
            bool success = resRenew.Success;
            var msg = success ? "IP address renewed successfully" : $"IP renew failed (Exit: {resRenew.ExitCode})";
            TaskCoreLog.Info("QuickActions", $"[RenewIp] {msg} in {duration.TotalMilliseconds:F0}ms");
            return new QuickActionResult("RenewIp", success, resRenew.ExitCode, msg, duration);
        }

        public static async Task<QuickActionResult> ClearIconCacheAsync()
        {
            var startTime = DateTime.UtcNow;
            return await Task.Run(async () =>
            {
                try
                {
                    // Kill explorer
                    foreach (var p in Process.GetProcessesByName("explorer"))
                    {
                        try { p.Kill(); await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); } catch { } finally { p.Dispose(); }
                    }

                    // Delete IconCache.db
                    var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    var iconCache = Path.Combine(localAppData, "IconCache.db");
                    if (File.Exists(iconCache))
                    {
                        try { File.Delete(iconCache); } catch { }
                    }

                    // Restart explorer
                    Process.Start("explorer.exe");

                    var duration = DateTime.UtcNow - startTime;
                    TaskCoreLog.Info("QuickActions", $"[ClearIconCache] Succeeded in {duration.TotalMilliseconds:F0}ms");
                    return new QuickActionResult("ClearIconCache", true, 0, "Icon cache cleared successfully", duration);
                }
                catch (Exception ex)
                {
                    var duration = DateTime.UtcNow - startTime;
                    TaskCoreLog.Error("QuickActions", $"[ClearIconCache] Failed: {ex.Message}", ex);
                    return new QuickActionResult("ClearIconCache", false, -1, $"Failed to clear icon cache: {ex.Message}", duration);
                }
            });
        }

        public static async Task<QuickActionResult> SfcScanAsync()
        {
            return await LaunchElevatedToolAsync("SfcScan", "sfc.exe", "/scannow");
        }

        public static async Task<QuickActionResult> DismRepairAsync()
        {
            return await LaunchElevatedToolAsync("DismRepair", "DISM.exe", "/Online /Cleanup-Image /RestoreHealth");
        }

        private static async Task<QuickActionResult> ExecuteServiceRestartAsync(string actionName, string serviceName)
        {
            if (!ElevationService.IsRunningAsAdmin())
            {
                return new QuickActionResult(actionName, false, -1, $"Restarting '{serviceName}' service requires Administrator privileges.", TimeSpan.Zero);
            }

            var startTime = DateTime.UtcNow;
            var stopRes = await ExecuteCommandAsync($"{actionName}_Stop", "net.exe", $"stop {serviceName}", requireAdmin: true);
            await Task.Delay(500);
            var startRes = await ExecuteCommandAsync($"{actionName}_Start", "net.exe", $"start {serviceName}", requireAdmin: true);

            var duration = DateTime.UtcNow - startTime;
            bool success = startRes.Success;
            var msg = success ? $"Service '{serviceName}' restarted successfully" : $"Failed to restart service '{serviceName}'";
            TaskCoreLog.Info("QuickActions", $"[{actionName}] {msg} in {duration.TotalMilliseconds:F0}ms");
            return new QuickActionResult(actionName, success, startRes.ExitCode, msg, duration);
        }

        private static async Task<QuickActionResult> LaunchElevatedToolAsync(string actionName, string fileName, string arguments)
        {
            var startTime = DateTime.UtcNow;
            try
            {
                var psi = new ProcessStartInfo(fileName, arguments)
                {
                    UseShellExecute = true,
                    Verb = "runas"
                };

                using var proc = Process.Start(psi);
                var duration = DateTime.UtcNow - startTime;
                TaskCoreLog.Info("QuickActions", $"[{actionName}] Launched elevated process (PID: {proc?.Id})");
                return new QuickActionResult(actionName, true, 0, $"{actionName} tool launched in elevated window", duration);
            }
            catch (Exception ex)
            {
                var duration = DateTime.UtcNow - startTime;
                TaskCoreLog.Error("QuickActions", $"[{actionName}] Failed to launch elevated process: {ex.Message}", ex);
                return new QuickActionResult(actionName, false, -1, $"Elevation required or canceled: {ex.Message}", duration);
            }
        }

        private static async Task<QuickActionResult> ExecuteCommandAsync(string actionName, string fileName, string arguments, bool requireAdmin, int timeoutMs = 15000)
        {
            var startTime = DateTime.UtcNow;
            try
            {
                var psi = new ProcessStartInfo(fileName, arguments)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                if (requireAdmin && !ElevationService.IsRunningAsAdmin())
                {
                    return new QuickActionResult(actionName, false, -1, $"{actionName} requires Administrator privileges.", TimeSpan.Zero);
                }

                using var proc = new Process { StartInfo = psi };
                proc.Start();

                var outputTask = proc.StandardOutput.ReadToEndAsync();
                var errorTask = proc.StandardError.ReadToEndAsync();

                using var cts = new System.Threading.CancellationTokenSource(timeoutMs);
                await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);

                var output = await outputTask.ConfigureAwait(false);
                var error = await errorTask.ConfigureAwait(false);

                var fullOutput = output + (string.IsNullOrEmpty(error) ? "" : $" [ERR: {error}]");
                var duration = DateTime.UtcNow - startTime;
                bool success = proc.ExitCode == 0;

                var msg = success ? $"{actionName} completed" : $"{actionName} failed with exit code {proc.ExitCode}";
                TaskCoreLog.Info("QuickActions", $"[{actionName}] ExitCode: {proc.ExitCode}, Duration: {duration.TotalMilliseconds:F0}ms");
                return new QuickActionResult(actionName, success, proc.ExitCode, msg, duration, fullOutput);
            }
            catch (Exception ex)
            {
                var duration = DateTime.UtcNow - startTime;
                TaskCoreLog.Error("QuickActions", $"[{actionName}] Exception: {ex.Message}", ex);
                return new QuickActionResult(actionName, false, -1, $"Failed: {ex.Message}", duration);
            }
        }
    }
}
