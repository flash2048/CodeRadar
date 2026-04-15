using System.Collections.Generic;
using System.Windows;
using CodeRadar.Models;

namespace CodeRadar.Views
{
    public partial class LinqDecomposerWindow : Window
    {
        public LinqDecomposerWindow(string originalExpression, IReadOnlyList<LinqStepResult> steps)
        {
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
                    if (s.Count.HasValue) prevCount = s.Count;
                }
            }
            StepList.ItemsSource = rows;
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

            public string CountText
            {
                get
                {
                    if (Result.HasError) return string.Empty;
                    if (!Result.Count.HasValue) return string.Empty;

                    string suffix = Result.Truncated ? "+" : string.Empty;
                    if (PreviousCount.HasValue && PreviousCount.Value != Result.Count.Value)
                    {
                        char arrow = Result.Count.Value >= PreviousCount.Value ? '\u25B2' : '\u25BC';
                        return $"{PreviousCount.Value} -> {Result.Count.Value}{suffix} {arrow}";
                    }
                    return $"{Result.Count.Value}{suffix} item(s)";
                }
            }

            public Visibility HasErrorVisibility => Result.HasError ? Visibility.Visible : Visibility.Collapsed;
            public Visibility HasSamplesVisibility =>
                (Result.SampleItems != null && Result.SampleItems.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
            public Visibility TruncatedVisibility =>
                Result.Truncated ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
