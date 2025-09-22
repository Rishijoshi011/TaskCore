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
        private readonly ToggleButton cpuHeader;
        private List<ProcessInfo> allProcesses;
        private bool _isChangingSortHeader = false;

        bool isNameAsc = true;
        bool isPidAsc = true;
        bool isMemAsc = true;
        bool isCpuAsc = true;

        private const double NAME_COLUMN_WIDTH = 320.0;
        private const double PID_COLUMN_WIDTH = 100.0;
        private const double MEMORY_COLUMN_WIDTH = 140.0;
        private const double CPU_COLUMN_WIDTH = 100.0;

        public TreeView processTreeView { get; }
        public Button Btn_end_task { get; }
        public object ProcessTreeView { get; internal set; }

        public Color bg_color = Color.FromRgb(30, 30, 35);

        public ProcessListPanel()
        {
            var grid = new Grid
            {
                Margin = new Thickness(16),
                Background = new SolidColorBrush(bg_color)
            };

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var headerGrid = new Grid
            {
                Background = new SolidColorBrush(bg_color),
                Margin = new Thickness(0, 0, 0, 2),
                Height = 40
            };

            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(NAME_COLUMN_WIDTH) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PID_COLUMN_WIDTH) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(MEMORY_COLUMN_WIDTH) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CPU_COLUMN_WIDTH) });

            // vertical separators between columns
            for (int i = 1; i < 4; i++)
            {
                var separator = new Border
                {
                    Width = 1,
                    Background = new SolidColorBrush(bg_color),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(-1, 5, 0, 5)
                };
                Grid.SetColumn(separator, i);
                headerGrid.Children.Add(separator);
            }

            var headerMargin = new Thickness(12, 8, 8, 8);

            var headerButtonStyle = CreateHeaderButtonStyle();

            // Name header button
            nameHeader = new ToggleButton
            {
                Content = "Name ▲",
                Style = headerButtonStyle,
                Margin = headerMargin,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            };
            nameHeader.IsChecked = true; // Default selection
            Grid.SetColumn(nameHeader, 0);
            headerGrid.Children.Add(nameHeader);

            // PID header button
            pidHeader = new ToggleButton
            {
                Content = "PID",
                Style = headerButtonStyle,
                Margin = headerMargin,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            };
            Grid.SetColumn(pidHeader, 1);
            headerGrid.Children.Add(pidHeader);

            // Memory header button
            MemHeader = new ToggleButton
            {
                Content = "Memory Usage",
                Style = headerButtonStyle,
                Margin = headerMargin,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            };
            Grid.SetColumn(MemHeader, 2);
            headerGrid.Children.Add(MemHeader);

            // CPU header button
            cpuHeader = new ToggleButton
            {
                Content = "CPU Usage",
                Style = headerButtonStyle,
                Margin = headerMargin,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            };
            Grid.SetColumn(cpuHeader, 3);
            headerGrid.Children.Add(cpuHeader);

            // Event handlers
            nameHeader.Checked += (s, e) => OnSortHeaderChecked("Name");
            pidHeader.Checked += (s, e) => OnSortHeaderChecked("PID");
            MemHeader.Checked += (s, e) => OnSortHeaderChecked("Memory");
            cpuHeader.Checked += (s, e) => OnSortHeaderChecked("CPU");

            Grid.SetRow(headerGrid, 0);
            grid.Children.Add(headerGrid);

            var treeView = new TreeView
            {
                ItemsSource = ((TaskCore.MainWindow)Application.Current.MainWindow).Processes,
                Background = new SolidColorBrush(bg_color),
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 2, 0, 0)
            };

            // TreeView styling
            treeView.Resources.Add(typeof(TreeViewItem), CreateTreeViewItemStyle());

            // Build display list
            allProcesses = ((TaskCore.MainWindow)Application.Current.MainWindow).Processes.ToList();
            var grouped = GetGroupedSortedFilteredDisplayList("Name", true, "");
            treeView.ItemsSource = grouped;

            var processTemplate = CreateProcessDataTemplate();
            var groupTemplate = CreateGroupDataTemplate(processTemplate);

            treeView.ItemTemplate = null;
            treeView.ItemTemplateSelector = new ProcessTemplateSelector(groupTemplate, processTemplate);

            Grid.SetRow(treeView, 1);
            grid.Children.Add(treeView);

            // End task button
            Btn_end_task = new Button
            {
                Content = "End Task",
                Width = 100,
                Height = 32,
                Margin = new Thickness(0, 8, 16, 8),
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = new SolidColorBrush(Color.FromArgb(180, 255, 50, 69)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                BorderThickness = new Thickness(0)
            };

            // Add hover effect to End Task button
            Btn_end_task.MouseEnter += (s, e) => 
                Btn_end_task.Background = new SolidColorBrush(Color.FromRgb(171, 8, 137));
            Btn_end_task.MouseLeave += (s, e) => 
                Btn_end_task.Background = new SolidColorBrush(Color.FromArgb(180, 255, 50, 69));

            Btn_end_task.Click += (s, e) =>
            {
                if (Application.Current.MainWindow is TaskCore.MainWindow mw)
                    mw.EndTaskButton_Click(s, e);
            };

            Grid.SetRow(Btn_end_task, 2);
            grid.Children.Add(Btn_end_task);

            processTreeView = treeView;
            Content = grid;
        }

        private Style CreateHeaderButtonStyle()
        {
            var style = new Style(typeof(ToggleButton));

            // Base style
            style.Setters.Add(new Setter(ForegroundProperty, Brushes.LightGray));
            style.Setters.Add(new Setter(FontWeightProperty, FontWeights.SemiBold));
            style.Setters.Add(new Setter(CursorProperty, System.Windows.Input.Cursors.Hand));

            // Minimal custom template
            var template = new ControlTemplate(typeof(ToggleButton));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            border.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });

            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(contentPresenter);

            template.VisualTree = border;
            style.Setters.Add(new Setter(TemplateProperty, template));

            // Default background
            style.Setters.Add(new Setter(BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(BorderBrushProperty, Brushes.Transparent));

            // Hover
            var hoverTrigger = new Trigger { Property = IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush(Color.FromRgb(33, 92, 116))));
            hoverTrigger.Setters.Add(new Setter(ForegroundProperty, Brushes.White));
            style.Triggers.Add(hoverTrigger);

            // Checked
            var checkedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            checkedTrigger.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush(Color.FromRgb(33, 92, 116))));
            checkedTrigger.Setters.Add(new Setter(ForegroundProperty, Brushes.White));
            style.Triggers.Add(checkedTrigger);

            return style;
        }



        private Style CreateTreeViewItemStyle()
        {
            var style = new Style(typeof(TreeViewItem));
            style.Setters.Add(new Setter(BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(PaddingProperty, new Thickness(0, 2, 0, 2)));

            // Hover effect for rows
            var hoverTrigger = new Trigger { Property = IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(BackgroundProperty, 
                new SolidColorBrush(Color.FromRgb(50, 54, 61))));
            style.Triggers.Add(hoverTrigger);

            // Selected row effect
            var selectedTrigger = new Trigger { Property = TreeViewItem.IsSelectedProperty, Value = true };
            selectedTrigger.Setters.Add(new Setter(BackgroundProperty, 
                new SolidColorBrush(Color.FromRgb(0, 120, 215))));
            selectedTrigger.Setters.Add(new Setter(ForegroundProperty, Brushes.White));
            style.Triggers.Add(selectedTrigger);

            return style;
        }

        private DataTemplate CreateProcessDataTemplate()
        {
            var template = new DataTemplate(typeof(ProcessInfo));
            var spFactory = new FrameworkElementFactory(typeof(StackPanel));
            spFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            spFactory.SetValue(MarginProperty, new Thickness(0, 1, 0, 1));

            // Name column with proper TreeView indentation
            var nameText = new FrameworkElementFactory(typeof(TextBlock));
            nameText.SetBinding(TextBlock.TextProperty, new Binding("Name"));
            nameText.SetValue(WidthProperty, NAME_COLUMN_WIDTH);
            nameText.SetValue(ForegroundProperty, Brushes.White);
            nameText.SetValue(FontSizeProperty, 11.0);
            nameText.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            nameText.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            nameText.SetValue(MarginProperty, new Thickness(20, 0, 8, 0));
            spFactory.AppendChild(nameText);

            // PID column
            var pidText = new FrameworkElementFactory(typeof(TextBlock));
            pidText.SetBinding(TextBlock.TextProperty, new Binding("Id"));
            pidText.SetValue(WidthProperty, PID_COLUMN_WIDTH);
            pidText.SetValue(ForegroundProperty, new SolidColorBrush(Color.FromRgb(180, 180, 180)));
            pidText.SetValue(FontSizeProperty, 11.0);
            pidText.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
            pidText.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            pidText.SetValue(MarginProperty, new Thickness(8, 0, 8, 0));
            spFactory.AppendChild(pidText);

            // Memory column
            var memText = new FrameworkElementFactory(typeof(TextBlock));
            memText.SetBinding(TextBlock.TextProperty, new Binding("Memory") { StringFormat = "{0:F2} MB" });
            memText.SetValue(WidthProperty, MEMORY_COLUMN_WIDTH);
            memText.SetValue(ForegroundProperty, new SolidColorBrush(Color.FromRgb(180, 180, 180)));
            memText.SetValue(FontSizeProperty, 11.0);
            memText.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
            memText.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            memText.SetValue(MarginProperty, new Thickness(8, 0, 8, 0));
            spFactory.AppendChild(memText);

            // CPU column
            var cpuText = new FrameworkElementFactory(typeof(TextBlock));
            cpuText.SetBinding(TextBlock.TextProperty, new Binding("Cpu") { StringFormat = "{0:F2}%" });
            cpuText.SetValue(WidthProperty, CPU_COLUMN_WIDTH);
            cpuText.SetValue(ForegroundProperty, new SolidColorBrush(Color.FromRgb(180, 180, 180)));
            cpuText.SetValue(FontSizeProperty, 11.0);
            cpuText.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
            cpuText.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            cpuText.SetValue(MarginProperty, new Thickness(8, 0, 8, 0));
            spFactory.AppendChild(cpuText);

            template.VisualTree = spFactory;
            return template;
        }

        private HierarchicalDataTemplate CreateGroupDataTemplate(DataTemplate processTemplate)
        {
            var groupTemplate = new HierarchicalDataTemplate(typeof(ProcessGroup));
            var spFactory = new FrameworkElementFactory(typeof(StackPanel));
            spFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            spFactory.SetValue(BackgroundProperty, new SolidColorBrush(Color.FromRgb(40, 44, 50)));
            spFactory.SetValue(MarginProperty, new Thickness(0, 1, 0, 1));

            // Group name
            var groupNameText = new FrameworkElementFactory(typeof(TextBlock));
            groupNameText.SetBinding(TextBlock.TextProperty, new Binding("GroupName"));
            groupNameText.SetValue(WidthProperty, NAME_COLUMN_WIDTH);
            groupNameText.SetValue(FontWeightProperty, FontWeights.Bold);
            groupNameText.SetValue(ForegroundProperty, new SolidColorBrush(Color.FromRgb(255, 255, 255)));
            groupNameText.SetValue(FontSizeProperty, 11.0);
            groupNameText.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            groupNameText.SetValue(MarginProperty, new Thickness(8, 2, 8, 2));
            spFactory.AppendChild(groupNameText);

            // Empty PID column for groups
            var groupPidText = new FrameworkElementFactory(typeof(TextBlock));
            groupPidText.SetValue(TextBlock.TextProperty, "");
            groupPidText.SetValue(WidthProperty, PID_COLUMN_WIDTH);
            spFactory.AppendChild(groupPidText);

            // Total memory
            var groupMemText = new FrameworkElementFactory(typeof(TextBlock));
            groupMemText.SetBinding(TextBlock.TextProperty, new Binding("TotalMemory") { StringFormat = "{0:F2} MB" });
            groupMemText.SetValue(WidthProperty, MEMORY_COLUMN_WIDTH);
            groupMemText.SetValue(ForegroundProperty, new SolidColorBrush(Color.FromRgb(200, 200, 200)));
            groupMemText.SetValue(FontSizeProperty, 11.0);
            groupMemText.SetValue(FontWeightProperty, FontWeights.SemiBold);
            groupMemText.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
            groupMemText.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            groupMemText.SetValue(MarginProperty, new Thickness(8, 2, 8, 2));
            spFactory.AppendChild(groupMemText);

            // Total CPU
            var groupCpuText = new FrameworkElementFactory(typeof(TextBlock));
            groupCpuText.SetBinding(TextBlock.TextProperty, new Binding("TotalCpu") { StringFormat = "{0:F2}%" });
            groupCpuText.SetValue(WidthProperty, CPU_COLUMN_WIDTH);
            groupCpuText.SetValue(ForegroundProperty, new SolidColorBrush(Color.FromRgb(200, 200, 200)));
            groupCpuText.SetValue(FontSizeProperty, 11.0);
            groupCpuText.SetValue(FontWeightProperty, FontWeights.SemiBold);
            groupCpuText.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
            groupCpuText.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            groupCpuText.SetValue(MarginProperty, new Thickness(8, 2, 8, 2));
            spFactory.AppendChild(groupCpuText);

            groupTemplate.VisualTree = spFactory;
            groupTemplate.ItemsSource = new Binding("Processes");
            groupTemplate.ItemTemplate = processTemplate;

            return groupTemplate;
        }
        private void OnSortHeaderChecked(string column)
        {
            if (_isChangingSortHeader) return;
            _isChangingSortHeader = true;

            // Uncheck all headers first
            nameHeader.IsChecked = false;
            pidHeader.IsChecked = false;
            MemHeader.IsChecked = false;
            cpuHeader.IsChecked = false;

            // Check the selected header
            switch (column)
            {
                case "Name":
                    nameHeader.IsChecked = true;
                    isNameAsc = !isNameAsc;
                    break;
                case "PID":
                    pidHeader.IsChecked = true;
                    isPidAsc = !isPidAsc;
                    break;
                case "Memory":
                    MemHeader.IsChecked = true;
                    isMemAsc = !isMemAsc;
                    break;
                case "CPU":
                    cpuHeader.IsChecked = true;
                    isCpuAsc = !isCpuAsc;
                    break;
            }

            UpdateHeaderContent(column);

            string filterText = "";
            processTreeView.ItemsSource = GetGroupedSortedFilteredDisplayList(column,
                column == "Name" ? isNameAsc : column == "PID" ? isPidAsc : column == "Memory" ? isMemAsc : isCpuAsc,
                filterText);

            _isChangingSortHeader = false;
        }

        private void UpdateHeaderContent(string activeColumn)
        {
            nameHeader.Content = "Name";
            pidHeader.Content = "PID";
            MemHeader.Content = "Memory Usage";
            cpuHeader.Content = "CPU Usage";

            string indicator;
            switch (activeColumn)
            {
                case "Name":
                    indicator = isNameAsc ? " ▲" : " ▼";
                    nameHeader.Content = "Name" + indicator;
                    break;
                case "PID":
                    indicator = isPidAsc ? " ▲" : " ▼";
                    pidHeader.Content = "PID" + indicator;
                    break;
                case "Memory":
                    indicator = isMemAsc ? " ▲" : " ▼";
                    MemHeader.Content = "Memory Usage" + indicator;
                    break;
                case "CPU":
                    indicator = isCpuAsc ? " ▲" : " ▼";
                    cpuHeader.Content = "CPU Usage" + indicator;
                    break;
            }
        }

        private List<object> GetGroupedSortedFilteredDisplayList(string column, bool ascending, string filter)
        {
            IEnumerable<ProcessInfo> sorted = column switch
            {
                "Name" => ascending
                    ? allProcesses.OrderBy(p => p.Name)
                    : allProcesses.OrderByDescending(p => p.Name),
                "PID" => ascending
                    ? allProcesses.OrderBy(p => p.Id)
                    : allProcesses.OrderByDescending(p => p.Id),
                "Memory" => ascending
                    ? allProcesses.OrderBy(p => p.Memory)
                    : allProcesses.OrderByDescending(p => p.Memory),
                "CPU" => ascending
                    ? allProcesses.OrderBy(p => p.Cpu)
                    : allProcesses.OrderByDescending(p => p.Cpu),
                _ => allProcesses
            };

            if (!string.IsNullOrWhiteSpace(filter))
            {
                sorted = sorted.Where(p => p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));
            }

            var grouped = sorted
                .GroupBy(p => p.Name)
                .Select(g => g.Count() > 1
                    ? (object)new ProcessGroup
                    {
                        GroupName = g.Key,
                        Processes = g.ToList()
                    }
                    : (object)g.First())
                .ToList();

            return grouped;
        }
    }

    public class ProcessGroup
    {
        public string GroupName { get; set; }
        public List<ProcessInfo> Processes { get; set; }
        public float TotalMemory => Processes?.Sum(p => p.Memory) ?? 0;
        public float TotalCpu => Processes?.Sum(p => p.Cpu) ?? 0;
    }

    public class ProcessTemplateSelector : DataTemplateSelector
    {
        private readonly DataTemplate _groupTemplate;
        private readonly DataTemplate _processTemplate;

        public ProcessTemplateSelector(DataTemplate groupTemplate, DataTemplate processTemplate)
        {
            _groupTemplate = groupTemplate;
            _processTemplate = processTemplate;
        }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (item is ProcessGroup) return _groupTemplate;
            return _processTemplate;
        }
    }
}