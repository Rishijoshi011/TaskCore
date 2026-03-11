using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TaskCore.ViewModels;
using TaskCore.Services;
using TaskCore.Models;

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
            TaskCoreLogger.Info("elevation", "RelaunchAsAdmin_Click fired from SettingsView");
            var result = ElevationService.TryRelaunchAsAdminAndExit();
            TaskCoreLogger.Info("elevation", $"TryRelaunchAsAdminAndExit returned: {result}");
        }

        private void ProcessRow_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ProcessItem item)
            {
                if (DataContext is ProcessesViewModel vm)
                {
                    vm.SelectedProcess = item;
                }
            }
        }
    }
}
