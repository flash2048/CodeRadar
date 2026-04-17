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
    internal sealed class ExpressionEvaluatorService : IExpressionEvaluatorService
    {
        private readonly JoinableTaskFactory _jtf;

        public ExpressionEvaluatorService(JoinableTaskFactory joinableTaskFactory)
        {
            _jtf = joinableTaskFactory ?? throw new ArgumentNullException(nameof(joinableTaskFactory));
        }

        public async Task<VariableNode> EvaluateAsync(string expression, int maxChildDepth, CancellationToken cancellationToken)
        {
            // Overall budget scales with requested depth so deeper evaluations
            // are still bounded but get a bit more headroom.
            var overall = CodeRadarLimits.OverallEvalBudget + TimeSpan.FromSeconds(Math.Max(0, maxChildDepth - 1) * 2);
            return await WithOverallTimeout(
                (ct) => EvaluateCoreAsync(expression, expression, maxChildDepth, CodeRadarLimits.DefaultExprTimeoutMs, ct),
                expression, overall, cancellationToken);
        }

        public async Task<(int? count, bool truncated)> TryCountAsync(string expression, int maxCount, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(expression)) return (null, false);
            int cap = Math.Max(1, maxCount);

            string wrapped = $"System.Linq.Enumerable.Count(System.Linq.Enumerable.Take({expression}, {cap + 1}))";

            var node = await WithOverallTimeout(
                (ct) => EvaluateCoreAsync(displayName: expression, expression: wrapped, maxChildDepth: 0, CodeRadarLimits.SequenceExprTimeoutMs, ct),
                expression, CodeRadarLimits.CountProbeBudget, cancellationToken);

            if (!node.IsValid) return (null, false);
            if (int.TryParse(node.Value, out int n))
            {
                if (n > cap) return (cap, true);
                return (n, false);
            }
            return (null, false);
        }

        public async Task<VariableNode> EvaluateSequenceAsync(string expression, int maxItems, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(expression))
            {
                return new VariableNode(expression ?? string.Empty, "<empty>", string.Empty,
                    isValid: false, isNull: false, children: Array.Empty<VariableNode>());
            }

            var take = Math.Max(1, maxItems);
            var wrapped = $"System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Take({expression}, {take}))";

            return await WithOverallTimeout(
                (ct) => EvaluateCoreAsync(displayName: expression, expression: wrapped, maxChildDepth: 1, CodeRadarLimits.SequenceExprTimeoutMs, ct),
                expression, CodeRadarLimits.SequenceEvalBudget, cancellationToken);
        }

        // The timeout path threads the linked token into the work delegate so that
        // inner GetExpression / DataMembers enumeration actually observes cancellation.
        private async Task<VariableNode> WithOverallTimeout(
            Func<CancellationToken, Task<VariableNode>> work, string displayName, TimeSpan timeout, CancellationToken outer)
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(outer))
            {
                linked.CancelAfter(timeout);
                try
                {
                    return await work(linked.Token);
                }
                catch (OperationCanceledException) when (!outer.IsCancellationRequested)
                {
                    return new VariableNode(displayName ?? string.Empty,
                        CodeRadarLimits.StatusTimedOut, string.Empty,
                        isValid: false, isNull: false, children: Array.Empty<VariableNode>());
                }
            }
        }

        private async Task<VariableNode> EvaluateCoreAsync(string displayName, string expression, int maxChildDepth, int timeoutMs, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(expression))
            {
                return new VariableNode(displayName ?? string.Empty, "<empty>", string.Empty,
                    isValid: false, isNull: false, children: Array.Empty<VariableNode>());
            }

            cancellationToken.ThrowIfCancellationRequested();

            await _jtf.SwitchToMainThreadAsync(cancellationToken);

            var dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;
            var debugger = dte?.Debugger as Debugger2;

            if (debugger == null || debugger.CurrentMode != dbgDebugMode.dbgBreakMode)
            {
                return new VariableNode(displayName, "<not in break mode>", string.Empty,
                    isValid: false, isNull: false, children: Array.Empty<VariableNode>());
            }

            Expression expr;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                expr = debugger.GetExpression(expression, UseAutoExpandRules: true, Timeout: timeoutMs);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return new VariableNode(displayName, $"<eval failed: {ex.Message}>", string.Empty,
                    isValid: false, isNull: false, children: Array.Empty<VariableNode>());
            }

            var budget = new NodeBudget(CodeRadarLimits.MaxTotalNodesPerEvaluate);
            return BuildNode(displayName, expr, remainingDepth: Math.Max(0, maxChildDepth), cancellationToken, budget);
        }

        // Mutable counter shared across one evaluation tree so we don't materialise
        // arbitrary numbers of total nodes even if individual nodes stay under the
        // per-node cap.
        private sealed class NodeBudget
        {
            private int _remaining;
            public NodeBudget(int initial) { _remaining = initial; }
            public bool Take() { if (_remaining <= 0) return false; _remaining--; return true; }
            public bool Exhausted => _remaining <= 0;
        }

        private static VariableNode BuildNode(string name, Expression expr, int remainingDepth,
            CancellationToken cancellationToken, NodeBudget budget)
        {
            if (expr == null)
            {
                return new VariableNode(name, "<null expression>", string.Empty,
                    isValid: false, isNull: false, children: Array.Empty<VariableNode>());
            }

            budget.Take();

            string value = TruncateValue(SafeGet(() => expr.Value));
            string type  = SafeGet(() => expr.Type);
            bool isValid = SafeGet(() => expr.IsValidValue, false);
            bool isNull  = isValid && IsNullValue(value);

            IReadOnlyList<VariableNode> children = Array.Empty<VariableNode>();
            if (remainingDepth > 0 && isValid && !isNull
                && !cancellationToken.IsCancellationRequested
                && !budget.Exhausted)
            {
                try
                {
                    var members = expr.DataMembers;
                    int memberCount = members?.Count ?? 0;
                    if (memberCount > 0)
                    {
                        int take = memberCount > CodeRadarLimits.MaxChildrenPerNode
                            ? CodeRadarLimits.MaxChildrenPerNode
                            : memberCount;
                        var list = new List<VariableNode>(Math.Min(take, 64));
                        int i = 0;
                        foreach (Expression child in members)
                        {
                            if (i >= take) break;
                            if (cancellationToken.IsCancellationRequested) break;
                            if (budget.Exhausted) break;
                            try
                            {
                                list.Add(BuildNode(child.Name ?? string.Empty, child, remainingDepth - 1, cancellationToken, budget));
                            }
                            catch
                            {
                            }
                            i++;
                        }
                        if (memberCount > take && !cancellationToken.IsCancellationRequested)
                        {
                            list.Add(new VariableNode(
                                $"... {memberCount - take} more",
                                $"(truncated, total {memberCount})",
                                string.Empty,
                                isValid: false, isNull: false,
                                children: Array.Empty<VariableNode>()));
                        }
                        else if (budget.Exhausted && i < memberCount)
                        {
                            list.Add(new VariableNode(
                                "... more",
                                CodeRadarLimits.StatusBudgetReached,
                                string.Empty,
                                isValid: false, isNull: false,
                                children: Array.Empty<VariableNode>()));
                        }
                        children = list;
                    }
                }
                catch
                {
                }
            }

            return new VariableNode(name, value, type, isValid, isNull, children);
        }

        private static string TruncateValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
            if (value.Length <= CodeRadarLimits.MaxValueStringLength) return value;
            return value.Substring(0, CodeRadarLimits.MaxValueStringLength)
                 + "... (truncated " + (value.Length - CodeRadarLimits.MaxValueStringLength) + " chars)";
        }

        private static bool IsNullValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            return value == "null" || value == "<null>" || value == "Nothing";
        }

        private static string SafeGet(Func<string> getter)
        {
            try { return getter() ?? string.Empty; } catch { return string.Empty; }
        }

        private static bool SafeGet(Func<bool> getter, bool fallback)
        {
            try { return getter(); } catch { return fallback; }
        }
    }
}
