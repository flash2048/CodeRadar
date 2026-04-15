using System;
using System.Collections.Generic;

namespace CodeRadar.Models
{
    public sealed class ExceptionInfo
    {
        public ExceptionInfo(
            string exceptionType,
            string message,
            string code,
            bool isHandled,
            IReadOnlyList<StackFrameInfo> stackFrames,
            int relevantFrameIndex,
            DateTime observedAtUtc)
        {
            ExceptionType = exceptionType ?? string.Empty;
            Message = message ?? string.Empty;
            Code = code ?? string.Empty;
            IsHandled = isHandled;
            StackFrames = stackFrames ?? Array.Empty<StackFrameInfo>();
            RelevantFrameIndex = relevantFrameIndex;
            ObservedAtUtc = observedAtUtc;
        }

        public string ExceptionType { get; }

        public string Message { get; }

        public string Code { get; }

        public bool IsHandled { get; }

        public IReadOnlyList<StackFrameInfo> StackFrames { get; }

        public int RelevantFrameIndex { get; }

        public DateTime ObservedAtUtc { get; }
    }
}
