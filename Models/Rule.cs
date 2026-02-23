using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TaskCore.Models
{
    /// <summary>
    /// Types of notification modes for a rule.
    /// </summary>
    public enum RuleNotificationMode
    {
        Silent,           // No notification, just execute action
        ToastOnly,        // Show toast notification
        SoundOnly,        // Play sound only
        ToastAndSound     // Both toast and sound
    }

    /// <summary>
    /// Types of conditions that can be evaluated for a rule.
    /// </summary>
    public enum RuleConditionType
    {
        CpuAbove,
        MemoryAbove,
        ProcessNameContains,
        ProcessNameExact,
        ProcessRunning,
        ProcessNotRunning
    }

    /// <summary>
    /// Types of actions that can be triggered when a rule condition is met.
    /// </summary>
    public enum RuleActionType
    {
        Notify,
        Log,
        KillProcess,
        SuspendProcess,
        SetPriority
    }

    /// <summary>
    /// Represents a user-defined rule for monitoring processes and triggering actions.
    /// </summary>
    public class Rule : INotifyPropertyChanged
    {
        /// <summary>
        /// Unique identifier for the rule.
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Display name for the rule.
        /// </summary>
        public string Name { get; set; } = "New Rule";

        private bool _isEnabled = true;
        /// <summary>
        /// Whether the rule is enabled and should be evaluated.
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// The notification mode for this rule.
        /// </summary>
        public RuleNotificationMode NotificationMode { get; set; } = RuleNotificationMode.ToastAndSound;

        /// <summary>
        /// The type of condition to evaluate.
        /// </summary>
        public RuleConditionType ConditionType { get; set; } = RuleConditionType.CpuAbove;

        /// <summary>
        /// The target value for the condition.
        /// For CPU: percentage (0-100)
        /// For Memory: MB
        /// For ProcessName: string to match
        /// </summary>
        public double ThresholdValue { get; set; } = 50.0;

        /// <summary>
        /// Optional process name filter. If set, rule only applies to matching processes.
        /// </summary>
        public string? TargetProcessName { get; set; }

        /// <summary>
        /// How long the condition must persist before triggering (in seconds).
        /// </summary>
        public int DurationSeconds { get; set; } = 5;

        /// <summary>
        /// The action to perform when the condition is met.
        /// </summary>
        public RuleActionType ActionType { get; set; } = RuleActionType.Notify;

        /// <summary>
        /// Additional parameter for the action (e.g., priority level for SetPriority).
        /// </summary>
        public string? ActionParameter { get; set; }

        /// <summary>
        /// Cooldown period after triggering (in seconds) to prevent spam.
        /// </summary>
        public int CooldownSeconds { get; set; } = 60;

        /// <summary>
        /// When the rule was created.
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// When the rule was last modified.
        /// </summary>
        public DateTime ModifiedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Returns a human-readable description of the condition.
        /// </summary>
        public string GetConditionDescription()
        {
            var target = string.IsNullOrWhiteSpace(TargetProcessName) ? "any process" : TargetProcessName;

            return ConditionType switch
            {
                RuleConditionType.CpuAbove => $"CPU > {ThresholdValue}% for {target}",
                RuleConditionType.MemoryAbove => $"Memory > {ThresholdValue} MB for {target}",
                RuleConditionType.ProcessNameContains => $"Process name contains '{TargetProcessName}'",
                RuleConditionType.ProcessNameExact => $"Process name is exactly '{TargetProcessName}'",
                RuleConditionType.ProcessRunning => $"{TargetProcessName} is running",
                RuleConditionType.ProcessNotRunning => $"{TargetProcessName} is not running",
                _ => "Unknown condition"
            };
        }

        /// <summary>
        /// Returns a human-readable description of the action.
        /// </summary>
        public string GetActionDescription()
        {
            return ActionType switch
            {
                RuleActionType.Notify => "Show notification",
                RuleActionType.Log => "Log to file",
                RuleActionType.KillProcess => "Kill process",
                RuleActionType.SuspendProcess => "Suspend process",
                RuleActionType.SetPriority => $"Set priority to {ActionParameter}",
                _ => "Unknown action"
            };
        }

        /// <summary>
        /// Returns a human-readable description of the notification mode.
        /// </summary>
        public string GetNotificationModeDescription()
        {
            return NotificationMode switch
            {
                RuleNotificationMode.Silent => "Silent",
                RuleNotificationMode.ToastOnly => "Toast Only",
                RuleNotificationMode.SoundOnly => "Sound Only",
                RuleNotificationMode.ToastAndSound => "Toast + Sound",
                _ => "Unknown"
            };
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
