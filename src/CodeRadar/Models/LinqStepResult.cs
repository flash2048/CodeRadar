using System.Collections.Generic;

namespace CodeRadar.Models
{
    public sealed class LinqStepResult
    {
        public LinqStepResult(string label, string cumulativeExpression,
            int? totalCount, bool countTruncated,
            int sampleSize, bool sampleTruncated,
            IReadOnlyList<VariableNode> sampleItems, string error)
        {
            Label = label ?? string.Empty;
            CumulativeExpression = cumulativeExpression ?? string.Empty;
            TotalCount = totalCount;
            CountTruncated = countTruncated;
            SampleSize = sampleSize;
            SampleTruncated = sampleTruncated;
            SampleItems = sampleItems ?? System.Array.Empty<VariableNode>();
            Error = error ?? string.Empty;
        }

        public string Label { get; }

        public string CumulativeExpression { get; }

        // True total element count of the sequence (capped at a safety max).
        // Null if not enumerable or could not be computed.
        public int? TotalCount { get; }

        // If true, the real sequence had at least TotalCount elements and could have more.
        public bool CountTruncated { get; }

        // Number of sample items captured (up to the materialisation cap).
        public int SampleSize { get; }

        // If true, the sample list was cut off before exhausting the sequence.
        public bool SampleTruncated { get; }

        public IReadOnlyList<VariableNode> SampleItems { get; }

        public string Error { get; }

        public bool HasError => !string.IsNullOrEmpty(Error);
    }
}
