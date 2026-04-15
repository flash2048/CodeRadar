using System.Windows.Controls;
using System.Windows.Input;

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
    }
}
