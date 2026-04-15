using System.Threading;
using System.Threading.Tasks;

namespace CodeRadar.Services
{
    public interface IImageExtractor
    {
        Task<ImageExtractResult> TryExtractAsync(string expression, CancellationToken cancellationToken);
    }

    public sealed class ImageExtractResult
    {
        public bool Success { get; set; }
        public byte[] ImageBytes { get; set; }
        public string DetectedFormat { get; set; }
        public string Error { get; set; }
    }
}
