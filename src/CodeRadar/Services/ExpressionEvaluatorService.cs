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
        // Cap children per node to avoid enumerating huge collections through COM.
        private const int MaxChildrenPerNode = 200;

        // Per-GetExpression timeout in milliseconds. Keep low so a single stuck
        // expression doesn't freeze the shell for long.
        private const int DefaultExprTimeoutMs = 600;
        private const int SequenceExprTimeoutMs = 2500;

        private readonly JoinableTaskFactory _jtf;

        public ExpressionEvaluatorService(JoinableTaskFactory joinableTaskFactory)
        {
            _jtf = joinableTaskFactory ?? throw new ArgumentNullException(nameof(joinableTaskFactory));
        }

        public async Task<VariableNode> EvaluateAsync(string expression, int maxChildDepth, CancellationToken cancellationToken)
        {
            // Overall timeout: 3 seconds base + 2 seconds per depth level.
            int overallMs = 3000 + maxChildDepth * 2000;
            return await WithOverallTimeout(
                () => EvaluateCoreAsync(expression, expression, maxChildDepth, DefaultExprTimeoutMs, cancellationToken),
                expression, overallMs, cancellationToken);
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
                () => EvaluateCoreAsync(displayName: expression, expression: wrapped, maxChildDepth: 1, SequenceExprTimeoutMs, cancellationToken),
                expression, 6000, cancellationToken);
        }

        private async Task<VariableNode> WithOverallTimeout(
            Func<Task<VariableNode>> work, string displayName, int timeoutMs, CancellationToken outer)
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(outer))
            {
                linked.CancelAfter(timeoutMs);
                try
                {
                    return await work();
                }
                catch (OperationCanceledException) when (!outer.IsCancellationRequested)
                {
                    // Our internal timeout fired, not the caller's token.
                    return new VariableNode(displayName ?? string.Empty,
                        "<evaluation timed out>", string.Empty,
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

            return BuildNode(displayName, expr, remainingDepth: Math.Max(0, maxChildDepth), cancellationToken);
        }

        private static VariableNode BuildNode(string name, Expression expr, int remainingDepth, CancellationToken cancellationToken)
        {
            if (expr == null)
            {
                return new VariableNode(name, "<null expression>", string.Empty,
                    isValid: false, isNull: false, children: Array.Empty<VariableNode>());
            }

            string value = SafeGet(() => expr.Value);
            string type = SafeGet(() => expr.Type);
            bool isValid = SafeGet(() => expr.IsValidValue, false);
            bool isNull = isValid && IsNullValue(value);

            IReadOnlyList<VariableNode> children = Array.Empty<VariableNode>();
            if (remainingDepth > 0 && isValid && !isNull && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var members = expr.DataMembers;
                    int memberCount = members?.Count ?? 0;
                    if (memberCount > 0)
                    {
                        int take = memberCount > MaxChildrenPerNode ? MaxChildrenPerNode : memberCount;
                        var list = new List<VariableNode>(Math.Min(take, 64));
                        int i = 0;
                        foreach (Expression child in members)
                        {
                            if (i >= take) break;
                            if (cancellationToken.IsCancellationRequested) break;
                            try
                            {
                                list.Add(BuildNode(child.Name ?? string.Empty, child, remainingDepth - 1, cancellationToken));
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
                        children = list;
                    }
                }
                catch
                {
                }
            }

            return new VariableNode(name, value, type, isValid, isNull, children);
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
