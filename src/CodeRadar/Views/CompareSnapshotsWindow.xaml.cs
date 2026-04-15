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
        private static readonly SolidColorBrush ChangedBrush = FrozenBrush(0x33, 0xFF, 0xC8, 0x3D);
        private static readonly SolidColorBrush AddedBrush   = FrozenBrush(0x33, 0x4E, 0xC9, 0xB0);
        private static readonly SolidColorBrush RemovedBrush = FrozenBrush(0x33, 0xF4, 0x87, 0x71);
        private static readonly SolidColorBrush NameBrush    = FrozenBrush(0xFF, 0x9C, 0xDC, 0xFE);
        private static readonly SolidColorBrush TypeBrush    = FrozenBrush(0xFF, 0x7A, 0xC9, 0x7A);

        private static SolidColorBrush FrozenBrush(byte a, byte r, byte g, byte b)
        {
            var br = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            br.Freeze();
            return br;
        }

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

        private void Render()
        {
            if (LeftTree == null || RightTree == null) return;

            var left = LeftCombo.SelectedItem as WatchSnapshot;
            var right = RightCombo.SelectedItem as WatchSnapshot;

            int diffs = 0;
            LeftTree.Items.Clear();
            RightTree.Items.Clear();

            if (left == null && right == null) { DiffSummary.Text = string.Empty; return; }

            BuildPair(LeftTree.Items, RightTree.Items, left?.Root, right?.Root, ref diffs);
            DiffSummary.Text = diffs == 0 ? "Identical" : $"{diffs} difference(s)";
        }

        private static void BuildPair(ItemCollection leftItems, ItemCollection rightItems,
                                      VariableNode left, VariableNode right, ref int diffs)
        {
            var leftByName = new Dictionary<string, VariableNode>(StringComparer.Ordinal);
            var rightByName = new Dictionary<string, VariableNode>(StringComparer.Ordinal);
            var ordered = new List<string>();

            void Add(string name)
            {
                if (!leftByName.ContainsKey(name) && !rightByName.ContainsKey(name)) ordered.Add(name);
            }

            if (left != null) { foreach (var c in left.Children) { Add(c.Name); leftByName[c.Name] = c; } }
            if (right != null) { foreach (var c in right.Children) { Add(c.Name); rightByName[c.Name] = c; } }

            var leftTop = MakeRow(left);
            var rightTop = MakeRow(right);
            ColourPair(leftTop, rightTop, left, right, ref diffs);
            leftItems.Add(leftTop);
            rightItems.Add(rightTop);

            foreach (var name in ordered)
            {
                leftByName.TryGetValue(name, out var ln);
                rightByName.TryGetValue(name, out var rn);

                var l = MakeRow(ln, name);
                var r = MakeRow(rn, name);

                if (ln == null) { diffs++; ColourBackground(r, AddedBrush); }
                else if (rn == null) { diffs++; ColourBackground(l, RemovedBrush); }
                else if (ln.Value != rn.Value) { diffs++; ColourBackground(l, ChangedBrush); ColourBackground(r, ChangedBrush); }

                if ((ln?.Children.Count ?? 0) > 0 || (rn?.Children.Count ?? 0) > 0)
                    BuildPair(l.Items, r.Items, ln, rn, ref diffs);

                leftTop.Items.Add(l);
                rightTop.Items.Add(r);
            }

            leftTop.IsExpanded = true;
            rightTop.IsExpanded = true;
        }

        private static TreeViewItem MakeRow(VariableNode node, string fallbackName = null)
        {
            string name = node?.Name ?? fallbackName ?? "(missing)";
            string val = node == null ? "(missing)" : (node.IsValid ? node.Value : "<invalid>");
            string typ = node?.Type ?? string.Empty;

            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, Foreground = NameBrush });
            sp.Children.Add(new TextBlock { Text = " = ", Foreground = Brushes.Gray });
            sp.Children.Add(new TextBlock { Text = val });
            if (!string.IsNullOrEmpty(typ))
            {
                sp.Children.Add(new TextBlock { Text = " : ", Foreground = Brushes.Gray, Margin = new Thickness(6, 0, 0, 0) });
                sp.Children.Add(new TextBlock { Text = typ, Foreground = TypeBrush });
            }

            return new TreeViewItem { Header = sp };
        }

        private static void ColourBackground(TreeViewItem item, Brush brush) => item.Background = brush;

        private static void ColourPair(TreeViewItem left, TreeViewItem right, VariableNode l, VariableNode r, ref int diffs)
        {
            if (l == null && r == null) return;
            if (l == null) { ColourBackground(right, AddedBrush); diffs++; return; }
            if (r == null) { ColourBackground(left, RemovedBrush); diffs++; return; }
            if (l.Value != r.Value) { ColourBackground(left, ChangedBrush); ColourBackground(right, ChangedBrush); diffs++; }
        }
    }
}
