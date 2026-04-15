using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CodeRadar.Models;
using Microsoft.Win32;

namespace CodeRadar.Views
{
    public partial class ObjectViewerWindow : Window
    {
        private readonly VariableNode _root;

        public ObjectViewerWindow(VariableNode root, string caption)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            InitializeComponent();
            Title = string.IsNullOrWhiteSpace(caption) ? "Object Viewer" : $"Object Viewer - {caption}";
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

        private ExportOptions BuildOptions()
        {
            var opts = new ExportOptions();

            if (DepthCombo?.SelectedItem is ComboBoxItem d)
            {
                var text = (d.Content as string) ?? string.Empty;
                if (int.TryParse(text, out var n)) opts.MaxDepth = n;
                else opts.MaxDepth = null;
            }

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
