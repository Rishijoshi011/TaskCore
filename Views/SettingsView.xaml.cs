using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using TaskCore.ViewModels;
using TaskCore.Services;

namespace TaskCore.Views
{
    public partial class SettingsView : UserControl, IActivatableView
    {
        public SettingsView()
        {
            InitializeComponent();
        }

        public void OnActivated()
        {
            // Recreate ViewModel if it was disposed
            if (DataContext == null)
            {
                DataContext = new SettingsViewModel();
            }
        }

        public void OnDeactivated()
        {
            // Dispose ViewModel to free resources
            if (DataContext is IDisposable vm)
            {
                DataContext = null;
                vm.Dispose();
            }
        }

        private void RelaunchAsAdmin_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("[SettingsView] RelaunchAsAdmin_Click fired");
            var result = ElevationService.TryRelaunchAsAdminAndExit();
            Debug.WriteLine($"[SettingsView] TryRelaunchAsAdminAndExit returned: {result}");
        }
    }
}
