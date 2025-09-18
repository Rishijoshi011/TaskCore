using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace TaskCore.Views
{
    public class ProcessListPanel : UserControl
    {
        private readonly ToggleButton nameHeader;
        private readonly ToggleButton pidHeader;
        private readonly ToggleButton MemHeader;
       

        private List<ProcessInfo> allProcesses;
        private bool _isChangingSortHeader = false;
        // Sorting State
        bool isNameAsc = true;
        bool isPidAsc = true;
        bool isMemAsc = true;


        // geeting the public ProcessTreeView property from MainWindow
        public TreeView processTreeView { get; }
        public Button Btn_end_task { get; }
        public object ProcessTreeView { get; internal set; }

        public ProcessListPanel()
        {
            var grid = new Grid
            {
                Margin = new Thickness(16),
                Background = new SolidColorBrush(Color.FromRgb(35, 39, 46))
            };

            // Defining header here
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Header row
            var headerPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Background = new SolidColorBrush(Color.FromRgb(30, 34, 40)),
                Margin = new Thickness(2, 2, 2, 2),
            };

           

            var glb_Margin = new Thickness(8, 4, 8, 4);

            //styles for name col button 
            var nameStyle = new Style(typeof(ToggleButton));
            nameStyle.Setters.Add(new Setter(ToggleButton.BackgroundProperty, Brushes.Transparent));
            nameStyle.Setters.Add(new Setter(ToggleButton.ForegroundProperty, Brushes.LightGray));
            nameStyle.Setters.Add(new Setter(ToggleButton.BorderBrushProperty, Brushes.Transparent));
            nameStyle.Setters.Add(new Setter(ToggleButton.FontWeightProperty, FontWeights.Bold));
            nameStyle.Setters.Add(new Setter(ToggleButton.CursorProperty, System.Windows.Input.Cursors.Hand));
            // Hover effect trigger of name button
            var nameTrigger = new Trigger { Property = ToggleButton.IsMouseOverProperty, Value = true };
            nameTrigger.Setters.Add(new Setter(ToggleButton.BackgroundProperty, new SolidColorBrush(Color.FromRgb(155, 175, 214))));
            nameTrigger.Setters.Add(new Setter(ToggleButton.ForegroundProperty, Brushes.Black));
            nameStyle.Triggers.Add(nameTrigger);
            // Toggled (IsChecked) effect trigger
            var nameCheckedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            nameCheckedTrigger.Setters.Add(new Setter(ToggleButton.BackgroundProperty, new SolidColorBrush(Color.FromRgb(50, 120, 200))));
            nameCheckedTrigger.Setters.Add(new Setter(ToggleButton.ForegroundProperty, Brushes.Black));
            nameStyle.Triggers.Add(nameCheckedTrigger);
            // Name header button
            nameHeader = new ToggleButton
            {
                Content = "Name",
                Width = 200,
                Style = nameStyle,
                Margin = glb_Margin
            };
            // Set the style for the name header button
            nameHeader.Style = nameStyle;

            //styles for pid button
            var pidStyle = new Style(typeof(ToggleButton));
            pidStyle.Setters.Add(new Setter(ToggleButton.BackgroundProperty, Brushes.Transparent));
            pidStyle.Setters.Add(new Setter(ToggleButton.ForegroundProperty, Brushes.LightGray));
            pidStyle.Setters.Add(new Setter(ToggleButton.BorderBrushProperty, Brushes.Transparent));
            pidStyle.Setters.Add(new Setter(ToggleButton.FontWeightProperty, FontWeights.Bold));
            pidStyle.Setters.Add(new Setter(ToggleButton.CursorProperty, System.Windows.Input.Cursors.Hand));
            // Hover effect trigger of pid ToggleButton
            var pidTrigger = new Trigger { Property = ToggleButton.IsMouseOverProperty, Value = true };
            pidTrigger.Setters.Add(new Setter(ToggleButton.BackgroundProperty, new SolidColorBrush(Color.FromRgb(50, 60, 80))));
            pidTrigger.Setters.Add(new Setter(ToggleButton.ForegroundProperty, Brushes.Black));
            pidStyle.Triggers.Add(pidTrigger);
            // Toggled (IsChecked) effect trigger
            var pidCheckedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            pidCheckedTrigger.Setters.Add(new Setter(ToggleButton.BackgroundProperty, new SolidColorBrush(Color.FromRgb(50, 120, 200))));
            pidCheckedTrigger.Setters.Add(new Setter(ToggleButton.ForegroundProperty, Brushes.Black));
            pidStyle.Triggers.Add(pidCheckedTrigger);
            // PID header button
            pidHeader = new ToggleButton
            {
                Content = "PID",
                Width = 200,
                Style = pidStyle,
                Margin = glb_Margin
            };
            //set the style for the pid header button
            pidHeader.Style = pidStyle;

            // styles for memory usage button
            var MemStyle = new Style(typeof(ToggleButton));
            MemStyle.Setters.Add(new Setter(ToggleButton.BackgroundProperty, Brushes.Transparent));
            MemStyle.Setters.Add(new Setter(ToggleButton.ForegroundProperty, Brushes.LightGray));
            MemStyle.Setters.Add(new Setter(ToggleButton.BorderBrushProperty, Brushes.Transparent));
            MemStyle.Setters.Add(new Setter(ToggleButton.FontWeightProperty, FontWeights.Bold));
            MemStyle.Setters.Add(new Setter(ToggleButton.CursorProperty, System.Windows.Input.Cursors.Hand));
            // Hover effect trigger of Memory usage button
            var MemTrigger = new Trigger { Property = ToggleButton.IsMouseOverProperty, Value = true };
            MemTrigger.Setters.Add(new Setter(ToggleButton.BackgroundProperty, new SolidColorBrush(Color.FromRgb(50, 60, 80))));
            MemTrigger.Setters.Add(new Setter(ToggleButton.ForegroundProperty, Brushes.Black));
            MemStyle.Triggers.Add(MemTrigger);
            // Toggled (IsChecked) effect trigger
            var MemCheckedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            MemCheckedTrigger.Setters.Add(new Setter(ToggleButton.BackgroundProperty, new SolidColorBrush(Color.FromRgb(50, 120, 200))));
            MemCheckedTrigger.Setters.Add(new Setter(ToggleButton.ForegroundProperty, Brushes.Black));
            MemStyle.Triggers.Add(MemCheckedTrigger);
            // Memory header button
            MemHeader = new ToggleButton
            {
                Content = "Memory Usage",
                Width = 200,
                Style = MemStyle,
                Margin = glb_Margin
            };
            //Set the style for the memory header button
            MemHeader.Style = MemStyle;

            // Toggle buttons Logic --- 
            nameHeader.IsChecked = true; // Default sort by Name
            nameHeader.Content = "Name ▲"; // Default sort direction (Asc)

            nameHeader.Checked += (s, e) => OnSortHeaderChecked("Name");
            pidHeader.Checked += (s, e) => OnSortHeaderChecked("PID");
            MemHeader.Checked += (s, e) => OnSortHeaderChecked("Memory");

            // add them here to the header panel
            headerPanel.Children.Add(nameHeader);
            headerPanel.Children.Add(new TextBlock { Width = 16 }); // Spacer
            headerPanel.Children.Add(pidHeader);
            headerPanel.Children.Add(MemHeader);

            Grid.SetRow(headerPanel, 0);
            grid.Children.Add(headerPanel);

            // TreeView row
            var treeView = new TreeView
            {
                ItemsSource = ((TaskCore.MainWindow)Application.Current.MainWindow).Processes,
                Background = new SolidColorBrush(Color.FromRgb(35, 39, 46)),
            };

            // DataTemplate for TreeViewItem with columns
            var dataTemplate = new HierarchicalDataTemplate(typeof(TaskCore.ProcessInfo));
            var spFactory = new FrameworkElementFactory(typeof(StackPanel));
            spFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            // Name column
            var nameText = new FrameworkElementFactory(typeof(TextBlock));
            nameText.SetBinding(TextBlock.TextProperty, new Binding("Name"));
            nameText.SetValue(TextBlock.WidthProperty, 200.0);
            nameText.SetValue(TextBlock.ForegroundProperty, Brushes.White);
            spFactory.AppendChild(nameText);

            // Spacer
            var pidText = new FrameworkElementFactory(typeof(TextBlock));
            pidText.SetValue(TextBlock.TextProperty, "                                ");
            spFactory.AppendChild(pidText);

            // PID column
            var idText = new FrameworkElementFactory(typeof(TextBlock));
            idText.SetBinding(TextBlock.TextProperty, new Binding("Id"));
            idText.SetValue(TextBlock.WidthProperty, 80.0);
            idText.SetValue(TextBlock.ForegroundProperty, Brushes.Gray);
            spFactory.AppendChild(idText);

            var MemText = new FrameworkElementFactory(typeof(TextBlock));
            MemText.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
            MemText.SetValue(TextBlock.TextProperty, new Binding("Memory")
            {
                StringFormat = "                        {0:F2} MB"
            });
            MemText.SetValue(TextBlock.WidthProperty, 180.0);
            MemText.SetValue(TextBlock.ForegroundProperty, Brushes.Gray);
            spFactory.AppendChild(MemText);

            dataTemplate.VisualTree = spFactory;
            treeView.ItemTemplate = dataTemplate;

            Grid.SetRow(treeView, 1);
            grid.Children.Add(treeView);

            // End task button 
            Btn_end_task = new Button
            {
                Content = "End Task",
                Width = 100,
                Height = 30,
                Margin = glb_Margin,
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = new SolidColorBrush(Color.FromRgb(255, 69, 58)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold
            };

            Btn_end_task.Click += (s, e) =>
            {
                if (Application.Current.MainWindow is TaskCore.MainWindow mw)
                    mw.EndTaskButton_Click(s, e);
            };

            Grid.SetRow(Btn_end_task, 2);
            grid.Children.Add(Btn_end_task);

            // assigning tree view to the Content of the UserControl
            processTreeView = treeView;

            Content = grid;

            allProcesses = ((TaskCore.MainWindow)Application.Current.MainWindow).Processes.ToList();
        }

        // Centralized handler:
        private void OnSortHeaderChecked(string column)
        {
            if (_isChangingSortHeader) return;
            _isChangingSortHeader = true;

            // Uncheck all headers except the one clicked
            nameHeader.IsChecked = column == "Name";
            pidHeader.IsChecked = column == "PID";
            MemHeader.IsChecked = column == "Memory";

            // Toggle sort direction for the selected column
            switch (column)
            {
                case "Name":
                    isNameAsc = !isNameAsc;
                    SortByColumn("Name", isNameAsc);
                    nameHeader.Content = isNameAsc ? "Name ▲" : "Name ▼";
                    break;
                case "PID":
                    isPidAsc = !isPidAsc;
                    SortByColumn("PID", isPidAsc);
                    pidHeader.Content = isPidAsc ? "PID ▲" : "PID ▼";
                    break;
                case "Memory":
                    isMemAsc = !isMemAsc;
                    SortByColumn("Memory", isMemAsc);
                    MemHeader.Content = isMemAsc ? "Memory Usage ▲" : "Memory Usage ▼";
                    break;
            }

            // Reset other headers' content
            if (column != "Name") nameHeader.Content = "Name";
            if (column != "PID") pidHeader.Content = "PID";
            if (column != "Memory") MemHeader.Content = "Memory Usage";

            _isChangingSortHeader = false;
        }

        // Generalized sort method
        private void SortByColumn(string column, bool ascending)
        {
            List<ProcessInfo> sorted = column switch
            {
                "Name" => ascending
                    ? allProcesses.OrderBy(p => p.Name).ToList()
                    : allProcesses.OrderByDescending(p => p.Name).ToList(),
                "PID" => ascending
                    ? allProcesses.OrderBy(p => p.Id).ToList()
                    : allProcesses.OrderByDescending(p => p.Id).ToList(),
                "Memory" => ascending
                    ? allProcesses.OrderBy(p => p.Memory).ToList()
                    : allProcesses.OrderByDescending(p => p.Memory).ToList(),
                // Add more columns here as needed
                _ => allProcesses
            };

            allProcesses = sorted;
            processTreeView.ItemsSource = null;
            processTreeView.ItemsSource = allProcesses;
        }
    }
}
