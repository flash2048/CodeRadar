using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using CodeRadar.Commands;
using CodeRadar.Services;
using CodeRadar.Views;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace CodeRadar
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("Code Radar", "A modern debugging companion for Visual Studio.", "1.0.0")]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(CodeRadarToolWindow), Style = VsDockStyle.Tabbed, Window = EnvDTE.Constants.vsWindowKindOutput)]
    [ProvideToolWindowVisibility(typeof(CodeRadarToolWindow), VSConstants.UICONTEXT.Debugging_string)]
    [Guid(PackageGuids.PackageGuidString)]
    public sealed class CodeRadarPackage : AsyncPackage
    {
        private DebuggerService _debuggerService;
        private ExpressionEvaluatorService _expressionEvaluator;
        private ImageExtractor _imageExtractor;

        internal IDebuggerService DebuggerService => _debuggerService;

        internal IExpressionEvaluatorService ExpressionEvaluator => _expressionEvaluator;

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await base.InitializeAsync(cancellationToken, progress);

            _debuggerService = new DebuggerService(this, JoinableTaskFactory);
            _expressionEvaluator = new ExpressionEvaluatorService(JoinableTaskFactory);
            _imageExtractor = new ImageExtractor(JoinableTaskFactory);

            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            if (await GetServiceAsync(typeof(IMenuCommandService)).ConfigureAwait(true) is OleMenuCommandService mcs)
            {
                ShowCodeRadarWindowCommand.Register(this, mcs);
                EditorContextCommands.Register(this, mcs);
            }

            await _debuggerService.InitializeAsync(cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    _debuggerService?.Dispose();
                }
                catch
                {
                }
                _debuggerService = null;
                _expressionEvaluator = null;
                _imageExtractor = null;
            }

            base.Dispose(disposing);
        }

        internal TService GetCodeRadarService<TService>() where TService : class
        {
            if (typeof(TService) == typeof(IDebuggerService))
            {
                return _debuggerService as TService;
            }
            if (typeof(TService) == typeof(IExpressionEvaluatorService))
            {
                return _expressionEvaluator as TService;
            }
            if (typeof(TService) == typeof(IImageExtractor))
            {
                return _imageExtractor as TService;
            }
            return null;
        }
    }
}
