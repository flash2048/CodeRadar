using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CodeRadar.Models;
using CodeRadar.Services;

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
        private bool _isLazyPlaceholder;
        private bool _isLoadingChildren;

        public WatchItemViewModel(string name)
        {
            Name = name;
            Children = new ObservableCollection<WatchItemViewModel>();
            History = new ObservableCollection<WatchHistoryEntry>();
            Snapshots = new ObservableCollection<WatchSnapshot>();
        }

        // --- Identity ---

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

        // --- Scalar display state ---

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

        // --- Lazy loading ---

        // Sentinel row so the WPF TreeView shows an expand chevron even when the
        // real children haven't been materialised yet.
        public bool IsLazyPlaceholder
        {
            get => _isLazyPlaceholder;
            set => SetProperty(ref _isLazyPlaceholder, value);
        }

        // Guard against re-entrant loads when the user expands a node repeatedly
        // or when a refresh races with a lazy-expand.
        public bool IsLoadingChildren
        {
            get => _isLoadingChildren;
            set => SetProperty(ref _isLoadingChildren, value);
        }

        public bool HasLazyPlaceholder =>
            Children.Count == 1 && Children[0].IsLazyPlaceholder;

        public bool NeedsLazyLoad =>
            HasLazyPlaceholder && !string.IsNullOrEmpty(ExpressionPath);

        public static WatchItemViewModel CreateLazyPlaceholder()
        {
            return new WatchItemViewModel(CodeRadarLimits.StatusNotLoaded)
            {
                IsLazyPlaceholder = true,
                Value = string.Empty,
                Type = string.Empty,
                IsValid = false
            };
        }

        // True when the value shape suggests this node has inspectable members
        // (object reference / collection / struct). Debugger values for objects
        // display as "{Namespace.Type}" or "{Count = N}".
        public bool LikelyHasChildren()
        {
            if (!IsValid || IsNull) return false;
            var v = (Value ?? string.Empty).Trim();
            if (v.Length > 1 && v[0] == '{' && v[v.Length - 1] == '}') return true;
            var t = (Type ?? string.Empty);
            if (t.Contains("[")) return true;                 // arrays / indexers
            if (t.Contains("List<")) return true;
            if (t.Contains("Dictionary<")) return true;
            if (t.Contains("HashSet<")) return true;
            if (t.Contains("Queue<")) return true;
            if (t.Contains("Stack<")) return true;
            if (t.Contains("IEnumerable")) return true;
            if (t.Contains("Collection")) return true;
            return false;
        }

        // --- Child collections ---

        public ObservableCollection<WatchItemViewModel> Children { get; }

        public ObservableCollection<WatchHistoryEntry> History { get; }

        public const int HistoryCapacity = 100;

        public ObservableCollection<WatchSnapshot> Snapshots { get; }

        public bool HasHistory => History.Count > 0;
        public bool HasSnapshots => Snapshots.Count > 0;

        // --- Updates ---

        public void UpdateFrom(VariableNode node) => UpdateFrom(node, preserveLazyShape: false);

        // When <paramref name="preserveLazyShape"/> is true, the evaluator is telling us
        // the children weren't fetched (e.g. a depth-0 refresh). We keep any existing
        // lazy placeholder or real children rather than clearing them, and only refresh
        // the scalar fields. First-time lazy entries get a placeholder so the tree
        // still shows an expand chevron.
        public void UpdateFrom(VariableNode node, bool preserveLazyShape)
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

            if (preserveLazyShape)
            {
                // Install placeholder once for containers so the user can expand later.
                if (Children.Count == 0 && LikelyHasChildren())
                {
                    Children.Add(CreateLazyPlaceholder());
                }
                return;
            }

            ReconcileChildren(node);
            AddLazyPlaceholdersToChildren();
        }

        // Install lazy placeholders on each direct child that looks like a container
        // but currently has no children loaded. This way the user can drill into any
        // level via click, without eager recursive evaluation.
        private void AddLazyPlaceholdersToChildren()
        {
            foreach (var child in Children)
            {
                if (child.IsLazyPlaceholder) continue;
                if (child.Children.Count > 0) continue;
                if (string.IsNullOrEmpty(child.ExpressionPath)) continue;
                if (!child.LikelyHasChildren()) continue;
                child.Children.Add(CreateLazyPlaceholder());
            }
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

        // --- Search ---

        public bool ApplySearch(string query) => ApplySearchWithCount(query, out _);

        public bool ApplySearchWithCount(string query, out int matchCount)
        {
            if (string.IsNullOrEmpty(query))
            {
                IsSearchMatch = false;
                HasMatchingDescendant = false;
                foreach (var c in Children) c.ApplySearchWithCount(null, out _);
                matchCount = 0;
                return false;
            }

            // Skip lazy placeholders entirely so search count isn't polluted.
            if (IsLazyPlaceholder)
            {
                IsSearchMatch = false;
                HasMatchingDescendant = false;
                matchCount = 0;
                return false;
            }

            bool selfMatches =
                (Name?.IndexOf(query, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                || (Value?.IndexOf(query, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;

            bool descendantMatches = false;
            int subtotal = selfMatches ? 1 : 0;
            foreach (var child in Children)
            {
                if (child.ApplySearchWithCount(query, out int childCount))
                    descendantMatches = true;
                subtotal += childCount;
            }

            IsSearchMatch = selfMatches;
            HasMatchingDescendant = descendantMatches;

            if (descendantMatches)
                IsExpanded = true;

            matchCount = subtotal;
            return selfMatches || descendantMatches;
        }

        // --- Reconcile ---

        private void ReconcileChildren(VariableNode node)
        {
            int nodeChildren = node.Children.Count;

            // Strip any existing lazy placeholder - real children are replacing it.
            if (Children.Count == 1 && Children[0].IsLazyPlaceholder)
                Children.Clear();

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
