using System.Threading;
using System.Threading.Tasks;
using CodeRadar.Models;

namespace CodeRadar.Services
{
    public interface IExpressionEvaluatorService
    {
        Task<VariableNode> EvaluateAsync(string expression, int maxChildDepth, CancellationToken cancellationToken);

        Task<VariableNode> EvaluateSequenceAsync(string expression, int maxItems, CancellationToken cancellationToken);

        // Cheap count probe: returns the element count of an enumerable expression,
        // capped at <paramref name="maxCount"/> for safety. Returns null if not enumerable
        // or evaluation failed. If the real sequence has at least <paramref name="maxCount"/>
        // elements, <paramref name="truncated"/> is set and the returned value is maxCount.
        Task<(int? count, bool truncated)> TryCountAsync(string expression, int maxCount, CancellationToken cancellationToken);
    }
}
