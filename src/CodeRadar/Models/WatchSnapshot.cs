using System;

namespace CodeRadar.Models
{
    public sealed class WatchSnapshot
    {
        public WatchSnapshot(string label, DateTime takenAtUtc, VariableNode root, int breakIndex)
        {
            Label = string.IsNullOrWhiteSpace(label) ? "Snapshot" : label;
            TakenAtUtc = takenAtUtc;
            Root = root ?? throw new ArgumentNullException(nameof(root));
            BreakIndex = breakIndex;
        }

        public string Label { get; }

        public DateTime TakenAtUtc { get; }

        public int BreakIndex { get; }

        public VariableNode Root { get; }

        public string DisplayName =>
            $"{Label} - {TakenAtUtc.ToLocalTime():HH:mm:ss} (break #{BreakIndex})";
    }
}
