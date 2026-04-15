using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using CodeRadar.Models;
using CodeRadar.Services;
using Microsoft.VisualStudio.Threading;

namespace CodeRadar.ViewModels
{
    public sealed class CodeRadarViewModel : ObservableObject, IDisposable
    {
        private readonly IDebuggerService _debugger;
        private readonly IExpressionEvaluatorService _evaluator;
        private readonly JoinableTaskFactory _jtf;

        private readonly Dictionary<Guid, WatchItemViewModel> _watchVmsById = new Dictionary<Guid, WatchItemViewModel>();
        private readonly Dictionary<Guid, WatchExpression> _watchesById = new Dictionary<Guid, WatchExpression>();
        private readonly HashSet<string> _pinnedFrameKeys = new HashSet<string>(StringComparer.Ordinal);

        private DebuggerState _state;
        private string _stateText = "Design";
        private string _newWatchText;
        private string _stackFilter;
        private string _watchSearch;
        private int _watchSearchMatchCount;
        private int _breakCount;
        private DateTime? _lastBreakAt;
        private ExceptionInfo _lastException;
        private string _statusMessage;
        private System.Windows.Threading.DispatcherTimer _statusTimer;
        private int _disposed;

        public const int RecentExpressionCapacity = 15;

        public CodeRadarViewModel(IDebuggerService debugger, IExpressionEvaluatorService evaluator, JoinableTaskFactory jtf)
        {
            _debugger = debugger ?? throw new ArgumentNullException(nameof(debugger));
            _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
            _jtf = jtf ?? throw new ArgumentNullException(nameof(jtf));

            StackFrames = new ObservableCollection<StackFrameViewModel>();
            Threads = new ObservableCollection<ThreadInfo>();
            Watches = new ObservableCollection<WatchItemViewModel>();
            RecentExpressions = new ObservableCollection<string>();

            StackView = CollectionViewSource.GetDefaultView(StackFrames);
            StackView.Filter = FilterStackFrame;

            var pinnedSource = new CollectionViewSource { Source = StackFrames };
            pinnedSource.View.Filter = o => o is StackFrameViewModel f && f.IsPinned;
            PinnedFrames = pinnedSource.View;

            AddWatchCommand = new RelayCommand(_ => AddWatch(), _ => !string.IsNullOrWhiteSpace(NewWatchText));
            RemoveWatchCommand = new RelayCommand(RemoveWatch, w => w is WatchItemViewModel);
            RefreshCommand = new RelayCommand(async () => await RefreshAsync(CancellationToken.None));
            ClearExceptionCommand = new RelayCommand(() => LastException = null, () => LastException != null);
            TogglePinCommand = new RelayCommand(TogglePin, f => f is StackFrameViewModel);
            TogglePreviewSequenceCommand = new RelayCommand(TogglePreviewSequence, w => w is WatchItemViewModel);
            ExportWatchCommand = new RelayCommand(ExportWatch, w => w is WatchItemViewModel);
            AckChangeCommand = new RelayCommand(w => (w as WatchItemViewModel)?.AckChange(), w => w is WatchItemViewModel);
            UseRecentExpressionCommand = new RelayCommand(UseRecentExpression, e => e is string);

            CopyValueCommand       = new RelayCommand(CopyValue,     w => w is WatchItemViewModel);
            CopyPathCommand        = new RelayCommand(CopyPath,      w => w is WatchItemViewModel vm && vm.IsAddressable);
            CopyAsTextCommand      = new RelayCommand(w => CopyFormatted(w, Views.ExportFormat.Text,   "text"),   w => w is WatchItemViewModel);
            CopyAsJsonCommand      = new RelayCommand(w => CopyFormatted(w, Views.ExportFormat.Json,   "JSON"),   w => w is WatchItemViewModel);
            CopyAsCSharpCommand    = new RelayCommand(w => CopyFormatted(w, Views.ExportFormat.CSharp, "C#"),     w => w is WatchItemViewModel);
            PinAsWatchCommand      = new RelayCommand(PinAsWatch,    w => w is WatchItemViewModel vm && vm.IsAddressable);

            ShowHistoryCommand = new RelayCommand(ShowHistory, w => w is WatchItemViewModel);
            SnapshotWatchCommand = new RelayCommand(SnapshotWatch, w => w is WatchItemViewModel);
            CompareSnapshotsCommand = new RelayCommand(CompareSnapshots, w => w is WatchItemViewModel vm && vm.Snapshots.Count >= 2);
            ClearSnapshotsCommand = new RelayCommand(ClearSnapshots, w => w is WatchItemViewModel vm && vm.Snapshots.Count > 0);
            RevealAsWatchesCommand = new RelayCommand(RevealAsWatches, w => w is WatchItemViewModel);
            DecomposeLinqCommand = new RelayCommand(DecomposeLinq, w => w is WatchItemViewModel);
            ClearWatchSearchCommand = new RelayCommand(() => WatchSearch = string.Empty, () => !string.IsNullOrEmpty(WatchSearch));

            _state = _debugger.CurrentState;
            _stateText = _state.ToString();

            _debugger.StateChanged += OnDebuggerStateChanged;
            _debugger.BreakModeEntered += OnBreakModeEntered;
            _debugger.ExceptionRaised += OnExceptionRaised;
        }

        public ObservableCollection<StackFrameViewModel> StackFrames { get; }
        public ICollectionView StackView { get; }
        public ICollectionView PinnedFrames { get; }
        public ObservableCollection<ThreadInfo> Threads { get; }
        public ObservableCollection<WatchItemViewModel> Watches { get; }
        public ObservableCollection<string> RecentExpressions { get; }

        public DebuggerState State
        {
            get => _state;
            private set
            {
                if (SetProperty(ref _state, value))
                {
                    StateText = value.ToString();
                    OnPropertyChanged(nameof(IsInBreakMode));
                }
            }
        }

        public string StateText
        {
            get => _stateText;
            private set => SetProperty(ref _stateText, value);
        }

        public bool IsInBreakMode => _state == DebuggerState.Break;

        public string NewWatchText
        {
            get => _newWatchText;
            set
            {
                if (SetProperty(ref _newWatchText, value))
                {
                    (AddWatchCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public string StackFilter
        {
            get => _stackFilter;
            set
            {
                if (SetProperty(ref _stackFilter, value))
                    StackView.Refresh();
            }
        }

        public string WatchSearch
        {
            get => _watchSearch;
            set
            {
                if (SetProperty(ref _watchSearch, value))
                {
                    ApplyWatchSearch();
                    OnPropertyChanged(nameof(HasWatchSearch));
                    (ClearWatchSearchCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool HasWatchSearch => !string.IsNullOrEmpty(_watchSearch);

        public int WatchSearchMatchCount
        {
            get => _watchSearchMatchCount;
            private set => SetProperty(ref _watchSearchMatchCount, value);
        }

        public int BreakCount
        {
            get => _breakCount;
            private set => SetProperty(ref _breakCount, value);
        }

        public DateTime? LastBreakAt
        {
            get => _lastBreakAt;
            private set
            {
                if (SetProperty(ref _lastBreakAt, value))
                    OnPropertyChanged(nameof(LastBreakAtDisplay));
            }
        }

        public string LastBreakAtDisplay =>
            _lastBreakAt.HasValue ? _lastBreakAt.Value.ToLocalTime().ToString("HH:mm:ss") : "-";

        public ExceptionInfo LastException
        {
            get => _lastException;
            private set
            {
                if (SetProperty(ref _lastException, value))
                {
                    OnPropertyChanged(nameof(HasException));
                    (ClearExceptionCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool HasException => _lastException != null;
        public bool HasPinnedFrames => _pinnedFrameKeys.Count > 0;
        public bool HasRecentExpressions => RecentExpressions.Count > 0;

        public System.Windows.Input.ICommand AddWatchCommand { get; }
        public System.Windows.Input.ICommand RemoveWatchCommand { get; }
        public System.Windows.Input.ICommand RefreshCommand { get; }
        public System.Windows.Input.ICommand ClearExceptionCommand { get; }
        public System.Windows.Input.ICommand TogglePinCommand { get; }
        public System.Windows.Input.ICommand TogglePreviewSequenceCommand { get; }
        public System.Windows.Input.ICommand ExportWatchCommand { get; }
        public System.Windows.Input.ICommand AckChangeCommand { get; }
        public System.Windows.Input.ICommand ShowHistoryCommand { get; }
        public System.Windows.Input.ICommand SnapshotWatchCommand { get; }
        public System.Windows.Input.ICommand CompareSnapshotsCommand { get; }
        public System.Windows.Input.ICommand ClearSnapshotsCommand { get; }
        public System.Windows.Input.ICommand RevealAsWatchesCommand { get; }
        public System.Windows.Input.ICommand DecomposeLinqCommand { get; }
        public System.Windows.Input.ICommand ClearWatchSearchCommand { get; }
        public System.Windows.Input.ICommand UseRecentExpressionCommand { get; }
        public System.Windows.Input.ICommand CopyValueCommand { get; }
        public System.Windows.Input.ICommand CopyPathCommand { get; }
        public System.Windows.Input.ICommand CopyAsTextCommand { get; }
        public System.Windows.Input.ICommand CopyAsJsonCommand { get; }
        public System.Windows.Input.ICommand CopyAsCSharpCommand { get; }
        public System.Windows.Input.ICommand PinAsWatchCommand { get; }

        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                if (SetProperty(ref _statusMessage, value))
                    OnPropertyChanged(nameof(HasStatusMessage));
            }
        }

        public bool HasStatusMessage => !string.IsNullOrEmpty(_statusMessage);

        public event EventHandler<ExportRequestedEventArgs> ExportRequested;
        public event EventHandler<HistoryRequestedEventArgs> HistoryRequested;
        public event EventHandler<CompareRequestedEventArgs> CompareRequested;
        public event EventHandler<DecomposeRequestedEventArgs> DecomposeRequested;

        private void OnDebuggerStateChanged(object sender, DebuggerStateChangedEventArgs e)
        {
            _jtf.RunAsync(async () =>
            {
                await _jtf.SwitchToMainThreadAsync();
                State = e.NewState;
                if (e.NewState != DebuggerState.Break)
                {
                    StackFrames.Clear();
                    Threads.Clear();
                    foreach (var w in Watches)
                    {
                        w.IsValid = false;
                        w.Value = "<not in break mode>";
                        w.HasChanged = false;
                    }
                }
            }).Task.Forget();
        }

        private void OnBreakModeEntered(object sender, EventArgs e)
        {
            _jtf.RunAsync(async () => await RefreshAsync(CancellationToken.None)).Task.Forget();
        }

        private void OnExceptionRaised(object sender, ExceptionInfo e)
        {
            _jtf.RunAsync(async () =>
            {
                await _jtf.SwitchToMainThreadAsync();
                LastException = e;
            }).Task.Forget();
        }

        public async Task RefreshAsync(CancellationToken cancellationToken)
        {
            if (_disposed != 0) return;
            if (_debugger.CurrentState != DebuggerState.Break) return;

            var stack = await _debugger.GetCurrentStackAsync(cancellationToken).ConfigureAwait(true);
            var threads = await _debugger.GetThreadsAsync(cancellationToken).ConfigureAwait(true);

            var evaluated = new List<(Guid id, VariableNode node)>(_watchesById.Count);
            foreach (var kvp in _watchesById)
            {
                var vm = _watchVmsById[kvp.Key];
                VariableNode node = vm.IsSequencePreview
                    ? await _evaluator.EvaluateSequenceAsync(kvp.Value.Expression, maxItems: 50, cancellationToken).ConfigureAwait(true)
                    : await _evaluator.EvaluateAsync(kvp.Value.Expression, maxChildDepth: 1, cancellationToken).ConfigureAwait(true);
                evaluated.Add((kvp.Key, node));
            }

            await _jtf.SwitchToMainThreadAsync(cancellationToken);

            BreakCount++;
            LastBreakAt = DateTime.UtcNow;

            StackFrames.Clear();
            foreach (var frame in stack)
            {
                var fvm = new StackFrameViewModel(frame);
                fvm.IsPinned = _pinnedFrameKeys.Contains(fvm.PinKey);
                StackFrames.Add(fvm);
            }
            OnPropertyChanged(nameof(HasPinnedFrames));

            Threads.Clear();
            foreach (var t in threads) Threads.Add(t);

            foreach (var (id, node) in evaluated)
            {
                if (_watchVmsById.TryGetValue(id, out var vm))
                {
                    vm.UpdateFrom(node);
                    vm.RecordHistory(BreakCount, LastBreakAt ?? DateTime.UtcNow);
                }
            }

            // Re-apply any active search to the new evaluation results.
            if (HasWatchSearch) ApplyWatchSearch();
        }

        private void AddWatch()
        {
            if (string.IsNullOrWhiteSpace(NewWatchText)) return;
            AddWatchExpression(NewWatchText);
            NewWatchText = string.Empty;
        }

        internal void AddWatchFromExternal(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression)) return;
            AddWatchExpression(expression);
            Flash("Added watch: " + expression);
        }

        private WatchItemViewModel AddWatchExpression(string expression)
        {
            var expr = new WatchExpression(expression);
            var vm = new WatchItemViewModel(expr.Expression) { ExpressionPath = expr.Expression };
            _watchesById[expr.Id] = expr;
            _watchVmsById[expr.Id] = vm;
            vm.Tag(expr.Id);
            Watches.Add(vm);

            RecentExpressions.Remove(expr.Expression);
            RecentExpressions.Insert(0, expr.Expression);
            while (RecentExpressions.Count > RecentExpressionCapacity)
                RecentExpressions.RemoveAt(RecentExpressions.Count - 1);
            OnPropertyChanged(nameof(HasRecentExpressions));

            if (_debugger.CurrentState == DebuggerState.Break)
            {
                _jtf.RunAsync(async () =>
                {
                    var node = await _evaluator.EvaluateAsync(expr.Expression, maxChildDepth: 1, CancellationToken.None);
                    await _jtf.SwitchToMainThreadAsync();
                    vm.UpdateFrom(node);
                    vm.RecordHistory(BreakCount, DateTime.UtcNow);
                }).Task.Forget();
            }
            return vm;
        }

        private void RemoveWatch(object parameter)
        {
            if (!(parameter is WatchItemViewModel vm)) return;
            var id = vm.GetTag();
            if (id == Guid.Empty) return;
            _watchesById.Remove(id);
            _watchVmsById.Remove(id);
            Watches.Remove(vm);
        }

        private void UseRecentExpression(object parameter)
        {
            if (parameter is string s) NewWatchText = s;
        }

        private void TogglePreviewSequence(object parameter)
        {
            if (!(parameter is WatchItemViewModel vm)) return;
            var id = vm.GetTag();
            if (id == Guid.Empty || !_watchesById.TryGetValue(id, out var expr)) return;

            vm.IsSequencePreview = !vm.IsSequencePreview;
            if (_debugger.CurrentState != DebuggerState.Break) return;

            _jtf.RunAsync(async () =>
            {
                var node = vm.IsSequencePreview
                    ? await _evaluator.EvaluateSequenceAsync(expr.Expression, maxItems: 50, CancellationToken.None)
                    : await _evaluator.EvaluateAsync(expr.Expression, maxChildDepth: 1, CancellationToken.None);
                await _jtf.SwitchToMainThreadAsync();
                vm.UpdateFrom(node);
            }).Task.Forget();
        }

        private void ExportWatch(object parameter)
        {
            if (!(parameter is WatchItemViewModel vm)) return;

            var id = vm.GetTag();
            bool isRootWatch = id != Guid.Empty && _watchesById.ContainsKey(id);

            if (isRootWatch && _debugger.CurrentState == DebuggerState.Break)
            {
                var rootExpr = _watchesById[id].Expression;
                _jtf.RunAsync(async () =>
                {
                    VariableNode node;
                    try
                    {
                        node = await _evaluator.EvaluateAsync(rootExpr, maxChildDepth: 5, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        node = new VariableNode(rootExpr, "<eval failed: " + ex.Message + ">",
                            string.Empty, isValid: false, isNull: false,
                            children: System.Array.Empty<VariableNode>());
                    }
                    await _jtf.SwitchToMainThreadAsync();
                    ExportRequested?.Invoke(this, new ExportRequestedEventArgs(node, rootExpr));
                }).Task.Forget();
                return;
            }

            var snapshot = ToVariableNode(vm);
            ExportRequested?.Invoke(this, new ExportRequestedEventArgs(snapshot, vm.Name));
        }

        private void CopyValue(object parameter)
        {
            if (!(parameter is WatchItemViewModel vm)) return;
            TrySetClipboard(vm.Value ?? string.Empty);
            Flash("Copied value");
        }

        private void CopyPath(object parameter)
        {
            if (!(parameter is WatchItemViewModel vm)) return;
            if (string.IsNullOrEmpty(vm.ExpressionPath))
            {
                Flash("No evaluable path for this row");
                return;
            }
            TrySetClipboard(vm.ExpressionPath);
            Flash("Copied expression path");
        }

        private void CopyFormatted(object parameter, Views.ExportFormat format, string formatLabel)
        {
            if (!(parameter is WatchItemViewModel vm)) return;
            var node = ToVariableNode(vm);
            var text = Views.ObjectExporter.Export(node, format);
            TrySetClipboard(text);
            Flash("Copied as " + formatLabel);
        }

        private void PinAsWatch(object parameter)
        {
            if (!(parameter is WatchItemViewModel vm)) return;
            if (string.IsNullOrEmpty(vm.ExpressionPath)) { Flash("Cannot pin - no evaluable path"); return; }
            AddWatchExpression(vm.ExpressionPath);
            Flash("Pinned " + vm.ExpressionPath + " as watch");
        }

        private static void TrySetClipboard(string text)
        {
            // Retry briefly against transient COMException when the clipboard is held by another app.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    System.Windows.Clipboard.SetDataObject(text ?? string.Empty, copy: true);
                    return;
                }
                catch
                {
                    System.Threading.Thread.Sleep(20);
                }
            }
        }

        private void Flash(string message)
        {
            StatusMessage = message;

            if (_statusTimer == null)
            {
                _statusTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                _statusTimer.Tick += (s, e) =>
                {
                    _statusTimer.Stop();
                    StatusMessage = string.Empty;
                };
            }
            _statusTimer.Stop();
            _statusTimer.Start();
        }

        private void ShowHistory(object parameter)
        {
            if (!(parameter is WatchItemViewModel vm)) return;
            HistoryRequested?.Invoke(this, new HistoryRequestedEventArgs(vm));
        }

        private void SnapshotWatch(object parameter)
        {
            if (!(parameter is WatchItemViewModel vm)) return;
            var id = vm.GetTag();

            if (id != Guid.Empty && _watchesById.TryGetValue(id, out var expr)
                && _debugger.CurrentState == DebuggerState.Break)
            {
                _jtf.RunAsync(async () =>
                {
                    VariableNode node;
                    try
                    {
                        node = await _evaluator.EvaluateAsync(expr.Expression, maxChildDepth: 5, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        node = new VariableNode(expr.Expression, "<eval failed: " + ex.Message + ">",
                            string.Empty, isValid: false, isNull: false, children: System.Array.Empty<VariableNode>());
                    }
                    await _jtf.SwitchToMainThreadAsync();
                    var label = $"#{vm.Snapshots.Count + 1}";
                    vm.AddSnapshot(new WatchSnapshot(label, DateTime.UtcNow, node, BreakCount));
                    (CompareSnapshotsCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (ClearSnapshotsCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }).Task.Forget();
                return;
            }

            // Fallback: snapshot whatever we already have materialised for the row.
            var label2 = $"#{vm.Snapshots.Count + 1}";
            vm.AddSnapshot(new WatchSnapshot(label2, DateTime.UtcNow, ToVariableNode(vm), BreakCount));
            (CompareSnapshotsCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ClearSnapshotsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void CompareSnapshots(object parameter)
        {
            if (!(parameter is WatchItemViewModel vm) || vm.Snapshots.Count < 2) return;
            CompareRequested?.Invoke(this, new CompareRequestedEventArgs(vm));
        }

        private void ClearSnapshots(object parameter)
        {
            if (!(parameter is WatchItemViewModel vm)) return;
            vm.Snapshots.Clear();
            (CompareSnapshotsCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ClearSnapshotsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void RevealAsWatches(object parameter)
        {
            if (!(parameter is WatchItemViewModel vm)) return;

            // Only reveal for root watches - the stored expression gives us a reliable parent path.
            var id = vm.GetTag();
            if (id == Guid.Empty || !_watchesById.TryGetValue(id, out var expr)) return;

            if (vm.Children.Count == 0 && _debugger.CurrentState == DebuggerState.Break)
            {
                _jtf.RunAsync(async () =>
                {
                    var node = await _evaluator.EvaluateAsync(expr.Expression, maxChildDepth: 1, CancellationToken.None);
                    await _jtf.SwitchToMainThreadAsync();
                    vm.UpdateFrom(node);
                    DoReveal(vm, expr.Expression);
                }).Task.Forget();
                return;
            }

            DoReveal(vm, expr.Expression);
        }

        private void DoReveal(WatchItemViewModel vm, string parentExpr)
        {
            foreach (var child in vm.Children)
            {
                if (string.IsNullOrWhiteSpace(child.Name) || child.Name.Equals("Raw View", StringComparison.Ordinal))
                    continue;

                string memberExpr;
                if (child.Name.StartsWith("[", StringComparison.Ordinal) && child.Name.EndsWith("]", StringComparison.Ordinal))
                    memberExpr = parentExpr + child.Name;
                else
                    memberExpr = parentExpr + "." + child.Name;

                AddWatchExpression(memberExpr);
            }
        }

        private void DecomposeLinq(object parameter)
        {
            if (!(parameter is WatchItemViewModel vm)) return;
            var id = vm.GetTag();
            if (id == Guid.Empty || !_watchesById.TryGetValue(id, out var expr)) return;
            if (_debugger.CurrentState != DebuggerState.Break) return;

            var segments = LinqChainAnalyzer.Parse(expr.Expression);
            if (segments.Count == 0) return;

            _jtf.RunAsync(async () =>
            {
                var results = new List<LinqStepResult>(segments.Count);
                foreach (var segment in segments)
                {
                    LinqStepResult step;
                    try
                    {
                        var node = await _evaluator.EvaluateSequenceAsync(segment.CumulativeExpression, maxItems: 50, CancellationToken.None);
                        if (!node.IsValid)
                        {
                            node = await _evaluator.EvaluateAsync(segment.CumulativeExpression, maxChildDepth: 1, CancellationToken.None);
                        }
                        int? count = node.IsValid ? (int?)node.Children.Count : null;
                        bool truncated = node.Children.Count >= 50;
                        step = new LinqStepResult(segment.Label, segment.CumulativeExpression, count,
                            truncated, node.Children, node.IsValid ? string.Empty : node.Value);
                    }
                    catch (Exception ex)
                    {
                        step = new LinqStepResult(segment.Label, segment.CumulativeExpression, null,
                            false, System.Array.Empty<VariableNode>(), ex.Message);
                    }
                    results.Add(step);
                }

                await _jtf.SwitchToMainThreadAsync();
                DecomposeRequested?.Invoke(this, new DecomposeRequestedEventArgs(expr.Expression, results));
            }).Task.Forget();
        }

        private void ApplyWatchSearch()
        {
            int total = 0;
            foreach (var w in Watches)
                total += CountMatches(w, _watchSearch);
            WatchSearchMatchCount = total;
        }

        private static int CountMatches(WatchItemViewModel vm, string query)
        {
            vm.ApplySearch(query);
            int count = vm.IsSearchMatch ? 1 : 0;
            foreach (var c in vm.Children) count += CountMatches(c, query);
            return count;
        }

        private void TogglePin(object parameter)
        {
            if (!(parameter is StackFrameViewModel frame)) return;

            if (_pinnedFrameKeys.Remove(frame.PinKey))
                frame.IsPinned = false;
            else
            {
                _pinnedFrameKeys.Add(frame.PinKey);
                frame.IsPinned = true;
            }

            foreach (var other in StackFrames)
            {
                if (other != frame && other.PinKey == frame.PinKey)
                    other.IsPinned = frame.IsPinned;
            }

            PinnedFrames.Refresh();
            OnPropertyChanged(nameof(HasPinnedFrames));
        }

        private bool FilterStackFrame(object item)
        {
            if (string.IsNullOrWhiteSpace(_stackFilter)) return true;
            if (!(item is StackFrameViewModel frame)) return false;
            var needle = _stackFilter.Trim();
            return (frame.FunctionName?.IndexOf(needle, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                || (frame.Module?.IndexOf(needle, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;
        }

        private static VariableNode ToVariableNode(WatchItemViewModel vm)
        {
            var children = new List<VariableNode>(vm.Children.Count);
            foreach (var child in vm.Children)
                children.Add(ToVariableNode(child));
            return new VariableNode(vm.Name ?? string.Empty, vm.Value ?? string.Empty, vm.Type ?? string.Empty,
                vm.IsValid, vm.IsNull, children);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _debugger.StateChanged -= OnDebuggerStateChanged;
            _debugger.BreakModeEntered -= OnBreakModeEntered;
            _debugger.ExceptionRaised -= OnExceptionRaised;
            _statusTimer?.Stop();
            _statusTimer = null;
        }
    }

    // Event-argument carriers for dialog requests.

    public sealed class ExportRequestedEventArgs : EventArgs
    {
        public ExportRequestedEventArgs(VariableNode node, string caption) { Node = node; Caption = caption; }
        public VariableNode Node { get; }
        public string Caption { get; }
    }

    public sealed class HistoryRequestedEventArgs : EventArgs
    {
        public HistoryRequestedEventArgs(WatchItemViewModel watch) { Watch = watch; }
        public WatchItemViewModel Watch { get; }
    }

    public sealed class CompareRequestedEventArgs : EventArgs
    {
        public CompareRequestedEventArgs(WatchItemViewModel watch) { Watch = watch; }
        public WatchItemViewModel Watch { get; }
    }

    public sealed class DecomposeRequestedEventArgs : EventArgs
    {
        public DecomposeRequestedEventArgs(string originalExpression, IReadOnlyList<LinqStepResult> steps)
        {
            OriginalExpression = originalExpression;
            Steps = steps;
        }
        public string OriginalExpression { get; }
        public IReadOnlyList<LinqStepResult> Steps { get; }
    }

    internal static class WatchItemViewModelTagExtensions
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WatchItemViewModel, object> _tags
            = new System.Runtime.CompilerServices.ConditionalWeakTable<WatchItemViewModel, object>();

        public static void Tag(this WatchItemViewModel vm, Guid id)
        {
            _tags.Remove(vm);
            _tags.Add(vm, id);
        }

        public static Guid GetTag(this WatchItemViewModel vm)
            => _tags.TryGetValue(vm, out var o) && o is Guid g ? g : Guid.Empty;
    }

    internal static class TaskForgetExtensions
    {
        public static void Forget(this Task task)
        {
            if (task is null) return;
            _ = task.ContinueWith(t => { _ = t.Exception; },
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }
    }
}
