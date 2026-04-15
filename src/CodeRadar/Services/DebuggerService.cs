using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodeRadar.Models;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

namespace CodeRadar.Services
{
    internal sealed class DebuggerService : IDebuggerService
    {
        private readonly AsyncPackage _package;
        private readonly JoinableTaskFactory _jtf;
        private DTE2 _dte;
        private DebuggerEvents _debuggerEvents;
        private int _disposed;

        public DebuggerService(AsyncPackage package, JoinableTaskFactory joinableTaskFactory)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            _jtf = joinableTaskFactory ?? throw new ArgumentNullException(nameof(joinableTaskFactory));
            CurrentState = DebuggerState.Design;
        }

        public DebuggerState CurrentState { get; private set; }

        public event EventHandler<DebuggerStateChangedEventArgs> StateChanged;
        public event EventHandler BreakModeEntered;
        public event EventHandler<ExceptionInfo> ExceptionRaised;

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            await _jtf.SwitchToMainThreadAsync(cancellationToken);

            _dte = await _package.GetServiceAsync(typeof(SDTE)) as DTE2
                   ?? throw new InvalidOperationException("Could not obtain DTE.");

            _debuggerEvents = _dte.Events.DebuggerEvents;
            _debuggerEvents.OnEnterBreakMode += OnEnterBreakMode;
            _debuggerEvents.OnEnterRunMode += OnEnterRunMode;
            _debuggerEvents.OnEnterDesignMode += OnEnterDesignMode;
            _debuggerEvents.OnExceptionThrown += OnExceptionThrown;
            _debuggerEvents.OnExceptionNotHandled += OnExceptionNotHandled;

            CurrentState = MapMode(_dte.Debugger.CurrentMode);
        }

        private void OnEnterBreakMode(dbgEventReason reason, ref dbgExecutionAction executionAction)
        {
            SetState(DebuggerState.Break, reason.ToString());
            BreakModeEntered?.Invoke(this, EventArgs.Empty);
        }

        private void OnEnterRunMode(dbgEventReason reason) => SetState(DebuggerState.Running, reason.ToString());

        private void OnEnterDesignMode(dbgEventReason reason) => SetState(DebuggerState.Design, reason.ToString());

        private void OnExceptionThrown(string exceptionType, string name, int code, string description,
            ref dbgExceptionAction exceptionAction)
        {
            FireException(exceptionType, name, code, description, isHandled: true);
        }

        private void OnExceptionNotHandled(string exceptionType, string name, int code, string description,
            ref dbgExceptionAction exceptionAction)
        {
            FireException(exceptionType, name, code, description, isHandled: false);
        }

        private void FireException(string exceptionType, string name, int code, string description, bool isHandled)
        {
            IReadOnlyList<StackFrameInfo> frames;
            try
            {
                frames = CaptureStackOnUiThread();
            }
            catch
            {
                frames = Array.Empty<StackFrameInfo>();
            }

            int relevantIndex = -1;
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i].IsUserCode)
                {
                    relevantIndex = i;
                    break;
                }
            }

            var info = new ExceptionInfo(
                exceptionType: string.IsNullOrEmpty(name) ? exceptionType : name,
                message: description,
                code: code.ToString("X"),
                isHandled: isHandled,
                stackFrames: frames,
                relevantFrameIndex: relevantIndex,
                observedAtUtc: DateTime.UtcNow);

            try
            {
                ExceptionRaised?.Invoke(this, info);
            }
            catch
            {
            }
        }

        private void SetState(DebuggerState newState, string reason)
        {
            if (CurrentState == newState) return;
            CurrentState = newState;
            try
            {
                StateChanged?.Invoke(this, new DebuggerStateChangedEventArgs(newState, reason));
            }
            catch
            {
            }
        }

        public async Task<IReadOnlyList<ThreadInfo>> GetThreadsAsync(CancellationToken cancellationToken)
        {
            await _jtf.SwitchToMainThreadAsync(cancellationToken);
            if (_dte?.Debugger?.CurrentMode != dbgDebugMode.dbgBreakMode)
                return Array.Empty<ThreadInfo>();

            var programs = _dte.Debugger.CurrentProgram;
            if (programs == null) return Array.Empty<ThreadInfo>();

            var current = _dte.Debugger.CurrentThread;
            var list = new List<ThreadInfo>();
            foreach (EnvDTE.Thread t in _dte.Debugger.CurrentProgram.Threads)
            {
                try
                {
                    list.Add(new ThreadInfo(
                        id: t.ID,
                        name: t.Name,
                        location: t.Location,
                        isCurrent: current != null && current.ID == t.ID,
                        isFrozen: t.IsFrozen));
                }
                catch
                {
                }
            }
            return list;
        }

        public async Task<IReadOnlyList<StackFrameInfo>> GetCurrentStackAsync(CancellationToken cancellationToken)
        {
            await _jtf.SwitchToMainThreadAsync(cancellationToken);
            return CaptureStackOnUiThread();
        }

        private IReadOnlyList<StackFrameInfo> CaptureStackOnUiThread()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_dte?.Debugger?.CurrentMode != dbgDebugMode.dbgBreakMode)
                return Array.Empty<StackFrameInfo>();

            var thread = _dte.Debugger.CurrentThread;
            if (thread == null) return Array.Empty<StackFrameInfo>();

            var frames = new List<StackFrameInfo>();
            int i = 0;
            foreach (EnvDTE.StackFrame frame in thread.StackFrames)
            {
                try
                {
                    var function = frame.FunctionName ?? string.Empty;
                    var module = frame.Module ?? string.Empty;
                    var language = frame.Language ?? string.Empty;

                    string location = string.Empty;
                    try
                    {
                        var doc = _dte.ActiveDocument?.FullName;
                        if (!string.IsNullOrEmpty(doc))
                            location = doc;
                    }
                    catch { }

                    bool isUserCode = IsLikelyUserCode(module, function);

                    frames.Add(new StackFrameInfo(i, function, module, language, location, isUserCode));
                }
                catch
                {
                }
                i++;
            }
            return frames;
        }

        private static bool IsLikelyUserCode(string module, string function)
        {
            if (string.IsNullOrEmpty(module)) return false;
            string m = module.ToLowerInvariant();
            if (m.StartsWith("system") || m.StartsWith("microsoft.") ||
                m.StartsWith("mscorlib") || m.StartsWith("netstandard") ||
                m.StartsWith("presentationframework") || m.StartsWith("presentationcore") ||
                m.StartsWith("windowsbase") || m.StartsWith("clr.dll") ||
                m.StartsWith("coreclr") || m.StartsWith("ntdll") || m.StartsWith("kernel"))
            {
                return false;
            }
            return true;
        }

        private static DebuggerState MapMode(dbgDebugMode mode)
        {
            switch (mode)
            {
                case dbgDebugMode.dbgBreakMode: return DebuggerState.Break;
                case dbgDebugMode.dbgRunMode: return DebuggerState.Running;
                case dbgDebugMode.dbgDesignMode:
                default: return DebuggerState.Design;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            try
            {
                _jtf.Run(async () =>
                {
                    await _jtf.SwitchToMainThreadAsync();
                    if (_debuggerEvents != null)
                    {
                        _debuggerEvents.OnEnterBreakMode -= OnEnterBreakMode;
                        _debuggerEvents.OnEnterRunMode -= OnEnterRunMode;
                        _debuggerEvents.OnEnterDesignMode -= OnEnterDesignMode;
                        _debuggerEvents.OnExceptionThrown -= OnExceptionThrown;
                        _debuggerEvents.OnExceptionNotHandled -= OnExceptionNotHandled;
                        _debuggerEvents = null;
                    }
                    _dte = null;
                });
            }
            catch
            {
            }

            StateChanged = null;
            BreakModeEntered = null;
            ExceptionRaised = null;
        }
    }
}
