using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace TaskCore.Views
{
    public partial class DetailsView : UserControl
    {
        public DetailsView()
        {
            InitializeComponent();
            Loaded += DetailsView_Loaded;
        }

        private void DetailsView_Loaded(object sender, RoutedEventArgs e)
        {
            AttachToViewModel(DataContext);
            Dispatcher.BeginInvoke((Action)(() => AdjustAllGrids()), DispatcherPriority.Background);
            DataContextChanged += (s, args) => AttachToViewModel(args.NewValue);
        }

        private void AttachToViewModel(object vm)
        {
            if (vm is INotifyPropertyChanged inpc)
            {
                inpc.PropertyChanged -= ViewModel_PropertyChanged;
                inpc.PropertyChanged += ViewModel_PropertyChanged;
            }
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e == null || string.IsNullOrEmpty(e.PropertyName)) return;

            if (e.PropertyName.StartsWith("ShowSummary") || e.PropertyName.StartsWith("ShowSession"))
            {
                Dispatcher.BeginInvoke((Action)(() => AdjustAllGrids()), DispatcherPriority.Background);
            }
        }

        private void AdjustAllGrids()
        {
            TryAdjustGrid(AppSummaryGrid, isSummary: true);
            TryAdjustGrid(SessionsGrid, isSummary: false);
        }

        private void TryAdjustGrid(DataGrid grid, bool isSummary)
        {
            if (grid == null) return;

            var visibleCols = grid.Columns.Where(c => c.Visibility == Visibility.Visible).ToList();
            if (!visibleCols.Any())
            {
                // nothing to show; fallback to equal star columns
                foreach (var col in grid.Columns)
                {
                    col.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
                }
                return;
            }

            // First collapse hidden columns to 0 so they don't take space
            foreach (var col in grid.Columns)
            {
                if (col.Visibility != Visibility.Visible)
                {
                    col.Width = new DataGridLength(0, DataGridLengthUnitType.Pixel);
                }
            }

            // Distribute remaining space across visible columns
            foreach (var col in visibleCols)
            {
                var header = (col.Header ?? string.Empty).ToString();
                if (header.Contains("Application", StringComparison.OrdinalIgnoreCase))
                {
                    col.Width = new DataGridLength(2, DataGridLengthUnitType.Star);
                }
                else
                {
                    col.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
                }
            }

            grid.UpdateLayout();
        }

        private void DataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            e.Row.Header = (e.Row.GetIndex() + 1).ToString();
        }
    }
}
