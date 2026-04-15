using System.Linq;
using System.Windows;
using CodeRadar.ViewModels;

namespace CodeRadar.Views
{
    public partial class WatchHistoryWindow : Window
    {
        public WatchHistoryWindow(WatchItemViewModel watch)
        {
            InitializeComponent();
            HeaderText.Text = $"History for {watch.Name}";
            int changes = watch.History.Count(h => h.Changed);
            SubText.Text = $"{watch.History.Count} entries - {changes} value change(s)";
            HistoryList.ItemsSource = watch.History;
            if (HistoryList.Items.Count > 0)
                HistoryList.ScrollIntoView(HistoryList.Items[HistoryList.Items.Count - 1]);
        }
    }
}
