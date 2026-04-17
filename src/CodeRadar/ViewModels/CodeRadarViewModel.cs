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
        private readonly IImageExtractor _imageExtractor;
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
        private CancellationTokenSource _refreshCts;
        private int _disposed;

        public const int RecentExpressionCapacity = 15;

        public CodeRadarViewModel(IDebuggerService debugger, IExpressionEvaluatorService evaluator,
            IImageExtractor imageExtractor, JoinableTaskFactory jtf)
        {
            _debugger = debugger ?? throw new ArgumentNullException(nameof(debugger));
            _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
            _imageExtractor = imageExtractor;
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
            RefreshCommand = new RelayCommand(async () =>
            {
                _refreshCts?.Cancel();
                _refreshCts?.Dispose();
                var cts = new CancellationTokenSource(CodeRadarLimits.RefreshBatchBudget);
                _refreshCts = cts;
                try { await RefreshAsync(cts.Token); }
                catch (OperationCanceledException) { }
            });
            EnsureChildrenLoadedCommand = new RelayCommand(
                w => EnsureChildrenLoaded(w as WatchItemViewModel),
                w => w is WatchItemViewModel vm && vm.NeedsLazyLoad && !vm.IsLoadingChildren);
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
            ShowImageCommand = new RelayCommand(ShowImage, w => w is WatchItemViewModel vm && vm.IsAddressable);
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
        public System.Windows.Input.ICommand ShowImageCommand { get; }
        public System.Windows.Input.ICommand EnsureChildrenLoadedCommand { get; }

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
        public event EventHandler<ImageRequestedEventArgs> ImageRequested;

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
            _jtf.RunAsync(async () =>
            {
                _refreshCts?.Cancel();
                _refreshCts?.Dispose();
                var cts = new CancellationTokenSource(CodeRadarLimits.RefreshBatchBudget);
                _refreshCts = cts;
                try
                {
                    await RefreshAsync(cts.Token);
                }
                catch (OperationCanceledException) { }
            }).Task.Forget();
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

            IReadOnlyList<StackFrameInfo> stack;
            IReadOnlyList<ThreadInfo> threads;
            try
            {
                stack = await _debugger.GetCurrentStackAsync(cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException) { throw; }
            catch { stack = System.Array.Empty<StackFrameInfo>(); }

            try
            {
                threads = await _debugger.GetThreadsAsync(cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException) { throw; }
            catch { threads = System.Array.Empty<ThreadInfo>(); }

            // Evaluate each watch independently: one failure/timeout doesn't abort the batch.
            // Collapsed watches only evaluate the root value (depth 0) - their children
            // are fetched lazily on expand. Expanded and sequence-preview watches still
            // get a one-level walk so the user sees fresh children after each break.
            var evaluated = new List<(Guid id, VariableNode node, bool preserveLazyShape)>(_watchesById.Count);
            foreach (var kvp in _watchesById)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var vm = _watchVmsById[kvp.Key];
                bool preserveLazy = !vm.IsExpanded && !vm.IsSequencePreview;
                int depth = preserveLazy ? CodeRadarLimits.RefreshDepthCollapsed : CodeRadarLimits.RefreshDepthExpanded;

                VariableNode node;
                try
                {
                    using (var perWatch = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        perWatch.CancelAfter(CodeRadarLimits.PerWatchBudget);
                        node = vm.IsSequencePreview
                            ? await _evaluator.EvaluateSequenceAsync(kvp.Value.Expression, maxItems: CodeRadarLimits.SequencePreviewSize, perWatch.Token).ConfigureAwait(true)
                            : await _evaluator.EvaluateAsync(kvp.Value.Expression, maxChildDepth: depth, perWatch.Token).ConfigureAwait(true);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    node = new VariableNode(kvp.Value.Expression, CodeRadarLimits.StatusTimedOut, string.Empty,
                        isValid: false, isNull: false, children: System.Array.Empty<VariableNode>());
                    preserveLazy = false;
                }
                catch (Exception ex)
                {
                    node = new VariableNode(kvp.Value.Expression, "<watch failed: " + ex.Message + ">",
                        string.Empty, isValid: false, isNull: false, children: System.Array.Empty<VariableNode>());
                    preserveLazy = false;
                }
                evaluated.Add((kvp.Key, node, preserveLazy));
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

            foreach (var (id, node, preserveLazy) in evaluated)
            {
                if (_watchVmsById.TryGetValue(id, out var vm))
                {
                    try
                    {
                        vm.UpdateFrom(node, preserveLazyShape: preserveLazy);
                        vm.RecordHistory(BreakCount, LastBreakAt ?? DateTime.UtcNow);
                    }
                    catch
                    {
                        // Never let a single watch's UI update kill the whole refresh.
                    }
                }
            }

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
                    using (var cts = new CancellationTokenSource(CodeRadarLimits.InlineEvalBudget))
                    {
                        // Depth 0 here: the first child fetch happens on lazy expand.
                        var node = await _evaluator.EvaluateAsync(expr.Expression, maxChildDepth: CodeRadarLimits.RefreshDepthCollapsed, cts.Token);
                        await _jtf.SwitchToMainThreadAsync();
                        vm.UpdateFrom(node, preserveLazyShape: true);
                        vm.RecordHistory(BreakCount, DateTime.UtcNow);
                    }
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
                using (var cts = new CancellationTokenSource(CodeRadarLimits.TogglePreviewBudget))
                {
                    var node = vm.IsSequencePreview
                        ? await _evaluator.EvaluateSequenceAsync(expr.Expression, maxItems: CodeRadarLimits.SequencePreviewSize, cts.Token)
                        : await _evaluator.EvaluateAsync(expr.Expression, maxChildDepth: CodeRadarLimits.RefreshDepthExpanded, cts.Token);
                    await _jtf.SwitchToMainThreadAsync();
                    vm.UpdateFrom(node);
                }
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
                        using (var cts = new CancellationTokenSource(CodeRadarLimits.ExportBudget))
                        {
                            node = await _evaluator.EvaluateAsync(rootExpr, maxChildDepth: CodeRadarLimits.ExportDepth, cts.Token);
                        }
                    }
                    catch (Exception ex)
                    {
                        node = new VariableNode(rootExpr, "<eval failed: " + ex.Message + ">",
                            string.Empty, isValid: false, isNull: false,
                            children: System.Array.Empty<VariableNode>());
                    }
                    await _jtf.SwitchToMainThreadAsync();
                    ExportRequested?.Invoke(this, new ExportRequestedEventArgs(node, rootExpr,
                        CreateReEvaluator(rootExpr)));
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

        // Triggered by the WPF TreeView when a node expands. Fetches one additional
        // depth level of children for that specific node, on demand. No-op if the node
        // already has real children loaded or a load is already in flight.
        internal void EnsureChildrenLoaded(WatchItemViewModel vm)
        {
            if (vm == null) return;
            if (!vm.NeedsLazyLoad) return;
            if (vm.IsLoadingChildren) return;
            if (_debugger.CurrentState != DebuggerState.Break) return;
            if (string.IsNullOrEmpty(vm.ExpressionPath)) return;

            vm.IsLoadingChildren = true;
            var expr = vm.ExpressionPath;

            _jtf.RunAsync(async () =>
            {
                VariableNode node = null;
                try
                {
                    using (var cts = new CancellationTokenSource(CodeRadarLimits.LazyExpandBudget))
                    {
                        node = await _evaluator.EvaluateAsync(expr, CodeRadarLimits.LazyExpandDepth, cts.Token);
                    }
                }
                catch (Exception)
                {
                    // Swallow - the placeholder already conveys that a load was attempted.
                }

                await _jtf.SwitchToMainThreadAsync();
                try
                {
                    if (node != null)
                        vm.UpdateFrom(node);
                }
                finally
                {
                    vm.IsLoadingChildren = false;
                }
            }).Task.Forget();
        }

        private void ShowImage(object parameter)
        {
            if (!(parameter is WatchItemViewModel vm)) return;
            if (string.IsNullOrEmpty(vm.ExpressionPath) || _imageExtractor == null) return;
            if (_debugger.CurrentState != DebuggerState.Break) return;

            var expr = vm.ExpressionPath;
            _jtf.RunAsync(async () =>
            {
                ImageExtractResult result;
                try
                {
                    using (var cts = new CancellationTokenSource(CodeRadarLimits.ImageExtractBudget))
                    {
                        result = await _imageExtractor.TryExtractAsync(expr, cts.Token);
                    }
                }
                catch (Exception ex)
                {
                    result = new ImageExtractResult { Success = false, Error = ex.Message };
                }

                await _jtf.SwitchToMainThreadAsync();
                ImageRequested?.Invoke(this, new ImageRequestedEventArgs(result, expr));
            }).Task.Forget();
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
                        using (var cts = new CancellationTokenSource(CodeRadarLimits.SnapshotBudget))
                        {
                            node = await _evaluator.EvaluateAsync(expr.Expression, maxChildDepth: CodeRadarLimits.SnapshotDepth, cts.Token);
                        }
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

            // Reveal needs real children. If the VM only has a lazy placeholder or
            // no children, evaluate once; otherwise reuse what we already materialised.
            bool needsEval = vm.Children.Count == 0 || vm.HasLazyPlaceholder;
            if (needsEval && _debugger.CurrentState == DebuggerState.Break)
            {
                _jtf.RunAsync(async () =>
                {
                    using (var cts = new CancellationTokenSource(CodeRadarLimits.InlineEvalBudget))
                    {
                        var node = await _evaluator.EvaluateAsync(expr.Expression, maxChildDepth: CodeRadarLimits.RefreshDepthExpanded, cts.Token);
                        await _jtf.SwitchToMainThreadAsync();
                        vm.UpdateFrom(node);
                        DoReveal(vm, expr.Expression);
                    }
                }).Task.Forget();
                return;
            }

            DoReveal(vm, expr.Expression);
        }

        private void DoReveal(WatchItemViewModel vm, string parentExpr)
        {
            foreach (var child in vm.Children)
            {
                if (child.IsLazyPlaceholder) continue;
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
                using (var cts = new CancellationTokenSource(CodeRadarLimits.LinqDecomposeBudget))
                {
                var results = new List<LinqStepResult>(segments.Count);
                int SampleCap = CodeRadarLimits.SequencePreviewSize;
                int CountCap  = CodeRadarLimits.MaxCountProbe;

                foreach (var segment in segments)
                {
                    if (cts.Token.IsCancellationRequested) break;
                    LinqStepResult step;
                    try
                    {
                        // Preview: materialise up to SampleCap items (for display only).
                        var preview = await _evaluator.EvaluateSequenceAsync(segment.CumulativeExpression, maxItems: SampleCap, cts.Token);
                        bool isEnumerable = preview.IsValid;
                        int sampleSize = isEnumerable ? preview.Children.Count : 0;
                        bool sampleTruncated = isEnumerable && sampleSize >= SampleCap;

                        // Real count: query the sequence separately so we don't mislead the user
                        // when the sample is capped. Capped at CountCap for safety.
                        int? totalCount = null;
                        bool countTruncated = false;
                        if (isEnumerable)
                        {
                            try
                            {
                                var (c, trunc) = await _evaluator.TryCountAsync(segment.CumulativeExpression, CountCap, cts.Token);
                                totalCount = c;
                                countTruncated = trunc;
                            }
                            catch { /* non-fatal: we still have the preview */ }
                        }

                        // Fallback: for non-enumerables (source scalar) show the scalar itself.
                        IReadOnlyList<VariableNode> samples = preview.Children;
                        string error = string.Empty;
                        if (!isEnumerable)
                        {
                            var scalar = await _evaluator.EvaluateAsync(segment.CumulativeExpression, maxChildDepth: 1, cts.Token);
                            samples = scalar.Children ?? System.Array.Empty<VariableNode>();
                            if (!scalar.IsValid) error = scalar.Value;
                        }

                        step = new LinqStepResult(segment.Label, segment.CumulativeExpression,
                            totalCount, countTruncated, sampleSize, sampleTruncated, samples, error);
                    }
                    catch (OperationCanceledException)
                    {
                        step = new LinqStepResult(segment.Label, segment.CumulativeExpression,
                            null, false, 0, false, System.Array.Empty<VariableNode>(), "Step evaluation timed out.");
                    }
                    catch (Exception ex)
                    {
                        step = new LinqStepResult(segment.Label, segment.CumulativeExpression,
                            null, false, 0, false, System.Array.Empty<VariableNode>(), ex.Message);
                    }
                    results.Add(step);
                }

                await _jtf.SwitchToMainThreadAsync();
                DecomposeRequested?.Invoke(this, new DecomposeRequestedEventArgs(expr.Expression, results));
                } // end using cts
            }).Task.Forget();
        }

        private void ApplyWatchSearch()
        {
            int total = 0;
            foreach (var w in Watches)
            {
                w.ApplySearchWithCount(_watchSearch, out int n);
                total += n;
            }
            WatchSearchMatchCount = total;
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
            {
                // Lazy placeholders are UI sentinels - never include them when we
                // snapshot, export, or compare user-visible data.
                if (child.IsLazyPlaceholder) continue;
                children.Add(ToVariableNode(child));
            }
            return new VariableNode(vm.Name ?? string.Empty, vm.Value ?? string.Empty, vm.Type ?? string.Empty,
                vm.IsValid, vm.IsNull, children);
        }

        private Func<int, CancellationToken, Task<VariableNode>> CreateReEvaluator(string expression)
        {
            return async (depth, ct) =>
            {
                if (_disposed != 0 || _debugger.CurrentState != DebuggerState.Break)
                    return new VariableNode(expression, "<not in break mode>", string.Empty,
                        isValid: false, isNull: false, children: System.Array.Empty<VariableNode>());

                return await _evaluator.EvaluateAsync(expression, depth, ct);
            };
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _debugger.StateChanged -= OnDebuggerStateChanged;
            _debugger.BreakModeEntered -= OnBreakModeEntered;
            _debugger.ExceptionRaised -= OnExceptionRaised;
            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            _refreshCts = null;
            _statusTimer?.Stop();
            _statusTimer = null;
        }
    }

    // Event-argument carriers for dialog requests.

    public sealed class ExportRequestedEventArgs : EventArgs
    {
        public ExportRequestedEventArgs(VariableNode node, string caption,
            Func<int, CancellationToken, Task<VariableNode>> reEvaluator = null)
        {
            Node = node;
            Caption = caption;
            ReEvaluator = reEvaluator;
        }
        public VariableNode Node { get; }
        public string Caption { get; }
        public Func<int, CancellationToken, Task<VariableNode>> ReEvaluator { get; }
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

    public sealed class ImageRequestedEventArgs : EventArgs
    {
        public ImageRequestedEventArgs(ImageExtractResult result, string expression)
        {
            Result = result;
            Expression = expression ?? string.Empty;
        }
        public ImageExtractResult Result { get; }
        public string Expression { get; }
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
