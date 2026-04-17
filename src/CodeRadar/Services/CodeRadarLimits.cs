using System;

namespace CodeRadar.Services
{
    // Single, authoritative set of safety limits applied across the extension.
    // Kept in one place so feature behavior stays consistent when caps trigger,
    // and so a single review can understand what the extension will and will not
    // do under pressure.
    internal static class CodeRadarLimits
    {
        // ------- Evaluation depth -------

        // Refresh-time eagerness: watches that are currently collapsed in the UI
        // only evaluate the root (depth 0). Expanded watches go one level deep.
        public const int RefreshDepthCollapsed = 0;
        public const int RefreshDepthExpanded  = 1;

        // Lazy expansion fetches one more level when the user expands a node.
        public const int LazyExpandDepth = 1;

        // Explicit-action default depth (export / snapshot). User can request
        // deeper via the Object Viewer "Re-evaluate" button.
        public const int ExportDepth   = 1;
        public const int SnapshotDepth = 1;

        // Maximum depth the Object Viewer's Re-evaluate button will request.
        // A hard ceiling exists in ObjectViewerWindow to stop runaway depth.
        public const int MaxReEvaluateDepth = 10;

        // ------- Tree / collection shape -------

        // Max direct children materialised per node. Prevents enumerating huge
        // Dictionary<,> or array values through COM.
        public const int MaxChildrenPerNode = 200;

        // Max total nodes materialised in a single evaluation tree.
        public const int MaxTotalNodesPerEvaluate = 5000;

        // Max characters retained in a single value string (longer values are
        // truncated with an explicit suffix). Debuggers occasionally return
        // multi-MB strings for long serialized payloads.
        public const int MaxValueStringLength = 2048;

        // ------- Sequence previews -------

        // Default preview size for LINQ / IEnumerable materialisation.
        public const int SequencePreviewSize = 50;

        // Max real-count probe when computing the total element count of a
        // sequence (LINQ decomposer stage count). Capped to keep the probe fast.
        public const int MaxCountProbe = 10000;

        // ------- Image extraction -------

        // Cap on decoded image payload (protects the IDE from OOM on a bad watch).
        public const int MaxImageBytes = 32 * 1024 * 1024;

        // Cap on the Base64 string we pull from the debugger before decoding.
        public const int MaxBase64Chars = 48 * 1024 * 1024;

        // ------- Timeouts -------

        // Per-GetExpression call (the one the VS debugger honours natively).
        public const int DefaultExprTimeoutMs  = 600;
        public const int SequenceExprTimeoutMs = 2500;

        // Overall per-action budgets. Each feature owns its own CancellationTokenSource
        // tied to one of these. They define the maximum user-visible stall per action.
        public static readonly TimeSpan OverallEvalBudget     = TimeSpan.FromSeconds(3) + TimeSpan.FromSeconds(2);
        public static readonly TimeSpan SequenceEvalBudget    = TimeSpan.FromSeconds(6);
        public static readonly TimeSpan CountProbeBudget      = TimeSpan.FromSeconds(4);
        public static readonly TimeSpan PerWatchBudget        = TimeSpan.FromSeconds(4);
        public static readonly TimeSpan RefreshBatchBudget    = TimeSpan.FromSeconds(12);
        public static readonly TimeSpan ExportBudget          = TimeSpan.FromSeconds(8);
        public static readonly TimeSpan SnapshotBudget        = TimeSpan.FromSeconds(8);
        public static readonly TimeSpan ImageExtractBudget    = TimeSpan.FromSeconds(8);
        public static readonly TimeSpan LinqDecomposeBudget   = TimeSpan.FromSeconds(20);
        public static readonly TimeSpan LazyExpandBudget      = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan InlineEvalBudget      = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan TogglePreviewBudget   = TimeSpan.FromSeconds(6);
        public static readonly TimeSpan ReEvaluateBudget      = TimeSpan.FromSeconds(15);

        // ------- User-facing status text -------

        public const string StatusTruncated     = "(truncated - too many items to show)";
        public const string StatusBudgetReached = "(node budget reached - right-click 'Re-evaluate' for deeper view)";
        public const string StatusNotLoaded     = "[not loaded - expand to inspect]";
        public const string StatusTimedOut      = "<evaluation timed out>";
        public const string StatusTooLarge      = "<value too large>";
    }
}
