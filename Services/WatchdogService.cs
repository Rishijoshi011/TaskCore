using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace TaskCore.Services
{
    public sealed class WatchdogService
    {
        public static WatchdogService Instance { get; } = new WatchdogService();

        private readonly object _activityLock = new();
        private readonly List<string> _activity = new();
        private const int MaxActivity = 20;

        public event Action? ActivityChanged;

        public IReadOnlyList<string> GetRecentActivity()
        {
            lock (_activityLock)
            {
                return _activity.ToList();
            }
        }

        private readonly DispatcherTimer _timer;
        private int _tickRunning;

        private readonly object _cooldownLock = new();
        private readonly Dictionary<string, DateTime> _lastActionUtcByTarget = new(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan _targetCooldown = TimeSpan.FromSeconds(5);

        private WatchdogService()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
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
                    EvaluateOnce();
                }
                finally
                {
                    Interlocked.Exchange(ref _tickRunning, 0);
                }
            });
        }

        private void EvaluateOnce()
        {
            var settings = AppSettingsService.Load();
            if (!settings.WatchdogEnabled)
                return;

            var targets = (settings.WatchdogTargets ?? new List<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (targets.Count == 0)
                return;

            foreach (var target in targets)
            {
                var baseName = target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? target[..^4]
                    : target;

                Process[] procs;
                try { procs = Process.GetProcessesByName(baseName); }
                catch { continue; }

                foreach (var proc in procs)
                {
                    HandleHit(proc, target, settings);
                }
            }
        }

        private void HandleHit(Process proc, string name, AppSettings settings)
        {
            if (IsInCooldown(name))
                return;

            var mode = settings.WatchdogMode ?? "Alert Only";

            if (string.Equals(mode, "Log Only", StringComparison.OrdinalIgnoreCase))
            {
                if (settings.WatchdogLoggingEnabled)
                    WatchdogLogger.Log(name, "Detected (Log Only)");

                AddActivity($"{DateTime.Now:HH:mm:ss} | {name} | Detected (Log Only)");
                MarkCooldown(name);
                if (settings.WatchdogSoundAlertEnabled)
                    TryBeep();
                return;
            }

            if (string.Equals(mode, "Alert Only", StringComparison.OrdinalIgnoreCase))
            {
                if (settings.WatchdogLoggingEnabled)
                    WatchdogLogger.Log(name, "Detected (Alert Only)");

                AddActivity($"{DateTime.Now:HH:mm:ss} | {name} | Detected (Alert Only)");
                MarkCooldown(name);
                if (settings.WatchdogSoundAlertEnabled)
                    TryBeep();
                return;
            }

            if (string.Equals(mode, "Auto Kill", StringComparison.OrdinalIgnoreCase))
            {
                AddActivity($"{DateTime.Now:HH:mm:ss} | {name} | Attempt kill");
                TryKill(proc, name, settings);
            }
        }

        private bool IsInCooldown(string target)
        {
            try
            {
                var now = DateTime.UtcNow;
                lock (_cooldownLock)
                {
                    if (_lastActionUtcByTarget.TryGetValue(target, out var last))
                    {
                        if ((now - last) < _targetCooldown)
                            return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private void MarkCooldown(string target)
        {
            try
            {
                var now = DateTime.UtcNow;
                lock (_cooldownLock)
                {
                    _lastActionUtcByTarget[target] = now;

                    if (_lastActionUtcByTarget.Count > 200)
                    {
                        var cutoff = now - TimeSpan.FromMinutes(10);
                        var keys = _lastActionUtcByTarget.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList();
                        foreach (var k in keys) _lastActionUtcByTarget.Remove(k);
                    }
                }
            }
            catch
            {
            }
        }

        private void TryKill(Process proc, string name, AppSettings settings)
        {
            try
            {
                proc.Kill(true);
                if (settings.WatchdogLoggingEnabled)
                    WatchdogLogger.Log(name, "Killed");

                AddActivity($"{DateTime.Now:HH:mm:ss} | {name} | Killed");
                MarkCooldown(name);
                if (settings.WatchdogSoundAlertEnabled)
                    TryBeep();
            }
            catch (Exception ex)
            {
                if (settings.WatchdogLoggingEnabled)
                    WatchdogLogger.Log(name, "Kill failed: " + ex.GetType().Name);

                AddActivity($"{DateTime.Now:HH:mm:ss} | {name} | Kill failed ({ex.GetType().Name})");
                MarkCooldown(name);

                if (settings.PreferAdminForProtectedActions && !ElevationService.IsRunningAsAdmin())
                {
                    TryPromptForElevationOnce();
                }
            }
        }

        private void AddActivity(string line)
        {
            try
            {
                lock (_activityLock)
                {
                    _activity.Insert(0, line);
                    while (_activity.Count > MaxActivity)
                        _activity.RemoveAt(_activity.Count - 1);
                }

                try
                {
                    Application.Current?.Dispatcher?.BeginInvoke(() =>
                    {
                        try { ActivityChanged?.Invoke(); } catch { }
                    });
                }
                catch
                {
                }
            }
            catch
            {
            }
        }

        private static int _elevPrompted;

        private void TryPromptForElevationOnce()
        {
            if (Interlocked.Exchange(ref _elevPrompted, 1) == 1)
                return;

            try
            {
                Application.Current?.Dispatcher?.BeginInvoke(() =>
                {
                    try
                    {
                        var res = MessageBox.Show(
                            "TaskCore Watchdog could not kill a protected process. Relaunch as Administrator?",
                            "Watchdog",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);

                        if (res == MessageBoxResult.Yes)
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

        private void TryBeep()
        {
            try
            {
                var settings = AppSettingsService.Load();
                var path = settings.CustomAlertSoundPath;
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path) &&
                    string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase))
                {
                    var player = new SoundPlayer(path);
                    player.Play();
                }
                else
                {
                    SystemSounds.Beep.Play();
                }
            }
            catch
            {
            }
        }
    }
}
