using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodeRadar.Models;

namespace CodeRadar.Services
{
    public interface IDebuggerService : IDisposable
    {
        DebuggerState CurrentState { get; }

        event EventHandler<DebuggerStateChangedEventArgs> StateChanged;

        event EventHandler BreakModeEntered;

        event EventHandler<ExceptionInfo> ExceptionRaised;

        Task<IReadOnlyList<ThreadInfo>> GetThreadsAsync(CancellationToken cancellationToken);

        Task<IReadOnlyList<StackFrameInfo>> GetCurrentStackAsync(CancellationToken cancellationToken);
    }
}
