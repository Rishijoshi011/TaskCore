using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using TaskCore.Models;
using TaskCore.Services;

namespace TaskCore.Services
{
    /// <summary>
    /// Represents a triggered rule event for logging/display.
    /// </summary>
    public class RuleTriggerEvent
    {
        public string RuleId { get; set; } = string.Empty;
        public string RuleName { get; set; } = string.Empty;
        public DateTime TriggeredAt { get; set; } = DateTime.Now;
        public string Description { get; set; } = string.Empty;
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public RuleActionType ActionType { get; set; }
        public string ActionResult { get; set; } = string.Empty;
    }

    /// <summary>
    /// Tracks the state of rule evaluation for each rule-process pair.
    /// </summary>
    internal class RuleState
    {
        public DateTime ConditionStartTime { get; set; }
        public DateTime LastTriggeredAt { get; set; } = DateTime.MinValue;
        public bool IsConditionMet { get; set; }
    }

    /// <summary>
    /// Singleton service that evaluates rules periodically and triggers actions.
    /// </summary>
    public sealed class RulesService
    {
        public static RulesService Instance { get; } = new();

        static RulesService()
        {
            // Watchdog migration removed - users can manually create rules
        }

        private readonly DispatcherTimer _timer;
        private readonly object _stateLock = new();
        private readonly Dictionary<string, RuleState> _ruleStates = new();
        private readonly List<RuleTriggerEvent> _recentEvents = new();
        private readonly object _eventsLock = new();
        private int _tickRunning;

        public event Action? RulesChanged;
        public event Action<RuleTriggerEvent>? RuleTriggered;

        private RulesService()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _timer.Tick += (_, __) => Tick();
            _timer.Start();
        }

        private void Tick()
        {
            if (Interlocked.Exchange(ref _tickRunning, 1) == 1)
                return;

            _ = Task.Run(() =>
            {
                try
                {
                    EvaluateRules();
                }
                finally
                {
                    Interlocked.Exchange(ref _tickRunning, 0);
                }
            });
        }

        private void EvaluateRules()
        {
            var settings = AppSettingsService.Load();
            if (!settings.RulesEngineEnabled)
                return;

            var rules = settings.Rules;
            if (rules == null || rules.Count == 0)
                return;

            var processes = Process.GetProcesses()
                .Select(p => new ProcessInfo
                {
                    Id = p.Id,
                    ProcessName = p.ProcessName,
                    WorkingSet64 = p.WorkingSet64,
                    MainWindowTitle = p.MainWindowTitle
                })
                .ToList();

            foreach (var rule in rules.Where(r => r.IsEnabled))
            {
                EvaluateRule(rule, processes);
            }
        }

        private void EvaluateRule(Rule rule, List<ProcessInfo> processes)
        {
            // Check rule-level cooldown first to prevent spam from multiple processes
            var ruleCooldownKey = $"{rule.Id}:COOLDOWN";
            lock (_stateLock)
            {
                if (_ruleStates.TryGetValue(ruleCooldownKey, out var cooldownState))
                {
                    var cooldownElapsed = DateTime.Now - cooldownState.LastTriggeredAt;
                    if (cooldownElapsed.TotalSeconds < rule.CooldownSeconds)
                        return; // Still in cooldown, skip this rule entirely
                }
            }

            var matchingProcesses = GetMatchingProcesses(rule, processes);
            bool anyTriggered = false;

            foreach (var proc in matchingProcesses)
            {
                var stateKey = $"{rule.Id}:{proc.Id}";
                bool conditionMet = CheckCondition(rule, proc);

                lock (_stateLock)
                {
                    if (!_ruleStates.TryGetValue(stateKey, out var state))
                    {
                        state = new RuleState();
                        _ruleStates[stateKey] = state;
                    }

                    if (conditionMet)
                    {
                        if (!state.IsConditionMet)
                        {
                            // Condition just started
                            state.ConditionStartTime = DateTime.Now;
                            state.IsConditionMet = true;
                        }
                        else
                        {
                            // Condition ongoing - check duration
                            var elapsed = DateTime.Now - state.ConditionStartTime;
                            
                            // Check per-process cooldown
                            var processCooldownElapsed = DateTime.Now - state.LastTriggeredAt;
                            if (processCooldownElapsed.TotalSeconds < rule.CooldownSeconds)
                                break; // This process is in cooldown

                            // Check rule-level cooldown again right before triggering
                            lock (_stateLock)
                            {
                                if (_ruleStates.TryGetValue(ruleCooldownKey, out var cooldownState))
                                {
                                    var ruleCooldownElapsed = DateTime.Now - cooldownState.LastTriggeredAt;
                                    if (ruleCooldownElapsed.TotalSeconds < rule.CooldownSeconds)
                                        break; // Rule is in cooldown, don't trigger
                                }
                            }

                            // Trigger the action!
                            state.LastTriggeredAt = DateTime.Now;
                            anyTriggered = true;
                            
                            // Set rule-level cooldown IMMEDIATELY to prevent other processes from triggering
                            lock (_stateLock)
                            {
                                if (!_ruleStates.TryGetValue(ruleCooldownKey, out var cooldownState))
                                {
                                    cooldownState = new RuleState();
                                    _ruleStates[ruleCooldownKey] = cooldownState;
                                }
                                cooldownState.LastTriggeredAt = DateTime.Now;
                            }
                            
                            TriggerAction(rule, proc);
                            break; // Only trigger once per evaluation cycle
                        }
                    }
                    else
                    {
                        // Condition ended - reset
                        state.IsConditionMet = false;
                    }
                }
            }

            // If any process triggered, set rule-level cooldown
            if (anyTriggered)
            {
                lock (_stateLock)
                {
                    if (!_ruleStates.TryGetValue(ruleCooldownKey, out var cooldownState))
                    {
                        cooldownState = new RuleState();
                        _ruleStates[ruleCooldownKey] = cooldownState;
                    }
                    cooldownState.LastTriggeredAt = DateTime.Now;
                }
            }

            // Clean up states for processes that no longer exist
            var activePids = new HashSet<int>(processes.Select(p => (int)p.Id));
            lock (_stateLock)
            {
                var keysToRemove = _ruleStates.Keys
                    .Where(k => k.StartsWith(rule.Id + ":"))
                    .Where(k =>
                    {
                        var parts = k.Split(':');
                        if (parts.Length == 2 && int.TryParse(parts[1], out var pid))
                            return !activePids.Contains(pid);
                        return true;
                    })
                    .ToList();

                foreach (var key in keysToRemove)
                    _ruleStates.Remove(key);
            }
        }

        private List<ProcessInfo> GetMatchingProcesses(Rule rule, List<ProcessInfo> allProcesses)
        {
            // If no target process name specified, apply to all processes (for CPU/Memory rules)
            if (string.IsNullOrWhiteSpace(rule.TargetProcessName))
            {
                return rule.ConditionType switch
                {
                    RuleConditionType.CpuAbove or RuleConditionType.MemoryAbove => allProcesses,
                    _ => new List<ProcessInfo>()
                };
            }

            var target = rule.TargetProcessName;
            var baseName = target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? target[..^4]
                : target;

            return allProcesses
                .Where(p =>
                {
                    var name = p.ProcessName;
                    return rule.ConditionType switch
                    {
                        RuleConditionType.ProcessNameContains =>
                            name.Contains(target, StringComparison.OrdinalIgnoreCase),
                        RuleConditionType.ProcessNameExact or
                        RuleConditionType.ProcessRunning or
                        RuleConditionType.ProcessNotRunning =>
                            name.Equals(baseName, StringComparison.OrdinalIgnoreCase),
                        _ => name.Equals(baseName, StringComparison.OrdinalIgnoreCase)
                    };
                })
                .ToList();
        }

        private bool CheckCondition(Rule rule, ProcessInfo proc)
        {
            return rule.ConditionType switch
            {
                RuleConditionType.CpuAbove =>
                    // For CPU, we need to calculate it which is expensive
                    // For simplicity in v1, we'll approximate from ProcessService refresh
                    // or use a cached value
                    CheckCpuCondition(rule, (int)proc.Id),

                RuleConditionType.MemoryAbove =>
                    (proc.WorkingSet64 / (1024.0 * 1024.0)) > rule.ThresholdValue,

                RuleConditionType.ProcessNameContains or
                RuleConditionType.ProcessNameExact =>
                    true, // Already filtered by GetMatchingProcesses

                RuleConditionType.ProcessRunning =>
                    true, // If we're here, it matched so it's running

                RuleConditionType.ProcessNotRunning =>
                    false, // This is handled differently - we check separately

                _ => false
            };
        }

        private readonly Dictionary<int, double> _cachedCpuValues = new();
        private DateTime _lastCpuCacheUpdate = DateTime.MinValue;

        private bool CheckCpuCondition(Rule rule, int processId)
        {
            // Use cached CPU values from ProcessService if available
            // For now, do a quick check using Process.GetProcessById
            try
            {
                lock (_stateLock)
                {
                    if (DateTime.Now - _lastCpuCacheUpdate > TimeSpan.FromSeconds(2))
                    {
                        _cachedCpuValues.Clear();
                        _lastCpuCacheUpdate = DateTime.Now;
                    }
                }

                // Get fresh CPU reading
                using var proc = Process.GetProcessById(processId);
                if (proc.HasExited) return false;

                // Simple check - would need proper CPU calculation in production
                // For now, always return false to not spam notifications
                // This should be integrated with ProcessService's CPU tracking
                return false;
            }
            catch
            {
                return false;
            }
        }

        private void TriggerAction(Rule rule, ProcessInfo proc)
        {
            var eventInfo = new RuleTriggerEvent
            {
                RuleId = rule.Id,
                RuleName = rule.Name,
                TriggeredAt = DateTime.Now,
                Description = $"{rule.GetConditionDescription()} triggered on {proc.ProcessName} ({proc.Id})",
                ProcessId = proc.Id,
                ProcessName = proc.ProcessName,
                ActionType = rule.ActionType,
                ActionResult = "Pending"
            };

            // Handle notification based on notification mode
            bool shouldShowToast = false;
            bool shouldPlaySound = false;

            switch (rule.NotificationMode)
            {
                case RuleNotificationMode.Silent:
                    shouldShowToast = false;
                    shouldPlaySound = false;
                    break;
                case RuleNotificationMode.ToastOnly:
                    shouldShowToast = true;
                    shouldPlaySound = false;
                    break;
                case RuleNotificationMode.SoundOnly:
                    shouldShowToast = false;
                    shouldPlaySound = true;
                    break;
                case RuleNotificationMode.ToastAndSound:
                    shouldShowToast = true;
                    shouldPlaySound = true;
                    break;
            }

            try
            {
                switch (rule.ActionType)
                {
                    case RuleActionType.Notify:
                        if (shouldShowToast)
                        {
                            ToastNotificationService.ShowRuleNotification(
                                rule.Name, 
                                proc.ProcessName,
                                rule.ActionType.ToString(),
                                rule.GetConditionDescription(),
                                shouldPlaySound ? "Sound played" : "Silent");
                        }
                        if (shouldPlaySound)
                        {
                            TryPlaySound();
                        }
                        eventInfo.ActionResult = shouldShowToast ? "Notification sent" : (shouldPlaySound ? "Sound played" : "Silent");
                        break;

                    case RuleActionType.Log:
                        LogRuleEvent(eventInfo);
                        if (shouldShowToast)
                        {
                            ToastNotificationService.ShowRuleNotification(
                                rule.Name, 
                                proc.ProcessName,
                                rule.ActionType.ToString(),
                                rule.GetConditionDescription(),
                                "Logged");
                        }
                        if (shouldPlaySound)
                        {
                            TryPlaySound();
                        }
                        eventInfo.ActionResult = "Logged to file" + (shouldShowToast ? " + Toast" : "") + (shouldPlaySound ? " + Sound" : "");
                        break;

                    case RuleActionType.KillProcess:
                        eventInfo.ActionResult = TryKillProcess((int)proc.Id);
                        if (shouldShowToast)
                        {
                            ToastNotificationService.ShowRuleNotification(
                                rule.Name, 
                                proc.ProcessName,
                                rule.ActionType.ToString(),
                                rule.GetConditionDescription(),
                                eventInfo.ActionResult);
                        }
                        if (shouldPlaySound)
                        {
                            TryPlaySound();
                        }
                        break;

                    case RuleActionType.SuspendProcess:
                        eventInfo.ActionResult = "Suspend not implemented";
                        break;

                    case RuleActionType.SetPriority:
                        eventInfo.ActionResult = TrySetPriority((int)proc.Id, rule.ActionParameter);
                        if (shouldShowToast)
                        {
                            ToastNotificationService.ShowRuleNotification(
                                rule.Name, 
                                proc.ProcessName,
                                rule.ActionType.ToString(),
                                rule.GetConditionDescription(),
                                eventInfo.ActionResult);
                        }
                        if (shouldPlaySound)
                        {
                            TryPlaySound();
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                eventInfo.ActionResult = $"Error: {ex.Message}";
            }

            // Log ALL rule events to file (regardless of action type)
            LogRuleEvent(eventInfo);

            // Add to recent events
            lock (_eventsLock)
            {
                _recentEvents.Insert(0, eventInfo);
                if (_recentEvents.Count > 100)
                    _recentEvents.RemoveAt(_recentEvents.Count - 1);
            }

            // Notify subscribers
            try
            {
                RuleTriggered?.Invoke(eventInfo);
            }
            catch { }
        }

        private static int _elevPrompted;

        private void TryPromptForElevationOnce()
        {
            if (Interlocked.Exchange(ref _elevPrompted, 1) == 1)
                return;

            try
            {
                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() =>
                {
                    try
                    {
                        var res = System.Windows.MessageBox.Show(
                            "TaskCore could not kill a protected process. Relaunch as Administrator?",
                            "Process Action",
                            System.Windows.MessageBoxButton.YesNo,
                            System.Windows.MessageBoxImage.Warning);

                        if (res == System.Windows.MessageBoxResult.Yes)
                            ElevationService.TryRelaunchAsAdminAndExit();
                    }
                    catch
                    {
                    }
                });
            }
            catch
            {
            }
        }

        private void TryPlaySound()
        {
            try
            {
                var settings = AppSettingsService.Load();
                // Check if sound alert is enabled
                if (!settings.SoundAlertEnabled)
                    return;

                // Check sound mode
                if (settings.SoundMode == "Silent")
                    return;

                var path = settings.CustomAlertSoundPath;
                if (settings.SoundMode == "Custom" && 
                    !string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path) &&
                    string.Equals(System.IO.Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase))
                {
                    var player = new System.Media.SoundPlayer(path);
                    player.Play();
                }
                else
                {
                    System.Media.SystemSounds.Beep.Play();
                }
            }
            catch
            {
            }
        }

        private string TryKillProcess(int processId)
        {
            try
            {
                using var proc = Process.GetProcessById(processId);
                proc.Kill();
                proc.WaitForExit(5000);
                return proc.HasExited ? "Killed successfully" : "Kill timeout";
            }
            catch (Exception ex)
            {
                var settings = AppSettingsService.Load();
                if (settings.PreferAdminForProtectedActions && !ElevationService.IsRunningAsAdmin())
                {
                    TryPromptForElevationOnce();
                }
                return $"Failed: {ex.GetType().Name}";
            }
        }

        private string TrySetPriority(int processId, string? priorityName)
        {
            try
            {
                using var proc = Process.GetProcessById(processId);
                var priority = priorityName?.ToLower() switch
                {
                    "low" => ProcessPriorityClass.Idle,
                    "belownormal" => ProcessPriorityClass.BelowNormal,
                    "normal" => ProcessPriorityClass.Normal,
                    "abovenormal" => ProcessPriorityClass.AboveNormal,
                    "high" => ProcessPriorityClass.High,
                    "realtime" => ProcessPriorityClass.RealTime,
                    _ => ProcessPriorityClass.Normal
                };
                proc.PriorityClass = priority;
                return $"Priority set to {priority}";
            }
            catch (Exception ex)
            {
                return $"Failed: {ex.Message}";
            }
        }

        private void LogRuleEvent(RuleTriggerEvent evt)
        {
            try
            {
                var logDir = Path.Combine(AppSettingsService.BaseDirectory, "RulesLogs");
                Directory.CreateDirectory(logDir);
                var logFile = Path.Combine(logDir, $"{DateTime.Now:yyyy-MM-dd}.log");
                var line = $"[{evt.TriggeredAt:HH:mm:ss}] {evt.RuleName} | {evt.Description} | Action: {evt.ActionType} | Result: {evt.ActionResult}";
                File.AppendAllText(logFile, line + Environment.NewLine);
            }
            catch { }
        }

        /// <summary>
        /// Gets recent rule trigger events.
        /// </summary>
        public IReadOnlyList<RuleTriggerEvent> GetRecentEvents()
        {
            lock (_eventsLock)
            {
                return _recentEvents.ToList();
            }
        }

        /// <summary>
        /// Clears all rule states (useful when rules are modified).
        /// </summary>
        public void ResetStates()
        {
            lock (_stateLock)
            {
                _ruleStates.Clear();
            }
        }
    }
}
