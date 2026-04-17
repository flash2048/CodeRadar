using System;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

namespace CodeRadar.Services
{
    internal sealed class ImageExtractor : IImageExtractor
    {
        // Limits are centralized in CodeRadarLimits.
        private readonly JoinableTaskFactory _jtf;

        public ImageExtractor(JoinableTaskFactory jtf)
        {
            _jtf = jtf ?? throw new ArgumentNullException(nameof(jtf));
        }

        public async Task<ImageExtractResult> TryExtractAsync(string expression, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return new ImageExtractResult { Error = "Empty expression." };

            await _jtf.SwitchToMainThreadAsync(cancellationToken);

            var dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;
            var debugger = dte?.Debugger as Debugger2;

            if (debugger == null || debugger.CurrentMode != dbgDebugMode.dbgBreakMode)
                return new ImageExtractResult { Error = "Debugger is not in break mode." };

            // Strategies tried in order:
            //   1. Direct byte array / span.
            //   2. Stream-like types with ToArray().
            //   3. MemoryStream.GetBuffer() for streams whose ToArray throws.
            //   4. Expression already yielding a Base64 string.
            var attempts = new[]
            {
                "System.Convert.ToBase64String(" + expression + ")",
                "System.Convert.ToBase64String((" + expression + ").ToArray())",
                "System.Convert.ToBase64String((" + expression + ").GetBuffer())",
                expression
            };

            string lastError = null;
            foreach (var attempt in attempts)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var expr = debugger.GetExpression(attempt, UseAutoExpandRules: true, Timeout: 2000);
                    if (expr == null || !expr.IsValidValue)
                    {
                        lastError = expr?.Value;
                        continue;
                    }

                    var value = expr.Value ?? string.Empty;

                    if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                        value = value.Substring(1, value.Length - 2);

                    if (value.Length < 10) continue;
                    if (value.Length > CodeRadarLimits.MaxBase64Chars)
                    {
                        lastError = $"Base64 payload too large ({value.Length:N0} chars, max {CodeRadarLimits.MaxBase64Chars:N0}).";
                        continue;
                    }

                    byte[] bytes;
                    try { bytes = Convert.FromBase64String(value); }
                    catch { continue; }

                    if (bytes.Length > CodeRadarLimits.MaxImageBytes)
                    {
                        lastError = $"Image payload too large ({bytes.Length:N0} bytes, max {CodeRadarLimits.MaxImageBytes:N0}).";
                        continue;
                    }

                    var format = DetectImageFormat(bytes);
                    if (format == null) continue;

                    return new ImageExtractResult
                    {
                        Success        = true,
                        ImageBytes     = bytes,
                        DetectedFormat = format
                    };
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                }
            }

            return new ImageExtractResult
            {
                Success = false,
                Error = string.IsNullOrEmpty(lastError)
                    ? "No image data found in expression."
                    : "Could not extract image: " + lastError
            };
        }

        private static string DetectImageFormat(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 4) return null;

            if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                return "PNG";

            if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
                return "JPEG";

            if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38)
                return "GIF";

            if (bytes[0] == 0x42 && bytes[1] == 0x4D) return "BMP";

            if (bytes.Length >= 12
                && bytes[0]  == 0x52 && bytes[1]  == 0x49 && bytes[2]  == 0x46 && bytes[3]  == 0x46
                && bytes[8]  == 0x57 && bytes[9]  == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
                return "WEBP";

            if ((bytes[0] == 0x49 && bytes[1] == 0x49 && bytes[2] == 0x2A && bytes[3] == 0x00)
             || (bytes[0] == 0x4D && bytes[1] == 0x4D && bytes[2] == 0x00 && bytes[3] == 0x2A))
                return "TIFF";

            if (bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0x01 && bytes[3] == 0x00)
                return "ICO";

            return null;
        }
    }
}
