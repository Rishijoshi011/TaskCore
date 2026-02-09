using System.Windows;
using System.Windows.Input;
using TaskCore.Views;

namespace TaskCore
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // Default page
            Sidebar.SelectedIndex = 0;
            ContentArea.Content = new ProcessesView();
        }

        private void Sidebar_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            switch (Sidebar.SelectedIndex)
            {
                case 0:
                    ContentArea.Content = new ProcessesView();
                    break;
                case 1:
                    ContentArea.Content = new GraphsView();
                    break;
                case 2:
                    ContentArea.Content = new DetailsView();
                    break;
                case 3:
                    ContentArea.Content = new SettingsView();
                    break;
            }
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }
        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
                WindowState = WindowState.Normal;
            else
                WindowState = WindowState.Maximized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}
