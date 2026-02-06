using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using TaskCore.Models;

namespace TaskCore.Views
{
    public class ProcessListPanel : UserControl
    {
        public event Action<ProcessInfo>? EndTaskRequested;

        public ProcessInfo? SelectedProcess { get; set; }

        public ProcessListPanel()
        {
            Background = new SolidColorBrush(Color.FromRgb(24, 24, 28));

            var root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var grid = BuildGrid();
            Grid.SetRow(grid, 0);
            root.Children.Add(grid);

            var endTask = BuildEndTaskButton();
            Grid.SetRow(endTask, 1);
            root.Children.Add(endTask);

            Content = root;
        }

        // ================= GRID =================

        private DataGrid BuildGrid()
        {
            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.None,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserResizeRows = false,
                SelectionMode = DataGridSelectionMode.Single,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                EnableRowVirtualization = true,
                EnableColumnVirtualization = true,

                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                RowHeight = 32
            };

            // Kill default blue selection
            grid.Resources[SystemColors.HighlightBrushKey] =
                new SolidColorBrush(Color.FromRgb(60, 60, 65));
            grid.Resources[SystemColors.HighlightTextBrushKey] = Brushes.White;

            // Bindings
            grid.SetBinding(ItemsControl.ItemsSourceProperty,
                new Binding("ProcessesView"));
            grid.SetBinding(DataGrid.SelectedItemProperty,
                new Binding(nameof(SelectedProcess))
                {
                    Source = this,
                    Mode = BindingMode.TwoWay
                });

            grid.RowStyle = CreateRowStyle();
            grid.CellStyle = CreateCellStyle();
            grid.ColumnHeaderStyle = CreateHeaderStyle();

            // Columns
            grid.Columns.Add(MakeColumn("Name", "Name", 260));
            grid.Columns.Add(MakeColumn("PID", "Id", 90, TextAlignment.Right));
            grid.Columns.Add(MakeColumn("Memory (MB)", "Memory", 120, TextAlignment.Right, "F2"));
            grid.Columns.Add(MakeColumn("CPU (%)", "Cpu", 100, TextAlignment.Right, "F1"));

            // Group headers
            grid.GroupStyle.Add(new GroupStyle
            {
                HeaderTemplate = CreateGroupHeaderTemplate(),
                HidesIfEmpty = true
            });

            return grid;
        }

        // ================= STYLES =================

        private static Style CreateRowStyle()
        {
            var style = new Style(typeof(DataGridRow));
            style.Setters.Add(new Setter(BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(ForegroundProperty, Brushes.Gainsboro));
            style.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0)));

            style.Triggers.Add(new Trigger
            {
                Property = DataGridRow.IsMouseOverProperty,
                Value = true,
                Setters =
                {
                    new Setter(BackgroundProperty,
                        new SolidColorBrush(Color.FromRgb(45,45,50)))
                }
            });

            style.Triggers.Add(new Trigger
            {
                Property = DataGridRow.IsSelectedProperty,
                Value = true,
                Setters =
                {
                    new Setter(BackgroundProperty,
                        new SolidColorBrush(Color.FromRgb(70,70,75))),
                    new Setter(ForegroundProperty, Brushes.White)
                }
            });

            return style;
        }

        private static Style CreateCellStyle()
        {
            var style = new Style(typeof(DataGridCell));
            style.Setters.Add(new Setter(BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(PaddingProperty, new Thickness(6, 0, 6, 0)));
            return style;
        }

        private static Style CreateHeaderStyle()
        {
            var style = new Style(typeof(DataGridColumnHeader));
            style.Setters.Add(new Setter(BackgroundProperty,
                new SolidColorBrush(Color.FromRgb(32, 32, 36))));
            style.Setters.Add(new Setter(ForegroundProperty, Brushes.White));
            style.Setters.Add(new Setter(FontWeightProperty, FontWeights.SemiBold));
            style.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(PaddingProperty, new Thickness(6)));
            return style;
        }

        // ================= BUTTON =================

        private Button BuildEndTaskButton()
        {
            var btn = new Button
            {
                Content = "End Task",
                Width = 120,
                Height = 34,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(200, 60, 60)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };

            btn.Click += (_, _) =>
            {
                if (SelectedProcess != null)
                    EndTaskRequested?.Invoke(SelectedProcess);
            };

            return btn;
        }

        // ================= HELPERS =================

        private static DataGridTextColumn MakeColumn(
            string header,
            string binding,
            double width,
            TextAlignment align = TextAlignment.Left,
            string? format = null)
        {
            return new DataGridTextColumn
            {
                Header = header,
                Width = width,
                Binding = new Binding(binding)
                {
                    StringFormat = format == null ? null : $"{{0:{format}}}"
                },
                ElementStyle = new Style(typeof(TextBlock))
                {
                    Setters =
                    {
                        new Setter(TextBlock.TextAlignmentProperty, align),
                        new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center)
                    }
                }
            };
        }

        private static DataTemplate CreateGroupHeaderTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty,
                new SolidColorBrush(Color.FromRgb(36, 36, 40)));
            border.SetValue(Border.PaddingProperty, new Thickness(8, 4, 8, 4));
            border.SetValue(Border.MarginProperty, new Thickness(0, 6, 0, 2));

            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetValue(TextBlock.ForegroundProperty, Brushes.White);
            text.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            text.SetBinding(TextBlock.TextProperty,
                new Binding("Name") { StringFormat = "{0}" });

            border.AppendChild(text);

            return new DataTemplate { VisualTree = border };
        }
    }
}
