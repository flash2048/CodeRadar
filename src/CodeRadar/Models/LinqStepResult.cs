using System.Collections.Generic;

namespace CodeRadar.Models
{
    public sealed class LinqStepResult
    {
        public LinqStepResult(string label, string cumulativeExpression, int? count,
            bool truncated, IReadOnlyList<VariableNode> sampleItems, string error)
        {
            Label = label ?? string.Empty;
            CumulativeExpression = cumulativeExpression ?? string.Empty;
            Count = count;
            Truncated = truncated;
            SampleItems = sampleItems ?? System.Array.Empty<VariableNode>();
            Error = error ?? string.Empty;
        }

        public string Label { get; }

        public string CumulativeExpression { get; }

        public int? Count { get; }

        public bool Truncated { get; }

        public IReadOnlyList<VariableNode> SampleItems { get; }

        public string Error { get; }

        public bool HasError => !string.IsNullOrEmpty(Error);
    }
}
