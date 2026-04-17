using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CodeRadar.ViewModels;

namespace CodeRadar.Views
{
    public partial class CodeRadarControl : UserControl
    {
        public CodeRadarControl()
        {
            InitializeComponent();
        }

        // WPF TreeView doesn't auto-select on right-click, so without this handler
        // TreeView.SelectedItem stays null and any context-menu command that expects a
        // WatchItemViewModel parameter is disabled. Promoting the clicked row to the
        // selection makes the CommandParameter bindings resolve.
        private void TreeViewItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TreeViewItem item)
            {
                item.Focus();
                item.IsSelected = true;
            }
        }

        // Same rationale as TreeViewItem_PreviewMouseRightButtonDown.
        private void ListViewItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListViewItem item)
            {
                item.Focus();
                item.IsSelected = true;
            }
        }

        // Fires when the user expands any TreeViewItem in the watch tree. If the
        // associated VM currently shows a lazy placeholder, ask the main view model
        // to evaluate that node's expression one level deeper, on demand.
        private void TreeViewItem_Expanded(object sender, RoutedEventArgs e)
        {
            if (!(sender is TreeViewItem item)) return;
            if (!(item.DataContext is WatchItemViewModel vm)) return;
            if (!(DataContext is CodeRadarViewModel main)) return;

            if (!vm.NeedsLazyLoad) return;
            main.EnsureChildrenLoaded(vm);
            e.Handled = false;
        }
    }
}
