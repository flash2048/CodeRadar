using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CodeRadar.Models;
using Microsoft.Win32;

namespace CodeRadar.Views
{
    public partial class ObjectViewerWindow : Window
    {
        private VariableNode _root;
        private readonly Func<int, CancellationToken, Task<VariableNode>> _reEvaluator;
        private CancellationTokenSource _reEvalCts;

        public ObjectViewerWindow(VariableNode root, string caption,
            Func<int, CancellationToken, Task<VariableNode>> reEvaluator = null)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _reEvaluator = reEvaluator;
            InitializeComponent();
            Title = string.IsNullOrWhiteSpace(caption) ? "Object Viewer" : $"Object Viewer - {caption}";

            if (_reEvaluator == null)
                ReEvalButton.Visibility = Visibility.Collapsed;

            Refresh();
        }

        private ExportFormat CurrentFormat
        {
            get
            {
                if (FormatCombo.SelectedItem is ComboBoxItem item)
                {
                    var text = (item.Content as string) ?? string.Empty;
                    if (text.Equals("JSON", StringComparison.OrdinalIgnoreCase)) return ExportFormat.Json;
                    if (text.Equals("C#",   StringComparison.OrdinalIgnoreCase)) return ExportFormat.CSharp;
                }
                return ExportFormat.Text;
            }
        }

        private int? SelectedDepth
        {
            get
            {
                if (DepthCombo?.SelectedItem is ComboBoxItem d)
                {
                    var text = (d.Content as string) ?? string.Empty;
                    if (int.TryParse(text, out var n)) return n;
                }
                return null; // "Unlimited"
            }
        }

        private ExportOptions BuildOptions()
        {
            var opts = new ExportOptions();
            opts.MaxDepth = SelectedDepth;
            opts.UseTypeFullName     = FullTypeCheck?.IsChecked       == true;
            opts.IgnoreIndexes       = IgnoreIndexesCheck?.IsChecked  == true;
            opts.IgnoreDefaultValues = IgnoreDefaultsCheck?.IsChecked == true;
            opts.PropertiesOnly      = PropertiesOnlyCheck?.IsChecked == true;
            opts.TrimRootName        = TrimRootNameCheck?.IsChecked   == true;
            return opts;
        }

        private void Refresh()
        {
            if (OutputBox == null) return;

            OutputBox.Text = ObjectExporter.Export(_root, CurrentFormat, BuildOptions());
            if (StatusText != null) StatusText.Text = string.Empty;
        }

        private void OptionsChanged(object sender, RoutedEventArgs e) => Refresh();

        private async void ReEvalButton_Click(object sender, RoutedEventArgs e)
        {
            if (_reEvaluator == null) return;

            int depth = SelectedDepth ?? 10;
            ReEvalButton.IsEnabled = false;
            StatusText.Text = $"Evaluating at depth {depth}...";

            _reEvalCts?.Cancel();
            _reEvalCts?.Dispose();
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            _reEvalCts = cts;

            try
            {
                var node = await _reEvaluator(depth, cts.Token);
                if (node != null)
                {
                    _root = node;
                    Refresh();
                    StatusText.Text = "Loaded.";
                }
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "Evaluation timed out.";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Re-evaluate failed: " + ex.Message;
            }
            finally
            {
                ReEvalButton.IsEnabled = true;
            }
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetDataObject(OutputBox.Text ?? string.Empty, copy: true);
                StatusText.Text = "Copied.";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Copy failed: " + ex.Message;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            string ext, filter;
            switch (CurrentFormat)
            {
                case ExportFormat.Json:   ext = "json"; filter = "JSON file (*.json)|*.json|All files (*.*)|*.*"; break;
                case ExportFormat.CSharp: ext = "cs";   filter = "C# file (*.cs)|*.cs|All files (*.*)|*.*";       break;
                default:                  ext = "txt";  filter = "Text file (*.txt)|*.txt|All files (*.*)|*.*";   break;
            }

            var dlg = new SaveFileDialog
            {
                Title = "Save object",
                Filter = filter,
                DefaultExt = ext,
                FileName = SuggestedFileName(ext),
                OverwritePrompt = true,
                AddExtension = true
            };
            if (dlg.ShowDialog(this) == true)
            {
                try
                {
                    File.WriteAllText(dlg.FileName, OutputBox.Text ?? string.Empty);
                    StatusText.Text = "Saved to " + dlg.FileName;
                }
                catch (Exception ex)
                {
                    StatusText.Text = "Save failed: " + ex.Message;
                }
            }
        }

        private string SuggestedFileName(string ext)
        {
            var name = (_root.Name ?? "object").Trim();
            var safe = new System.Text.StringBuilder(name.Length);
            foreach (var c in name)
                safe.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            if (safe.Length == 0) safe.Append("object");
            safe.Append('.').Append(ext);
            return safe.ToString();
        }
    }
}
