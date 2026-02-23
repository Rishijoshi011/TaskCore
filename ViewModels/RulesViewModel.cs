using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskCore.Models;
using TaskCore.Services;

namespace TaskCore.ViewModels
{
    public partial class RulesViewModel : ObservableObject
    {
        private readonly AppSettings _settings;

        [ObservableProperty]
        private ObservableCollection<Rule> rules = new();

        [ObservableProperty]
        private ObservableCollection<RuleTriggerEvent> recentEvents = new();

        [ObservableProperty]
        private bool rulesEngineEnabled;

        [ObservableProperty]
        private bool soundAlertEnabled = true;

        [ObservableProperty]
        private string selectedSoundMode = "Default";

        public string[] SoundModes => new[] { "Silent", "Default", "Custom" };

        public bool CanBrowseCustomSound => SoundAlertEnabled && SelectedSoundMode == "Custom";

        public string LogsDirectoryPath => System.IO.Path.Combine(AppSettingsService.BaseDirectory, "RulesLogs");

        [ObservableProperty]
        private Rule? selectedRule;

        [ObservableProperty]
        private bool isEditingRule;

        // Commands
        public ICommand AddRuleCommand { get; }
        public ICommand SaveRuleCommand { get; }
        public ICommand CancelEditCommand { get; }
        public ICommand DeleteRuleCommand { get; }
        public ICommand EditRuleCommand { get; }
        public ICommand ToggleRuleEnabledCommand { get; }
        public ICommand RefreshEventsCommand { get; }
        public ICommand ClearEventsCommand { get; }
        public ICommand BrowseCustomSoundCommand { get; }
        public ICommand CopyLogsPathCommand { get; }

        // Editing properties
        [ObservableProperty]
        private string editRuleName = "New Rule";

        [ObservableProperty]
        private RuleConditionType editConditionType = RuleConditionType.CpuAbove;

        [ObservableProperty]
        private double editThresholdValue = 50;

        [ObservableProperty]
        private string? editTargetProcessName;

        [ObservableProperty]
        private int editDurationSeconds = 5;

        [ObservableProperty]
        private RuleActionType editActionType = RuleActionType.Notify;

        [ObservableProperty]
        private string? editActionParameter;

        [ObservableProperty]
        private int editCooldownSeconds = 60;

        [ObservableProperty]
        private RuleNotificationMode editNotificationMode = RuleNotificationMode.ToastAndSound;

        // Available options
        public RuleConditionType[] ConditionTypes => (RuleConditionType[])System.Enum.GetValues(typeof(RuleConditionType));
        public RuleActionType[] ActionTypes => (RuleActionType[])System.Enum.GetValues(typeof(RuleActionType));
        public RuleNotificationMode[] NotificationModes => (RuleNotificationMode[])System.Enum.GetValues(typeof(RuleNotificationMode));

        public string[] PriorityOptions => new[] { "Low", "BelowNormal", "Normal", "AboveNormal", "High", "RealTime" };

        public RulesViewModel()
        {
            _settings = AppSettingsService.Load();
            RulesEngineEnabled = _settings.RulesEngineEnabled;
            SoundAlertEnabled = _settings.SoundAlertEnabled;
            SelectedSoundMode = _settings.SoundMode ?? "Default";

            // Initialize commands
            AddRuleCommand = new RelayCommand(AddRule);
            SaveRuleCommand = new RelayCommand(SaveRule);
            CancelEditCommand = new RelayCommand(CancelEdit);
            DeleteRuleCommand = new RelayCommand<Rule>(DeleteRule);
            EditRuleCommand = new RelayCommand<Rule>(EditRule);
            ToggleRuleEnabledCommand = new RelayCommand<Rule>(ToggleRuleEnabled);
            RefreshEventsCommand = new RelayCommand(RefreshEvents);
            ClearEventsCommand = new RelayCommand(ClearEvents);
            BrowseCustomSoundCommand = new RelayCommand(BrowseCustomSound);
            CopyLogsPathCommand = new RelayCommand(CopyLogsPathToClipboard);

            LoadRules();
            LoadRecentEvents();

            // Subscribe to rule triggers
            RulesService.Instance.RuleTriggered += OnRuleTriggered;
        }

        private void BrowseCustomSound()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Alert Sound",
                Filter = "WAV Audio (*.wav)|*.wav",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var sourcePath = dialog.FileName;
                    // Validate file extension
                    if (!System.IO.Path.GetExtension(sourcePath).Equals(".wav", StringComparison.OrdinalIgnoreCase))
                    {
                        System.Windows.MessageBox.Show(
                            "Only .wav files are supported for custom alert sounds.",
                            "Invalid File",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Warning);
                        return;
                    }
                    _settings.CustomAlertSoundPath = sourcePath;
                    AppSettingsService.Save(_settings);
                }
                catch
                {
                }
            }
        }

        private void LoadRules()
        {
            var rulesList = _settings.Rules ?? new System.Collections.Generic.List<Rule>();
            Rules = new ObservableCollection<Rule>(rulesList);
        }

        private void LoadRecentEvents()
        {
            var events = RulesService.Instance.GetRecentEvents();
            RecentEvents = new ObservableCollection<RuleTriggerEvent>(events);
        }

        private void OnRuleTriggered(RuleTriggerEvent evt)
        {
            // Add to beginning of list
            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                RecentEvents.Insert(0, evt);
                if (RecentEvents.Count > 50)
                    RecentEvents.RemoveAt(RecentEvents.Count - 1);
            });
        }

        partial void OnRulesEngineEnabledChanged(bool value)
        {
            _settings.RulesEngineEnabled = value;
            AppSettingsService.Save(_settings);
        }

        partial void OnSoundAlertEnabledChanged(bool value)
        {
            _settings.SoundAlertEnabled = value;
            AppSettingsService.Save(_settings);
            OnPropertyChanged(nameof(CanBrowseCustomSound));
        }

        partial void OnSelectedSoundModeChanged(string value)
        {
            _settings.SoundMode = value;
            AppSettingsService.Save(_settings);
            OnPropertyChanged(nameof(CanBrowseCustomSound));
        }

        private void AddRule()
        {
            SelectedRule = null;
            IsEditingRule = true;

            // Reset edit fields
            EditRuleName = "New Rule";
            EditConditionType = RuleConditionType.CpuAbove;
            EditThresholdValue = 50;
            EditTargetProcessName = null;
            EditDurationSeconds = 5;
            EditActionType = RuleActionType.Notify;
            EditActionParameter = null;
            EditCooldownSeconds = 60;
            EditNotificationMode = RuleNotificationMode.ToastAndSound;
        }

        private void EditRule(Rule? rule)
        {
            if (rule == null) return;

            SelectedRule = rule;
            IsEditingRule = true;

            // Load edit fields from rule
            EditRuleName = rule.Name;
            EditConditionType = rule.ConditionType;
            EditThresholdValue = rule.ThresholdValue;
            EditTargetProcessName = rule.TargetProcessName;
            EditDurationSeconds = rule.DurationSeconds;
            EditActionType = rule.ActionType;
            EditActionParameter = rule.ActionParameter;
            EditCooldownSeconds = rule.CooldownSeconds;
            EditNotificationMode = rule.NotificationMode;
        }

        private void SaveRule()
        {
            Rule rule;
            if (SelectedRule != null)
            {
                // Edit existing
                rule = SelectedRule;
            }
            else
            {
                // Create new
                rule = new Rule();
                Rules.Add(rule);
            }

            rule.Name = EditRuleName;
            rule.ConditionType = EditConditionType;
            rule.ThresholdValue = EditThresholdValue;
            rule.TargetProcessName = EditTargetProcessName;
            rule.DurationSeconds = EditDurationSeconds;
            rule.ActionType = EditActionType;
            rule.ActionParameter = EditActionParameter;
            rule.CooldownSeconds = EditCooldownSeconds;
            rule.NotificationMode = EditNotificationMode;
            rule.ModifiedAt = System.DateTime.Now;

            // Save to settings
            _settings.Rules = Rules.ToList();
            AppSettingsService.Save(_settings);

            // Reset rule states since rules changed
            RulesService.Instance.ResetStates();

            IsEditingRule = false;
            SelectedRule = null;
        }

        private void CancelEdit()
        {
            IsEditingRule = false;
            SelectedRule = null;
        }

        private void DeleteRule(Rule? rule)
        {
            if (rule == null) return;

            // Show confirmation dialog
            var result = System.Windows.MessageBox.Show(
                $"Are you sure you want to delete the rule '{rule.Name}'?",
                "Confirm Delete",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (result != System.Windows.MessageBoxResult.Yes)
                return;

            Rules.Remove(rule);

            // Save to settings
            _settings.Rules = Rules.ToList();
            AppSettingsService.Save(_settings);

            // Reset rule states
            RulesService.Instance.ResetStates();
        }

        private void ToggleRuleEnabled(Rule? rule)
        {
            if (rule == null) return;

            rule.IsEnabled = !rule.IsEnabled;

            // Save to settings
            _settings.Rules = Rules.ToList();
            AppSettingsService.Save(_settings);

            OnPropertyChanged(nameof(Rules));

            // Reset rule states
            RulesService.Instance.ResetStates();
        }

        private void RefreshEvents()
        {
            LoadRecentEvents();
        }

        private void ClearEvents()
        {
            RecentEvents.Clear();
        }

        private void CopyLogsPathToClipboard()
        {
            try
            {
                System.Windows.Clipboard.SetText(LogsDirectoryPath);
                System.Windows.MessageBox.Show(
                    "Log directory path copied to clipboard!",
                    "Copied",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to copy path: {ex.Message}",
                    "Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Returns a description for a condition type.
        /// </summary>
        public string GetConditionTypeDescription(RuleConditionType type)
        {
            return type switch
            {
                RuleConditionType.CpuAbove => "CPU Usage Above",
                RuleConditionType.MemoryAbove => "Memory Usage Above",
                RuleConditionType.ProcessNameContains => "Process Name Contains",
                RuleConditionType.ProcessNameExact => "Process Name Is",
                RuleConditionType.ProcessRunning => "Process Running",
                RuleConditionType.ProcessNotRunning => "Process Not Running",
                _ => type.ToString()
            };
        }

        /// <summary>
        /// Returns a description for an action type.
        /// </summary>
        public string GetActionTypeDescription(RuleActionType type)
        {
            return type switch
            {
                RuleActionType.Notify => "Show Notification",
                RuleActionType.Log => "Log to File",
                RuleActionType.KillProcess => "Kill Process",
                RuleActionType.SuspendProcess => "Suspend Process",
                RuleActionType.SetPriority => "Set Priority",
                _ => type.ToString()
            };
        }
    }
}
