namespace CodeRadar.Models
{
    public sealed class ThreadInfo
    {
        public ThreadInfo(int id, string name, string location, bool isCurrent, bool isFrozen)
        {
            Id = id;
            Name = name ?? string.Empty;
            Location = location ?? string.Empty;
            IsCurrent = isCurrent;
            IsFrozen = isFrozen;
        }

        public int Id { get; }

        public string Name { get; }

        public string Location { get; }

        public bool IsCurrent { get; }

        public bool IsFrozen { get; }

        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"Thread {Id}" : $"{Name} ({Id})";

        public override string ToString() => DisplayName;
    }
}
