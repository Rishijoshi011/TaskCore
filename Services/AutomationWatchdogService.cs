using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

namespace TaskCore.Services
{
    internal sealed class AutomationWatchdogService : IDisposable
    {
        private static readonly Lazy<AutomationWatchdogService> _instance = new(() => new AutomationWatchdogService());
        public static AutomationWatchdogService Instance => _instance.Value;

        private readonly object _lock = new();
        private Timer? _timer;
        private int _tickRunning;

        private readonly Dictionary<int, (DateTime firstSeenUtc, DateTime lastSeenUtc)> _cpuOffenders = new();

        private DateTime _lastExplorerRestartUtc = DateTime.MinValue;
        private DateTime _lastEdgeWebViewCleanupUtc = DateTime.MinValue;
        private DateTime _lastCpuActionUtc = DateTime.MinValue;

        private AutomationWatchdogService()
        {
            StartOrStop();
        }

        public void StartOrStop()
        {
            lock (_lock)
            {
                try
                {
                    var settings = AppSettingsService.Load();
                    if (!settings.AutomationWatchdogEnabled)
                    {
                        _timer?.Dispose();
                        _timer = null;
                        return;
                    }

                    if (_timer == null)
                    {
                        _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
                    }
                }
                catch
                {
                }
            }
        }

        private static int? TryGetParentPid(int pid)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "root\\CIMV2",
                    $"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = {pid}");

                foreach (var obj in searcher.Get())
                {
                    try
                    {
                        var ppidObj = obj["ParentProcessId"];
                        if (ppidObj == null) continue;
                        return Convert.ToInt32(ppidObj);
                    }
                    catch
                    {
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private void Tick()
        {
            if (Interlocked.Exchange(ref _tickRunning, 1) == 1)
                return;

            _ = Task.Run(() =>
            {
                try
                {
                    TickCore();
                }
                finally
                {
                    Interlocked.Exchange(ref _tickRunning, 0);
                }
            });
        }

        private void TickCore()
        {
            var settings = AppSettingsService.Load();
            if (!settings.AutomationWatchdogEnabled)
                return;

            if (settings.AutomationRestartExplorerEnabled)
                TryRestartExplorerIfFrozen(settings);

            if (settings.AutomationEdgeWebViewCleanupEnabled)
                TryCleanupEdgeWebView(settings);

            if (settings.AutomationCpuMitigationEnabled)
                TryMitigateCpu(settings);
        }

        private void TryRestartExplorerIfFrozen(AppSettings settings)
        {
            try
            {
                var nowUtc = DateTime.UtcNow;
                if ((nowUtc - _lastExplorerRestartUtc) < TimeSpan.FromSeconds(Math.Max(10, settings.AutomationExplorerRestartCooldownSeconds)))
                    return;

                var explorers = Process.GetProcessesByName("explorer");
                if (explorers == null || explorers.Length == 0)
                    return;

                var frozen = explorers.Any(p => !IsRespondingSafe(p));
                if (!frozen)
                    return;

                var evt = new AutomationLogStorage.AutomationLogEvent
                {
                    category = "explorer",
                    name = "explorer_freeze",
                    decision = "explorer.exe not responding",
                    action = "restart_explorer"
                };
                AutomationLogStorage.Append(evt);

                foreach (var p in explorers)
                {
                    try { p.Kill(); } catch { }
                }

                try { Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true }); } catch { }

                _lastExplorerRestartUtc = nowUtc;

                AutomationLogStorage.Append(new AutomationLogStorage.AutomationLogEvent
                {
                    category = "explorer",
                    name = "explorer_restart",
                    decision = "restart issued",
                    action = "start_explorer",
                    success = true
                });
            }
            catch (Exception ex)
            {
                AutomationLogStorage.Append(new AutomationLogStorage.AutomationLogEvent
                {
                    category = "explorer",
                    name = "explorer_restart_error",
                    decision = "exception",
                    action = "restart_explorer",
                    success = false,
                    error = ex.Message
                });
            }
        }

        private void TryCleanupEdgeWebView(AppSettings settings)
        {
            try
            {
                var nowUtc = DateTime.UtcNow;
                if ((nowUtc - _lastEdgeWebViewCleanupUtc) < TimeSpan.FromSeconds(Math.Max(10, settings.AutomationEdgeWebViewCleanupCooldownSeconds)))
                    return;

                var procs = Process.GetProcessesByName("msedgewebview2");
                if (procs == null || procs.Length == 0)
                    return;

                var soft = Math.Max(1, settings.AutomationEdgeWebViewSoftLimitInstances);
                var hard = Math.Max(soft + 1, settings.AutomationEdgeWebViewHardLimitInstances);
                var panic = Math.Max(hard + 1, settings.AutomationEdgeWebViewPanicLimitInstances);

                var count = procs.Length;

                if (count >= soft)
                {
                    AutomationLogStorage.Append(new AutomationLogStorage.AutomationLogEvent
                    {
                        category = "webview2",
                        name = "webview2_limit_check",
                        decision = $"instances={count} soft={soft} hard={hard} panic={panic}",
                        action = count >= hard ? "evaluate_heal" : "log_only",
                        tags = new Dictionary<string, string>
                        {
                            ["instances"] = count.ToString(),
                            ["soft"] = soft.ToString(),
                            ["hard"] = hard.ToString(),
                            ["panic"] = panic.ToString()
                        }
                    });
                }

                if (count < hard)
                    return;

                var parentGroups = new Dictionary<int, List<Process>>();
                foreach (var p in procs)
                {
                    try
                    {
                        var parentPid = TryGetParentPid(p.Id);
                        if (!parentPid.HasValue || parentPid.Value <= 0)
                            continue;

                        if (!parentGroups.TryGetValue(parentPid.Value, out var list))
                        {
                            list = new List<Process>();
                            parentGroups[parentPid.Value] = list;
                        }

                        list.Add(p);
                    }
                    catch
                    {
                    }
                }

                var topParent = parentGroups
                    .Select(kvp => new { ParentPid = kvp.Key, Count = kvp.Value.Count })
                    .OrderByDescending(x => x.Count)
                    .FirstOrDefault();

                if (topParent != null && topParent.Count > 0)
                {
                    try
                    {
                        var parent = Process.GetProcessById(topParent.ParentPid);
                        if (!IsNeverTouch(parent.ProcessName) && !string.Equals(parent.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase))
                        {
                            var parentSnap = AutomationLogStorage.TrySnapshot(parent);
                            AutomationLogStorage.Append(new AutomationLogStorage.AutomationLogEvent
                            {
                                category = "webview2",
                                name = count >= panic ? "webview2_panic_heal_parent_kill" : "webview2_hard_heal_parent_kill",
                                decision = $"instances={count} parentPid={topParent.ParentPid} parentChildren={topParent.Count}",
                                action = "kill_parent",
                                process = parentSnap,
                                tags = new Dictionary<string, string>
                                {
                                    ["instances"] = count.ToString(),
                                    ["parentChildren"] = topParent.Count.ToString()
                                }
                            });

                            parent.Kill();
                            _lastEdgeWebViewCleanupUtc = nowUtc;
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        AutomationLogStorage.Append(new AutomationLogStorage.AutomationLogEvent
                        {
                            category = "webview2",
                            name = "webview2_heal_parent_kill_error",
                            decision = "exception",
                            action = "kill_parent",
                            success = false,
                            error = ex.Message
                        });
                    }
                }

                if (count < panic)
                {
                    _lastEdgeWebViewCleanupUtc = nowUtc;
                    return;
                }

                var maxKills = Math.Max(1, settings.AutomationEdgeWebViewMaxKillsPerRun);
                var ordered = procs
                    .Select(p => new
                    {
                        P = p,
                        Ws = SafeWorkingSet(p),
                        Start = SafeStartTimeUtc(p)
                    })
                    .OrderByDescending(x => x.Ws)
                    .ThenBy(x => x.Start)
                    .ToList();

                var extras = ordered.Skip(settings.AutomationEdgeWebViewMaxInstances).Take(maxKills).ToList();
                if (extras.Count == 0)
                    return;

                AutomationLogStorage.Append(new AutomationLogStorage.AutomationLogEvent
                {
                    category = "webview2",
                    name = "webview2_cleanup_trigger",
                    decision = $"instances={procs.Length} limit={settings.AutomationEdgeWebViewMaxInstances}",
                    action = "kill_extras",
                    tags = new Dictionary<string, string>
                    {
                        ["count"] = procs.Length.ToString(),
                        ["limit"] = settings.AutomationEdgeWebViewMaxInstances.ToString(),
                        ["killing"] = extras.Count.ToString()
                    }
                });

                foreach (var x in extras)
                {
                    try
                    {
                        var snap = AutomationLogStorage.TrySnapshot(x.P);
                        AutomationLogStorage.Append(new AutomationLogStorage.AutomationLogEvent
                        {
                            category = "webview2",
                            name = "webview2_kill",
                            decision = "killing extra instance",
                            action = "kill",
                            process = snap
                        });

                        x.P.Kill();
                    }
                    catch (Exception ex)
                    {
                        AutomationLogStorage.Append(new AutomationLogStorage.AutomationLogEvent
                        {
                            category = "webview2",
                            name = "webview2_kill_error",
                            decision = "exception",
                            action = "kill",
                            success = false,
                            error = ex.Message
                        });
                    }
                }

                _lastEdgeWebViewCleanupUtc = nowUtc;
            }
            catch (Exception ex)
            {
                AutomationLogStorage.Append(new AutomationLogStorage.AutomationLogEvent
                {
                    category = "webview2",
                    name = "webview2_cleanup_error",
                    decision = "exception",
                    action = "cleanup",
                    success = false,
                    error = ex.Message
                });
            }
        }

        private void TryMitigateCpu(AppSettings settings)
        {
            try
            {
                var nowUtc = DateTime.UtcNow;
                if ((nowUtc - _lastCpuActionUtc) < TimeSpan.FromSeconds(Math.Max(5, settings.AutomationCpuActionCooldownSeconds)))
                    return;

                var processes = Process.GetProcesses();
                var samples = new List<(Process P, TimeSpan Cpu, long Ws)>();

                foreach (var p in processes)
                {
                    try
                    {
                        if (p.HasExited) continue;
                        if (IsNeverTouch(p.ProcessName)) continue;
                        samples.Add((p, p.TotalProcessorTime, p.WorkingSet64));
                    }
                    catch
                    {
                    }
                }

                Thread.Sleep(600);

                var deltas = new List<(Process P, double CpuPct, long Ws)>();
                foreach (var s in samples)
                {
                    try
                    {
                        if (s.P.HasExited) continue;
                        var cpu2 = s.P.TotalProcessorTime;
                        var deltaMs = (cpu2 - s.Cpu).TotalMilliseconds;
                        var cpuPct = (deltaMs / 600.0) * 100.0 / Environment.ProcessorCount;
                        deltas.Add((s.P, cpuPct, s.Ws));
                    }
                    catch
                    {
                    }
                }

                var offender = deltas.OrderByDescending(d => d.CpuPct).FirstOrDefault();
                if (offender.P == null)
                    return;

                if (offender.CpuPct < settings.AutomationCpuOffenderThresholdPercent)
                {
                    lock (_lock) _cpuOffenders.Remove(offender.P.Id);
                    return;
                }

                var sustain = Math.Max(3, settings.AutomationCpuOffenderSustainSeconds);

                bool shouldLowerPriority = false;
                bool shouldKill = false;

                lock (_lock)
                {
                    if (!_cpuOffenders.TryGetValue(offender.P.Id, out var state))
                    {
                        _cpuOffenders[offender.P.Id] = (nowUtc, nowUtc);
                    }
                    else
                    {
                        _cpuOffenders[offender.P.Id] = (state.firstSeenUtc, nowUtc);
                        if ((nowUtc - state.firstSeenUtc) >= TimeSpan.FromSeconds(sustain))
                            shouldLowerPriority = true;

                        if ((nowUtc - state.firstSeenUtc) >= TimeSpan.FromSeconds(sustain + Math.Max(1, settings.AutomationCpuKillAfterSeconds)))
                            shouldKill = true;
                    }
                }

                if (!shouldLowerPriority && !shouldKill)
                    return;

                if (shouldLowerPriority)
                {
                    TryLowerPriority(offender.P, offender.CpuPct);
                    _lastCpuActionUtc = nowUtc;
                }

                if (shouldKill)
                {
                    TryKillOffender(offender.P, offender.CpuPct);
                    _lastCpuActionUtc = nowUtc;

                    lock (_lock) _cpuOffenders.Remove(offender.P.Id);
                }
            }
            catch (Exception ex)
            {
                AutomationLogStorage.Append(new AutomationLogStorage.AutomationLogEvent
                {
                    category = "cpu",
                    name = "cpu_mitigation_error",
                    decision = "exception",
                    action = "mitigate",
                    success = false,
                    error = ex.Message
                });
            }
        }

        private void TryLowerPriority(Process p, double cpuPct)
        {
            try
            {
                var snap = AutomationLogStorage.TrySnapshot(p, cpuPct);
                AutomationLogStorage.Append(new AutomationLogStorage.AutomationLogEvent
                {
                    category = "cpu",
                    name = "cpu_offender_priority_lower",
                    decision = $"cpu={cpuPct:F1}%",
                    action = "set_priority_below_normal",
                    process = snap
                });

                try
                {
                    p.PriorityClass = ProcessPriorityClass.BelowNormal;
                }
                catch
                {
                    // if protected, log it
                    AutomationLogStorage.Append(new AutomationLogStorage.AutomationLogEvent
                    {
                        category = "cpu",
                        name = "cpu_offender_priority_lower_denied",
                        decision = "access_denied_or_failed",
                        action = "set_priority_below_normal",
                        success = false,
                        process = snap
                    });
                }
            }
            catch (Exception ex)
            {
                AutomationLogStorage.Append(new AutomationLogStorage.AutomationLogEvent
                {
                    category = "cpu",
                    name = "cpu_offender_priority_lower_error",
                    decision = "exception",
                    action = "set_priority_below_normal",
                    success = false,
                    error = ex.Message
                });
            }
        }

        private void TryKillOffender(Process p, double cpuPct)
        {
            try
            {
                var snap = AutomationLogStorage.TrySnapshot(p, cpuPct);
                AutomationLogStorage.Append(new AutomationLogStorage.AutomationLogEvent
                {
                    category = "cpu",
                    name = "cpu_offender_kill",
                    decision = $"cpu={cpuPct:F1}% still high after priority lower",
                    action = "kill",
                    process = snap
                });

                p.Kill();
            }
            catch (Exception ex)
            {
                AutomationLogStorage.Append(new AutomationLogStorage.AutomationLogEvent
                {
                    category = "cpu",
                    name = "cpu_offender_kill_error",
                    decision = "exception",
                    action = "kill",
                    success = false,
                    error = ex.Message
                });
            }
        }

        private static bool IsNeverTouch(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;

            // Hard blocklist - never kill/modify these
            var n = name.Trim().ToLowerInvariant();
            return n is "system" or "idle" or "csrss" or "wininit" or "smss" or "lsass" or "services" or "winlogon";
        }

        private static bool IsRespondingSafe(Process p)
        {
            try
            {
                return p.Responding;
            }
            catch
            {
                return true;
            }
        }

        private static long SafeWorkingSet(Process p)
        {
            try { return p.WorkingSet64; } catch { return 0; }
        }

        private static DateTime SafeStartTimeUtc(Process p)
        {
            try { return p.StartTime.ToUniversalTime(); } catch { return DateTime.MinValue; }
        }

        public void Dispose()
        {
            try { _timer?.Dispose(); } catch { }
            _timer = null;
        }
    }
}
