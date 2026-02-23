using System;
using Microsoft.Toolkit.Uwp.Notifications;

namespace TaskCore.Services
{
    /// <summary>
    /// Service for displaying Windows Toast notifications.
    /// </summary>
    public static class ToastNotificationService
    {
        private const string AppId = "TaskCore";

        /// <summary>
        /// Shows a toast notification for a rule trigger with clear action description.
        /// </summary>
        public static void ShowRuleNotification(string ruleName, string processName, string action, string condition, string? actionResult = null)
        {
            try
            {
                // Build clear, descriptive title and body
                var (title, emoji) = action switch
                {
                    "KillProcess" => ("Process Killed", "🔴"),
                    "SetPriority" => ("Priority Changed", "⚡"),
                    "Log" => ("Event Logged", "📝"),
                    _ => ("Rule Triggered", "🔔")
                };

                // Body explains what happened to which process and why
                var body = $"{processName} {GetActionVerb(action)} because {condition}";
                
                // Add result if available
                if (!string.IsNullOrWhiteSpace(actionResult) && actionResult != "Notification sent" && actionResult != "Silent")
                    body += $" | {actionResult}";

                // Use explicit toast XML for better compatibility
                var toastXml = new Windows.Data.Xml.Dom.XmlDocument();
                var titleText = $"{emoji} {title}: {ruleName}";
                toastXml.LoadXml($"""
                    <toast>
                        <visual>
                            <binding template="ToastText02">
                                <text id="1">{EscapeXml(titleText)}</text>
                                <text id="2">{EscapeXml(body)}</text>
                            </binding>
                        </visual>
                    </toast>
                    """);

                var toast = new Windows.UI.Notifications.ToastNotification(toastXml);
                Windows.UI.Notifications.ToastNotificationManager.CreateToastNotifier(AppId).Show(toast);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Toast notification failed: {ex.Message}");
            }
        }

        private static string EscapeXml(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

        private static string GetActionVerb(string action) => action switch
        {
            "KillProcess" => "was killed",
            "SetPriority" => "priority was changed",
            "Log" => "was logged",
            _ => "triggered a notification"
        };

        /// <summary>
        /// Shows a generic notification.
        /// </summary>
        public static void ShowNotification(string title, string message, string? attribution = null)
        {
            try
            {
                var builder = new ToastContentBuilder()
                    .AddText(title)
                    .AddText(message);

                if (!string.IsNullOrWhiteSpace(attribution))
                    builder.AddAttributionText(attribution);

                builder.Show();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Toast notification failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Shows a high priority/warning notification.
        /// </summary>
        public static void ShowWarning(string title, string message)
        {
            try
            {
                new ToastContentBuilder()
                    .AddArgument("action", "viewProcesses")
                    .AddText($"⚠️ {title}")
                    .AddText(message)
                    .SetToastScenario(ToastScenario.Reminder)
                    .Show();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Toast notification failed: {ex.Message}");
            }
        }
    }
}
