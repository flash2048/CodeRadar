using System.Threading;
using System.Threading.Tasks;
using CodeRadar.Models;

namespace CodeRadar.Services
{
    public interface IExpressionEvaluatorService
    {
        Task<VariableNode> EvaluateAsync(string expression, int maxChildDepth, CancellationToken cancellationToken);

        Task<VariableNode> EvaluateSequenceAsync(string expression, int maxItems, CancellationToken cancellationToken);
    }
}
