using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CodeRadar.Models;

namespace CodeRadar.ViewModels
{
    public sealed class WatchItemViewModel : ObservableObject
    {
        private string _value;
        private string _type;
        private bool _isValid;
        private bool _isNull;
        private bool _hasChanged;
        private bool _isExpanded;
        private string _previousValue;
        private bool _isSequencePreview;
        private bool _isSearchMatch;
        private bool _hasMatchingDescendant;

        public WatchItemViewModel(string name)
        {
            Name = name;
            Children = new ObservableCollection<WatchItemViewModel>();
            History = new ObservableCollection<WatchHistoryEntry>();
            Snapshots = new ObservableCollection<WatchSnapshot>();
        }

        public string Name { get; }

        public string ExpressionPath { get; set; }

        public static readonly HashSet<string> SyntheticNames =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "Raw View",
                "Static members",
                "Non-Public members",
                "Results View"
            };

        public bool IsAddressable =>
            !string.IsNullOrEmpty(ExpressionPath) && !SyntheticNames.Contains(Name ?? string.Empty);

        public string Value
        {
            get => _value;
            set => SetProperty(ref _value, value);
        }

        public string Type
        {
            get => _type;
            set => SetProperty(ref _type, value);
        }

        public bool IsValid
        {
            get => _isValid;
            set => SetProperty(ref _isValid, value);
        }

        public bool IsNull
        {
            get => _isNull;
            set => SetProperty(ref _isNull, value);
        }

        public bool HasChanged
        {
            get => _hasChanged;
            set => SetProperty(ref _hasChanged, value);
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        public string PreviousValue
        {
            get => _previousValue;
            private set => SetProperty(ref _previousValue, value);
        }

        public bool IsSequencePreview
        {
            get => _isSequencePreview;
            set => SetProperty(ref _isSequencePreview, value);
        }

        public bool IsSearchMatch
        {
            get => _isSearchMatch;
            set => SetProperty(ref _isSearchMatch, value);
        }

        public bool HasMatchingDescendant
        {
            get => _hasMatchingDescendant;
            set => SetProperty(ref _hasMatchingDescendant, value);
        }

        public ObservableCollection<WatchItemViewModel> Children { get; }

        public ObservableCollection<WatchHistoryEntry> History { get; }

        public const int HistoryCapacity = 100;

        public ObservableCollection<WatchSnapshot> Snapshots { get; }

        public bool HasHistory => History.Count > 0;
        public bool HasSnapshots => Snapshots.Count > 0;

        public void UpdateFrom(VariableNode node)
        {
            var previous = Value;
            var changed = previous != null && previous != node.Value;

            if (changed)
                PreviousValue = previous;

            Value = node.Value;
            Type = node.Type;
            IsValid = node.IsValid;
            IsNull = node.IsNull;
            HasChanged = changed;

            ReconcileChildren(node);
        }

        public void RecordHistory(int breakIndex, DateTime atUtc)
        {
            History.Add(new WatchHistoryEntry(breakIndex, atUtc, Value ?? string.Empty, HasChanged));
            while (History.Count > HistoryCapacity)
                History.RemoveAt(0);
            OnPropertyChanged(nameof(HasHistory));
        }

        public void AddSnapshot(WatchSnapshot snapshot)
        {
            if (snapshot == null) return;
            Snapshots.Add(snapshot);
            OnPropertyChanged(nameof(HasSnapshots));
        }

        public void AckChange() => HasChanged = false;

        public bool ApplySearch(string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                IsSearchMatch = false;
                HasMatchingDescendant = false;
                foreach (var c in Children) c.ApplySearch(null);
                return false;
            }

            bool selfMatches =
                (Name?.IndexOf(query, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                || (Value?.IndexOf(query, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;

            bool descendantMatches = false;
            foreach (var child in Children)
            {
                if (child.ApplySearch(query))
                    descendantMatches = true;
            }

            IsSearchMatch = selfMatches;
            HasMatchingDescendant = descendantMatches;

            if (descendantMatches)
                IsExpanded = true;

            return selfMatches || descendantMatches;
        }

        private void ReconcileChildren(VariableNode node)
        {
            int nodeChildren = node.Children.Count;
            int existingChildren = Children.Count;

            if (nodeChildren == 0)
            {
                if (existingChildren > 0) Children.Clear();
                return;
            }

            if (existingChildren == nodeChildren)
            {
                bool sameOrder = true;
                for (int i = 0; i < existingChildren; i++)
                {
                    if (Children[i].Name != node.Children[i].Name) { sameOrder = false; break; }
                }
                if (sameOrder)
                {
                    for (int i = 0; i < existingChildren; i++)
                    {
                        var vm = Children[i];
                        vm.ExpressionPath = ComposePath(ExpressionPath, vm.Name);
                        vm.UpdateFrom(node.Children[i]);
                    }
                    return;
                }
            }

            Dictionary<string, WatchItemViewModel> existing = null;
            if (existingChildren > 0)
            {
                existing = new Dictionary<string, WatchItemViewModel>(existingChildren, StringComparer.Ordinal);
                for (int i = 0; i < existingChildren; i++)
                {
                    var vm = Children[i];
                    if (!string.IsNullOrEmpty(vm.Name) && !existing.ContainsKey(vm.Name))
                        existing[vm.Name] = vm;
                }
            }

            Children.Clear();
            for (int i = 0; i < nodeChildren; i++)
            {
                var child = node.Children[i];
                WatchItemViewModel vm;
                if (existing != null && existing.TryGetValue(child.Name, out var reused))
                    vm = reused;
                else
                    vm = new WatchItemViewModel(child.Name);
                vm.ExpressionPath = ComposePath(ExpressionPath, child.Name);
                vm.UpdateFrom(child);
                Children.Add(vm);
            }
        }

        private static string ComposePath(string parentPath, string childName)
        {
            if (string.IsNullOrEmpty(parentPath)) return string.Empty;
            if (string.IsNullOrEmpty(childName)) return string.Empty;
            if (SyntheticNames.Contains(childName)) return string.Empty;

            if (childName.Length >= 2 && childName[0] == '[' && childName[childName.Length - 1] == ']')
                return parentPath + childName;

            if (!char.IsLetter(childName[0]) && childName[0] != '_')
                return string.Empty;

            return parentPath + "." + childName;
        }
    }
}
