using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using CodeRadar.Services;
using CodeRadar.ViewModels;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CodeRadar.Views
{
    [Guid(PackageGuids.ToolWindowGuidString)]
    public sealed class CodeRadarToolWindow : ToolWindowPane
    {
        private CodeRadarViewModel _viewModel;

        internal CodeRadarViewModel ViewModel => _viewModel;

        public CodeRadarToolWindow() : base(null)
        {
            Caption = "Code Radar";
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            CodeRadarPackage package = null;
            try
            {
                package = (CodeRadarPackage)Package;
                var debugger = package.GetCodeRadarService<IDebuggerService>();
                var evaluator = package.GetCodeRadarService<IExpressionEvaluatorService>();
                var imageExtractor = package.GetCodeRadarService<IImageExtractor>();

                _viewModel = new CodeRadarViewModel(debugger, evaluator, imageExtractor, package.JoinableTaskFactory);
                _viewModel.ExportRequested    += OnExportRequested;
                _viewModel.HistoryRequested   += OnHistoryRequested;
                _viewModel.CompareRequested   += OnCompareRequested;
                _viewModel.DecomposeRequested += OnDecomposeRequested;
                _viewModel.ImageRequested     += OnImageRequested;

                var control = new CodeRadarControl { DataContext = _viewModel };
                Content = control;
            }
            catch (Exception ex)
            {
                // Show a placeholder and record the failure so users can see why the tool
                // window didn't render (check the ActivityLog / 'ShowActivityLog' command).
                Content = new System.Windows.Controls.TextBlock
                {
                    Text = "Code Radar failed to initialise.\n\n" + ex,
                    TextWrapping = System.Windows.TextWrapping.Wrap,
                    Margin = new System.Windows.Thickness(12)
                };
                if (package != null)
                {
                    package.JoinableTaskFactory.RunAsync(async () =>
                        await package.LogErrorAsync("CodeRadarToolWindow.OnCreate failed: " + ex));
                }
            }
        }

        protected override void OnClose()
        {
            try
            {
                if (_viewModel != null)
                {
                    _viewModel.ExportRequested    -= OnExportRequested;
                    _viewModel.HistoryRequested   -= OnHistoryRequested;
                    _viewModel.CompareRequested   -= OnCompareRequested;
                    _viewModel.DecomposeRequested -= OnDecomposeRequested;
                    _viewModel.ImageRequested     -= OnImageRequested;
                    _viewModel.Dispose();
                }
            }
            catch
            {
            }
            _viewModel = null;
            base.OnClose();
        }

        private void OnHistoryRequested(object sender, HistoryRequestedEventArgs e)
            => ShowDialog(() => new WatchHistoryWindow(e.Watch));

        private void OnCompareRequested(object sender, CompareRequestedEventArgs e)
            => ShowDialog(() => new CompareSnapshotsWindow(e.Watch));

        private void OnDecomposeRequested(object sender, DecomposeRequestedEventArgs e)
        {
            var package = (CodeRadarPackage)Package;
            var evaluator = package.GetCodeRadarService<IExpressionEvaluatorService>();
            var imageExtractor = package.GetCodeRadarService<IImageExtractor>();

            Func<string, int, System.Threading.CancellationToken, System.Threading.Tasks.Task<Models.VariableNode>> evalFn = null;
            if (evaluator != null)
                evalFn = async (expr, depth, ct) => await evaluator.EvaluateAsync(expr, depth, ct);

            Func<string, System.Threading.CancellationToken, System.Threading.Tasks.Task<ImageExtractResult>> imgFn = null;
            if (imageExtractor != null)
                imgFn = async (expr, ct) => await imageExtractor.TryExtractAsync(expr, ct);

            ShowDialog(() => new LinqDecomposerWindow(e.OriginalExpression, e.Steps, evalFn, imgFn));
        }

        private void OnImageRequested(object sender, ImageRequestedEventArgs e)
        {
            if (e.Result == null || !e.Result.Success)
            {
                MessageBox.Show(
                    "No image data could be decoded from this expression.\n\n"
                    + (e.Result?.Error ?? string.Empty),
                    "Code Radar - Show image",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            ShowDialog(() => new ImageViewerWindow(e.Result.ImageBytes, e.Result.DetectedFormat, e.Expression));
        }

        private void ShowDialog(Func<Window> factory)
        {
            try
            {
                var dlg = factory();
                var owner = TryGetMainWindow();
                if (owner != IntPtr.Zero)
                {
                    try { new WindowInteropHelper(dlg).Owner = owner; }
                    catch { }
                }
                else
                {
                    dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }
                dlg.Show();
                dlg.Activate();
                dlg.Topmost = true;
                dlg.Topmost = false;
            }
            catch (Exception ex)
            {
                try
                {
                    MessageBox.Show("Code Radar dialog failed to open.\n\n" + ex,
                        "Code Radar", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch { }
            }
        }

        private void OnExportRequested(object sender, ExportRequestedEventArgs e)
            => ShowDialog(() => new ObjectViewerWindow(e.Node, e.Caption, e.ReEvaluator));

        private static IntPtr TryGetMainWindow()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var uiShell = Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(SVsUIShell)) as IVsUIShell;
                if (uiShell != null && uiShell.GetDialogOwnerHwnd(out var hwnd) == 0)
                    return hwnd;
            }
            catch
            {
            }
            return IntPtr.Zero;
        }
    }
}
