using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Threading;
using System.Threading.Tasks;
using CodeRadar.Models;
using CodeRadar.Services;
using CodeRadar.Views;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Task = System.Threading.Tasks.Task;

namespace CodeRadar.Commands
{
    internal sealed class EditorContextCommands
    {
        private readonly CodeRadarPackage _package;

        private EditorContextCommands(CodeRadarPackage package)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
        }

        public static void Register(CodeRadarPackage package, OleMenuCommandService commandService)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            if (commandService == null) throw new ArgumentNullException(nameof(commandService));

            var instance = new EditorContextCommands(package);

            AddCommand(commandService, PackageIds.EditorAddToWatchesCommandId,
                       instance.OnAddToWatches, instance.QueryRequiresEditor);

            AddCommand(commandService, PackageIds.EditorExportObjectCommandId,
                       instance.OnExportObject, instance.QueryRequiresBreakMode);

            AddCommand(commandService, PackageIds.EditorDecomposeLinqCommandId,
                       instance.OnDecomposeLinq, instance.QueryRequiresBreakMode);

            AddCommand(commandService, PackageIds.EditorShowImageCommandId,
                       instance.OnShowImage, instance.QueryRequiresBreakMode);

            AddCommand(commandService, PackageIds.EditorShowWindowCommandId,
                       instance.OnShowWindow, queryStatus: null);
        }

        private static void AddCommand(OleMenuCommandService mcs, int id,
                                       EventHandler exec, EventHandler queryStatus)
        {
            var cmdId = new CommandID(PackageGuids.CommandSetGuid, id);
            var cmd = new OleMenuCommand(exec, cmdId) { Visible = true, Enabled = true };
            if (queryStatus != null) cmd.BeforeQueryStatus += queryStatus;
            mcs.AddCommand(cmd);
        }

        private void QueryRequiresEditor(object sender, EventArgs e)
        {
            var cmd = (OleMenuCommand)sender;
            cmd.Visible = true;
            cmd.Enabled = HasActiveTextDocument();
        }

        private void QueryRequiresBreakMode(object sender, EventArgs e)
        {
            var cmd = (OleMenuCommand)sender;
            cmd.Visible = true;
            cmd.Enabled = HasActiveTextDocument()
                       && _package.DebuggerService?.CurrentState == DebuggerState.Break;
        }

        private bool HasActiveTextDocument()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;
                return dte?.ActiveDocument?.Object("TextDocument") is TextDocument;
            }
            catch
            {
                return false;
            }
        }

        private void OnAddToWatches(object sender, EventArgs e)
        {
            RunOnUiAsync(async () =>
            {
                var expr = ReadExpressionAtCursor();
                if (string.IsNullOrWhiteSpace(expr)) return;

                var tw = await OpenToolWindowAsync();
                tw?.ViewModel?.AddWatchFromExternal(expr);
            });
        }

        private void OnExportObject(object sender, EventArgs e)
        {
            RunOnUiAsync(async () =>
            {
                var expr = ReadExpressionAtCursor();
                if (string.IsNullOrWhiteSpace(expr)) return;
                if (_package.DebuggerService?.CurrentState != DebuggerState.Break) return;

                var evaluator = _package.GetCodeRadarService<IExpressionEvaluatorService>();
                if (evaluator == null) return;

                VariableNode node;
                try
                {
                    node = await evaluator.EvaluateAsync(expr, maxChildDepth: 5, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    node = new VariableNode(expr, "<eval failed: " + ex.Message + ">",
                        string.Empty, isValid: false, isNull: false,
                        children: Array.Empty<VariableNode>());
                }

                await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                DialogPresenter.Show(() => new ObjectViewerWindow(node, expr));
            });
        }

        private void OnDecomposeLinq(object sender, EventArgs e)
        {
            RunOnUiAsync(async () =>
            {
                var expr = ReadExpressionAtCursor();
                if (string.IsNullOrWhiteSpace(expr)) return;
                if (_package.DebuggerService?.CurrentState != DebuggerState.Break) return;

                var evaluator = _package.GetCodeRadarService<IExpressionEvaluatorService>();
                if (evaluator == null) return;

                var segments = LinqChainAnalyzer.Parse(expr);
                if (segments.Count == 0) return;

                var results = new List<LinqStepResult>(segments.Count);
                foreach (var segment in segments)
                {
                    LinqStepResult step;
                    try
                    {
                        var node = await evaluator.EvaluateSequenceAsync(segment.CumulativeExpression, maxItems: 50, CancellationToken.None);
                        if (!node.IsValid)
                            node = await evaluator.EvaluateAsync(segment.CumulativeExpression, maxChildDepth: 1, CancellationToken.None);

                        int? count = node.IsValid ? (int?)node.Children.Count : null;
                        bool truncated = node.Children.Count >= 50;
                        step = new LinqStepResult(segment.Label, segment.CumulativeExpression, count,
                            truncated, node.Children, node.IsValid ? string.Empty : node.Value);
                    }
                    catch (Exception ex)
                    {
                        step = new LinqStepResult(segment.Label, segment.CumulativeExpression, null,
                            false, Array.Empty<VariableNode>(), ex.Message);
                    }
                    results.Add(step);
                }

                await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                DialogPresenter.Show(() => new LinqDecomposerWindow(expr, results));
            });
        }

        private void OnShowImage(object sender, EventArgs e)
        {
            RunOnUiAsync(async () =>
            {
                var expr = ReadExpressionAtCursor();
                if (string.IsNullOrWhiteSpace(expr)) return;
                if (_package.DebuggerService?.CurrentState != DebuggerState.Break) return;

                var extractor = _package.GetCodeRadarService<IImageExtractor>();
                if (extractor == null) return;

                ImageExtractResult result;
                try
                {
                    result = await extractor.TryExtractAsync(expr, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    result = new ImageExtractResult { Success = false, Error = ex.Message };
                }

                await _package.JoinableTaskFactory.SwitchToMainThreadAsync();

                if (result == null || !result.Success)
                {
                    System.Windows.MessageBox.Show(
                        "No image data could be decoded from this expression.\n\n"
                        + "Supported shapes: byte[], MemoryStream / Stream with ToArray(), "
                        + "List<byte>, or a Base64 string whose bytes match a known image magic header "
                        + "(PNG, JPEG, GIF, BMP, WEBP, TIFF, ICO).\n\n"
                        + (result?.Error ?? string.Empty),
                        "Code Radar - Show image",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                    return;
                }

                DialogPresenter.Show(() =>
                    new ImageViewerWindow(result.ImageBytes, result.DetectedFormat, expr));
            });
        }

        private void OnShowWindow(object sender, EventArgs e)
        {
            RunOnUiAsync(async () => await OpenToolWindowAsync());
        }

        private void RunOnUiAsync(Func<Task> work)
        {
            _package.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                    await work();
                }
                catch
                {
                }
            }).Task.Forget();
        }

        private async Task<CodeRadarToolWindow> OpenToolWindowAsync()
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
            var window = await _package.ShowToolWindowAsync(
                typeof(CodeRadarToolWindow), id: 0, create: true,
                cancellationToken: _package.DisposalToken);

            if (window?.Frame is IVsWindowFrame frame)
                ErrorHandler.ThrowOnFailure(frame.Show());

            return window as CodeRadarToolWindow;
        }

        private string ReadExpressionAtCursor()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;
                var textDoc = dte?.ActiveDocument?.Object("TextDocument") as TextDocument;
                var sel = textDoc?.Selection;
                if (sel == null) return null;

                if (!string.IsNullOrWhiteSpace(sel.Text))
                    return sel.Text.Trim();

                var caret = sel.ActivePoint;
                var lineStart = caret.CreateEditPoint();
                lineStart.StartOfLine();
                var lineEnd = caret.CreateEditPoint();
                lineEnd.EndOfLine();
                var lineText = lineStart.GetText(lineEnd) ?? string.Empty;
                if (lineText.Length == 0) return null;

                int col = Math.Max(0, Math.Min(lineText.Length, caret.LineCharOffset - 1));

                int start = col;
                while (start > 0 && IsIdentifierOrDot(lineText[start - 1])) start--;

                int end = col;
                while (end < lineText.Length && IsIdentifierOrDot(lineText[end])) end++;

                if (end <= start) return null;
                var token = lineText.Substring(start, end - start).Trim();
                return string.IsNullOrEmpty(token) ? null : token;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsIdentifierOrDot(char c)
            => char.IsLetterOrDigit(c) || c == '_' || c == '.';
    }
}
