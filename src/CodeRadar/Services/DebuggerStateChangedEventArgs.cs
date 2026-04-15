using System;

namespace CodeRadar.Services
{
    public enum DebuggerState
    {
        Design,
        Running,
        Break
    }

    public sealed class DebuggerStateChangedEventArgs : EventArgs
    {
        public DebuggerStateChangedEventArgs(DebuggerState newState, string reason)
        {
            NewState = newState;
            Reason = reason ?? string.Empty;
        }

        public DebuggerState NewState { get; }

        public string Reason { get; }
    }
}
