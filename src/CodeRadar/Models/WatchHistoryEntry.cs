using System;

namespace CodeRadar.Models
{
    public sealed class WatchHistoryEntry
    {
        public WatchHistoryEntry(int breakIndex, DateTime atUtc, string value, bool changed)
        {
            BreakIndex = breakIndex;
            AtUtc = atUtc;
            Value = value ?? string.Empty;
            Changed = changed;
        }

        public int BreakIndex { get; }

        public DateTime AtUtc { get; }

        public string Value { get; }

        public bool Changed { get; }

        public string DisplayTime => AtUtc.ToLocalTime().ToString("HH:mm:ss.fff");
    }
}
