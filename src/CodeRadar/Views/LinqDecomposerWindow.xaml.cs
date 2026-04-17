using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CodeRadar.Models;
using CodeRadar.Services;

namespace CodeRadar.Views
{
    public partial class LinqDecomposerWindow : Window
    {
        private readonly Func<string, int, CancellationToken, Task<VariableNode>> _evaluator;
        private readonly Func<string, CancellationToken, Task<ImageExtractResult>> _imageExtractor;

        public LinqDecomposerWindow(string originalExpression, IReadOnlyList<LinqStepResult> steps,
            Func<string, int, CancellationToken, Task<VariableNode>> evaluator = null,
            Func<string, CancellationToken, Task<ImageExtractResult>> imageExtractor = null)
        {
            _evaluator = evaluator;
            _imageExtractor = imageExtractor;
            InitializeComponent();
            OriginalExprBox.Text = originalExpression ?? string.Empty;

            var rows = new List<StepRow>(steps?.Count ?? 0);
            if (steps != null)
            {
                int n = 0;
                int? prevCount = null;
                foreach (var s in steps)
                {
                    rows.Add(new StepRow(++n, s, prevCount));
                    if (s.TotalCount.HasValue) prevCount = s.TotalCount;
                }
            }
            StepList.ItemsSource = rows;
        }

        private VariableNode GetSelectedNode(object sender)
        {
            // Walk up from the MenuItem to the ContextMenu, then to the TreeView.
            if (sender is MenuItem mi && mi.Parent is ContextMenu cm && cm.PlacementTarget is TreeView tv)
                return tv.SelectedItem as VariableNode;
            return null;
        }

        private string BuildExpressionForNode(VariableNode node, object sender)
        {
            // Find which step row this tree belongs to and compose the expression.
            if (sender is MenuItem mi && mi.Parent is ContextMenu cm && cm.PlacementTarget is TreeView tv)
            {
                // Walk up to find the StepRow data context.
                var element = tv as FrameworkElement;
                while (element != null)
                {
                    if (element.DataContext is StepRow row)
                    {
                        string baseExpr = row.Result.CumulativeExpression;
                        if (node == null) return baseExpr;

                        // If the node is a direct child (array element), compose path.
                        string name = node.Name ?? string.Empty;
                        if (name.Length >= 2 && name[0] == '[' && name[name.Length - 1] == ']')
                            return baseExpr + name;
                        if (name.Length > 0 && (char.IsLetter(name[0]) || name[0] == '_'))
                            return baseExpr + "." + name;

                        return baseExpr;
                    }
                    element = element.Parent as FrameworkElement
                           ?? System.Windows.Media.VisualTreeHelper.GetParent(element) as FrameworkElement;
                }
            }
            return null;
        }

        private async void ViewObject_Click(object sender, RoutedEventArgs e)
        {
            var node = GetSelectedNode(sender);
            if (node == null) return;

            string expr = BuildExpressionForNode(node, sender);
            if (_evaluator != null && !string.IsNullOrEmpty(expr))
            {
                try
                {
                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
                    {
                        var deepNode = await _evaluator(expr, 2, cts.Token);
                        if (deepNode != null) node = deepNode;
                    }
                }
                catch { }
            }

            Func<int, CancellationToken, Task<VariableNode>> reEval = null;
            if (_evaluator != null && !string.IsNullOrEmpty(expr))
            {
                var capturedExpr = expr;
                reEval = async (depth, ct) => await _evaluator(capturedExpr, depth, ct);
            }

            DialogPresenter.Show(() => new ObjectViewerWindow(node, expr ?? node.Name, reEval));
        }

        private async void ShowImage_Click(object sender, RoutedEventArgs e)
        {
            var node = GetSelectedNode(sender);
            if (node == null || _imageExtractor == null) return;

            string expr = BuildExpressionForNode(node, sender);
            if (string.IsNullOrEmpty(expr)) return;

            ImageExtractResult result;
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
                {
                    result = await _imageExtractor(expr, cts.Token);
                }
            }
            catch (Exception ex)
            {
                result = new ImageExtractResult { Success = false, Error = ex.Message };
            }

            if (result == null || !result.Success)
            {
                MessageBox.Show(
                    "No image data could be decoded from this expression.\n\n"
                    + (result?.Error ?? string.Empty),
                    "Code Radar - Show image",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            DialogPresenter.Show(() => new ImageViewerWindow(result.ImageBytes, result.DetectedFormat, expr));
        }

        private void CopyValue_Click(object sender, RoutedEventArgs e)
        {
            var node = GetSelectedNode(sender);
            if (node == null) return;
            TrySetClipboard(node.Value ?? string.Empty);
        }

        private void CopyAsJson_Click(object sender, RoutedEventArgs e)
        {
            var node = GetSelectedNode(sender);
            if (node == null) return;
            TrySetClipboard(ObjectExporter.Export(node, ExportFormat.Json));
        }

        private static void TrySetClipboard(string text)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    Clipboard.SetDataObject(text ?? string.Empty, copy: true);
                    return;
                }
                catch
                {
                    System.Threading.Thread.Sleep(20);
                }
            }
        }

        public sealed class StepRow
        {
            public StepRow(int stepNumber, LinqStepResult result, int? previousCount)
            {
                StepNumber = stepNumber;
                Result = result;
                PreviousCount = previousCount;
            }

            public int StepNumber { get; }
            public LinqStepResult Result { get; }
            public int? PreviousCount { get; }

            // Real sequence count with delta arrow - uses TotalCount (NOT sample size).
            public string CountText
            {
                get
                {
                    if (Result.HasError) return string.Empty;
                    if (!Result.TotalCount.HasValue) return string.Empty;

                    string suffix = Result.CountTruncated ? "+" : string.Empty;
                    if (PreviousCount.HasValue && PreviousCount.Value != Result.TotalCount.Value)
                    {
                        char arrow = Result.TotalCount.Value >= PreviousCount.Value ? '\u25B2' : '\u25BC';
                        return $"{PreviousCount.Value} -> {Result.TotalCount.Value}{suffix} {arrow}";
                    }
                    return $"{Result.TotalCount.Value}{suffix} item(s)";
                }
            }

            // Clearly labels that the samples below are a preview, not the full sequence.
            public string SampleLabelText
            {
                get
                {
                    if (Result.SampleSize == 0) return string.Empty;
                    if (Result.SampleTruncated && Result.TotalCount.HasValue)
                        return $"Sample of first {Result.SampleSize} of {Result.TotalCount.Value}{(Result.CountTruncated ? "+" : string.Empty)} items:";
                    if (Result.SampleTruncated)
                        return $"Sample of first {Result.SampleSize} items (more available):";
                    return $"All {Result.SampleSize} item(s):";
                }
            }

            public Visibility HasErrorVisibility => Result.HasError ? Visibility.Visible : Visibility.Collapsed;
            public Visibility HasSamplesVisibility =>
                (Result.SampleItems != null && Result.SampleItems.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
            public Visibility TruncatedVisibility =>
                Result.SampleTruncated ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
