using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodeRadar.Models;
using CodeRadar.ViewModels;

namespace CodeRadar.Views
{
    public partial class CompareSnapshotsWindow : Window
    {
        // Row palette
        private static readonly SolidColorBrush ChangedSoftBrush = FrozenBrush(0x33, 0xFF, 0xC8, 0x3D);
        private static readonly SolidColorBrush AddedSoftBrush   = FrozenBrush(0x33, 0x4E, 0xC9, 0xB0);
        private static readonly SolidColorBrush RemovedSoftBrush = FrozenBrush(0x33, 0xF4, 0x87, 0x71);
        private static readonly SolidColorBrush ChangedStrongBrush = FrozenBrush(0xFF, 0xFF, 0xC8, 0x3D);
        private static readonly SolidColorBrush AddedStrongBrush   = FrozenBrush(0xFF, 0x4E, 0xC9, 0xB0);
        private static readonly SolidColorBrush RemovedStrongBrush = FrozenBrush(0xFF, 0xF4, 0x87, 0x71);

        // Text colors
        private static readonly SolidColorBrush NameBrush      = FrozenBrush(0xFF, 0x9C, 0xDC, 0xFE);
        private static readonly SolidColorBrush NameDimBrush   = FrozenBrush(0xFF, 0x62, 0x8B, 0xB4);
        private static readonly SolidColorBrush TypeBrush      = FrozenBrush(0xFF, 0x7A, 0xC9, 0x7A);
        private static readonly SolidColorBrush TypeDimBrush   = FrozenBrush(0xFF, 0x4B, 0x7D, 0x4B);
        private static readonly SolidColorBrush ValueBrush     = FrozenBrush(0xFF, 0xF1, 0xF1, 0xF1);
        private static readonly SolidColorBrush ValueDimBrush  = FrozenBrush(0xFF, 0x95, 0x95, 0x95);
        private static readonly SolidColorBrush PunctDimBrush  = FrozenBrush(0xFF, 0x5A, 0x5A, 0x5A);
        private static readonly SolidColorBrush MutedBrush     = FrozenBrush(0xFF, 0x7A, 0x7A, 0x7A);

        private static SolidColorBrush FrozenBrush(byte a, byte r, byte g, byte b)
        {
            var br = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            br.Freeze();
            return br;
        }

        private enum ChangeKind { Unchanged, Changed, Added, Removed, Missing }

        private readonly WatchItemViewModel _watch;

        public CompareSnapshotsWindow(WatchItemViewModel watch)
        {
            _watch = watch ?? throw new ArgumentNullException(nameof(watch));
            InitializeComponent();
            Title = $"Compare snapshots - {watch.Name}";

            LeftCombo.ItemsSource = _watch.Snapshots;
            RightCombo.ItemsSource = _watch.Snapshots;

            if (_watch.Snapshots.Count >= 2)
            {
                LeftCombo.SelectedIndex = 0;
                RightCombo.SelectedIndex = _watch.Snapshots.Count - 1;
            }
            else if (_watch.Snapshots.Count == 1)
            {
                LeftCombo.SelectedIndex = 0;
                RightCombo.SelectedIndex = 0;
            }
            Render();
        }

        private void ComboChanged(object sender, SelectionChangedEventArgs e) => Render();
        private void ShowOnlyChangesCheck_Changed(object sender, RoutedEventArgs e) => Render();

        private void Render()
        {
            if (LeftTree == null || RightTree == null) return;

            var left = LeftCombo.SelectedItem as WatchSnapshot;
            var right = RightCombo.SelectedItem as WatchSnapshot;

            int changed = 0, added = 0, removed = 0;
            LeftTree.Items.Clear();
            RightTree.Items.Clear();

            bool onlyChanges = ShowOnlyChangesCheck.IsChecked == true;

            if (left == null && right == null)
            {
                UpdateSummary(0, 0, 0);
                return;
            }

            BuildPair(LeftTree.Items, RightTree.Items, left?.Root, right?.Root,
                      ref changed, ref added, ref removed, onlyChanges,
                      isRoot: true);

            UpdateSummary(changed, added, removed);
        }

        private void UpdateSummary(int changed, int added, int removed)
        {
            ChangedCountText.Text = changed + " changed";
            AddedCountText.Text   = added + " added";
            RemovedCountText.Text = removed + " removed";
            IdenticalText.Text = (changed + added + removed) == 0
                ? "Snapshots are identical."
                : string.Empty;
        }

        // Returns true if this sub-tree contained at least one difference (drives
        // the "Show only changes" filter so ancestors of a change stay visible).
        private static bool BuildPair(
            ItemCollection leftItems, ItemCollection rightItems,
            VariableNode left, VariableNode right,
            ref int changed, ref int added, ref int removed,
            bool onlyChanges, bool isRoot)
        {
            var leftByName  = new Dictionary<string, VariableNode>(StringComparer.Ordinal);
            var rightByName = new Dictionary<string, VariableNode>(StringComparer.Ordinal);
            var ordered     = new List<string>();

            void AddOrdered(string name)
            {
                if (!leftByName.ContainsKey(name) && !rightByName.ContainsKey(name))
                    ordered.Add(name);
            }

            if (left != null)  { foreach (var c in left.Children)  { AddOrdered(c.Name); leftByName[c.Name]  = c; } }
            if (right != null) { foreach (var c in right.Children) { AddOrdered(c.Name); rightByName[c.Name] = c; } }

            // Top-level pair. For root we use a single-level comparison of the
            // scalar value; subtree differences are counted separately via recursion.
            var (topLeftKind, topRightKind) = ClassifyPair(left, right, isRoot: isRoot, hasChildren: ordered.Count > 0);

            var leftTop  = MakeRow(left,  topLeftKind);
            var rightTop = MakeRow(right, topRightKind);
            if (topLeftKind == ChangeKind.Changed || topRightKind == ChangeKind.Changed) changed++;
            else if (topLeftKind == ChangeKind.Removed) removed++;
            else if (topRightKind == ChangeKind.Added) added++;

            leftItems.Add(leftTop);
            rightItems.Add(rightTop);

            bool anyChildChanged = false;
            foreach (var name in ordered)
            {
                leftByName.TryGetValue(name, out var ln);
                rightByName.TryGetValue(name, out var rn);

                var (lKind, rKind) = ClassifyPair(ln, rn, isRoot: false, hasChildren:
                    (ln?.Children.Count ?? 0) > 0 || (rn?.Children.Count ?? 0) > 0);

                bool isDiff = lKind != ChangeKind.Unchanged || rKind != ChangeKind.Unchanged;

                var l = MakeRow(ln, lKind, fallbackName: name);
                var r = MakeRow(rn, rKind, fallbackName: name);

                if (lKind == ChangeKind.Changed || rKind == ChangeKind.Changed) changed++;
                else if (lKind == ChangeKind.Removed) removed++;
                else if (rKind == ChangeKind.Added) added++;

                bool childrenHadDiffs = false;
                if ((ln?.Children.Count ?? 0) > 0 || (rn?.Children.Count ?? 0) > 0)
                {
                    childrenHadDiffs = BuildPair(l.Items, r.Items, ln, rn,
                        ref changed, ref added, ref removed, onlyChanges, isRoot: false);
                }

                // When "Show only changes" is on, skip rows that are unchanged
                // AND whose subtree is unchanged.
                if (onlyChanges && !isDiff && !childrenHadDiffs)
                {
                    continue;
                }

                if (isDiff || childrenHadDiffs) anyChildChanged = true;

                // Auto-expand ancestors of changes so the user sees them without clicking.
                if (childrenHadDiffs)
                {
                    l.IsExpanded = true;
                    r.IsExpanded = true;
                }

                leftTop.Items.Add(l);
                rightTop.Items.Add(r);
            }

            leftTop.IsExpanded  = true;
            rightTop.IsExpanded = true;

            return anyChildChanged
                || topLeftKind == ChangeKind.Changed || topRightKind == ChangeKind.Changed
                || topLeftKind == ChangeKind.Removed || topRightKind == ChangeKind.Added;
        }

        private static (ChangeKind left, ChangeKind right) ClassifyPair(
            VariableNode l, VariableNode r, bool isRoot, bool hasChildren)
        {
            if (l == null && r == null) return (ChangeKind.Missing, ChangeKind.Missing);
            if (l == null) return (ChangeKind.Missing, ChangeKind.Added);
            if (r == null) return (ChangeKind.Removed, ChangeKind.Missing);

            // For object-valued rows with children we don't flag the parent as
            // "Changed" just because its debugger summary differs - the children
            // convey the real diff. For leaves, compare values.
            bool flaggedChanged = !hasChildren && !Equals(l.Value, r.Value);
            if (flaggedChanged) return (ChangeKind.Changed, ChangeKind.Changed);
            return (ChangeKind.Unchanged, ChangeKind.Unchanged);
        }

        // Builds a row with a narrow indicator gutter and a value-only highlight
        // pill. Unchanged rows are dimmed so changes visually pop.
        private static TreeViewItem MakeRow(VariableNode node, ChangeKind kind, string fallbackName = null)
        {
            string name = node?.Name ?? fallbackName ?? "(missing)";
            string val  = node == null ? "(missing)" : (node.IsValid ? node.Value : "<invalid>");
            string typ  = node?.Type ?? string.Empty;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Left gutter: change indicator (single glyph). Positioned tightly so
            // the eye can scan vertically.
            var indicator = new TextBlock
            {
                FontFamily = new FontFamily("Consolas"),
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 4, 0)
            };
            switch (kind)
            {
                case ChangeKind.Changed:
                    indicator.Text = "~";
                    indicator.Foreground = ChangedStrongBrush;
                    break;
                case ChangeKind.Added:
                    indicator.Text = "+";
                    indicator.Foreground = AddedStrongBrush;
                    break;
                case ChangeKind.Removed:
                    indicator.Text = "\u2212"; // minus sign
                    indicator.Foreground = RemovedStrongBrush;
                    break;
                case ChangeKind.Missing:
                    indicator.Text = " ";
                    break;
                default:
                    indicator.Text = "\u00B7"; // middle dot for unchanged
                    indicator.Foreground = PunctDimBrush;
                    break;
            }
            Grid.SetColumn(indicator, 0);
            grid.Children.Add(indicator);

            // Content row: name = [value-pill] : type
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            bool dim = kind == ChangeKind.Unchanged;

            var nameBrush  = dim ? NameDimBrush  : NameBrush;
            var typeBrush  = dim ? TypeDimBrush  : TypeBrush;
            var valueBrush = dim ? ValueDimBrush : ValueBrush;
            var punctBrush = dim ? PunctDimBrush : MutedBrush;

            if (kind == ChangeKind.Missing)
            {
                // Soft greyed-out "(missing)" placeholder so corresponding rows
                // still line up with the other side.
                content.Children.Add(new TextBlock
                {
                    Text = "(missing)",
                    Foreground = PunctDimBrush,
                    FontStyle = FontStyles.Italic
                });
            }
            else
            {
                content.Children.Add(new TextBlock
                {
                    Text = name,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = nameBrush
                });
                content.Children.Add(new TextBlock { Text = " = ", Foreground = punctBrush });

                // Value text wrapped in a Border so we can give it a pill
                // background ONLY on the value - not the full row width.
                var valueBorder = new Border
                {
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(4, 0, 4, 0)
                };
                switch (kind)
                {
                    case ChangeKind.Changed:
                        valueBorder.Background = ChangedSoftBrush;
                        break;
                    case ChangeKind.Added:
                        valueBorder.Background = AddedSoftBrush;
                        break;
                    case ChangeKind.Removed:
                        valueBorder.Background = RemovedSoftBrush;
                        break;
                }
                valueBorder.Child = new TextBlock
                {
                    Text = val,
                    Foreground = valueBrush,
                    FontWeight = (kind == ChangeKind.Changed || kind == ChangeKind.Added || kind == ChangeKind.Removed)
                        ? FontWeights.SemiBold : FontWeights.Normal
                };
                content.Children.Add(valueBorder);

                if (!string.IsNullOrEmpty(typ))
                {
                    content.Children.Add(new TextBlock
                    {
                        Text = " : ",
                        Foreground = punctBrush,
                        Margin = new Thickness(4, 0, 0, 0)
                    });
                    content.Children.Add(new TextBlock { Text = typ, Foreground = typeBrush });
                }
            }
            Grid.SetColumn(content, 1);
            grid.Children.Add(content);

            return new TreeViewItem { Header = grid };
        }
    }
}
