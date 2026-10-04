using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using Cutlify.Core;
using Cutlify.Storage;

namespace Cutlify.UI
{
    public partial class HistoryWindow : Window
    {
        private readonly AppController _app;
        private readonly ICollectionView _view;

        public HistoryWindow(AppController app)
        {
            _app = app;
            InitializeComponent();
            _view = new ListCollectionView(app.History.Items);
            _view.Filter = Filter;
            List.ItemsSource = _view;

            SourceInitialized += (_, __) => Native.RoundCorners(new WindowInteropHelper(this).Handle);
            CloseButton.Click += (_, __) => Close();
            SearchBox.TextChanged += (_, __) => { SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; Refresh(); };
            FilterAll.Checked += (_, __) => Refresh();
            FilterImages.Checked += (_, __) => Refresh();
            FilterVideos.Checked += (_, __) => Refresh();
            FilterAvailable.Checked += (_, __) => Refresh();
            FilterDeleted.Checked += (_, __) => Refresh();
            FilterExpired.Checked += (_, __) => Refresh();
            ClearButton.Click += (_, __) => _app.ClearHistory(this);
            List.MouseDoubleClick += (_, __) => { if (List.SelectedItem is HistoryItem i) _app.ShowMainWindow(i); };
            // Los estados cambian (eliminada/expirada) sin cambiar la colección: refrescar el filtro.
            foreach (var it in app.History.Items) it.PropertyChanged += OnItemChanged;
            app.History.Items.CollectionChanged += (_, e) =>
            {
                if (e.NewItems != null) foreach (HistoryItem it in e.NewItems) it.PropertyChanged += OnItemChanged;
            };
            KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
            app.History.Items.CollectionChanged += (_, __) => UpdateEmpty();
            UpdateEmpty();
        }

        private void OnItemChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(HistoryItem.State)) Dispatcher.BeginInvoke(new Action(Refresh));
        }

        private void Refresh()
        {
            _view?.Refresh();
            UpdateEmpty();
        }

        private void UpdateEmpty() => EmptyText.Visibility = _view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;

        private bool Filter(object o)
        {
            if (!(o is HistoryItem i)) return false;
            if (FilterImages?.IsChecked == true && i.IsVideo) return false;
            if (FilterVideos?.IsChecked == true && !i.IsVideo) return false;
            if (FilterAvailable?.IsChecked == true && !i.IsAvailable) return false;
            if (FilterDeleted?.IsChecked == true && i.State != ItemState.Deleted) return false;
            if (FilterExpired?.IsChecked == true && i.State != ItemState.Expired) return false;
            var q = SearchBox?.Text?.Trim();
            if (string.IsNullOrEmpty(q)) return true;
            return Matches(i, q);
        }

        internal static bool Matches(HistoryItem i, string q)
        {
            var cmp = StringComparison.OrdinalIgnoreCase;
            return i.Name.Contains(q, cmp)
                || i.DateLabel.Contains(q, cmp)
                || i.CreatedLocal.ToString("d MMMM yyyy", new System.Globalization.CultureInfo("es-ES")).Contains(q, cmp)
                || i.TypeLabel.Contains(q, cmp)
                || (i.IsVideo ? "video vídeo mp4" : "imagen png jpg").Contains(q, cmp)
                || i.StatusLabel.Contains(q, cmp)
                || i.CreatedLocal.ToString("yyyy-MM-dd").Contains(q, cmp);
        }

        private HistoryItem ItemOf(object sender) => (sender as FrameworkElement)?.DataContext as HistoryItem;

        private void Copy_Click(object sender, RoutedEventArgs e) { var i = ItemOf(sender); if (i != null) _app.Copy(i); }
        private void Save_Click(object sender, RoutedEventArgs e) { var i = ItemOf(sender); if (i != null) _app.Save(i); }
        private void Open_Click(object sender, RoutedEventArgs e) { var i = ItemOf(sender); if (i != null) _app.Open(i); }
        private void Location_Click(object sender, RoutedEventArgs e) { var i = ItemOf(sender); if (i != null) _app.OpenLocation(i); }
        private void Delete_Click(object sender, RoutedEventArgs e) { var i = ItemOf(sender); if (i != null) _app.DeleteFile(i, this); }
        private void Details_Click(object sender, RoutedEventArgs e) { var i = ItemOf(sender); if (i != null) _app.ShowDetails(i, this); }
        private void Remove_Click(object sender, RoutedEventArgs e) { var i = ItemOf(sender); if (i != null) _app.RemoveFromHistory(i); }
    }
}
