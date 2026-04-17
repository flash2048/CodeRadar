using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using CodeRadar.Commands;
using CodeRadar.Services;
using CodeRadar.Views;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace CodeRadar
{
    // ProvideMenuResource version must be bumped whenever the VSCT placement
    // changes so VS re-merges menus instead of reusing its cached layout. This
    // is the common fix for "Code Radar is installed but I don't see it under
    // View -> Other Windows" on VS 2026 and after upgrades.
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("Code Radar", "A debugging companion for Visual Studio with lazy large-object support, LINQ decomposition, snapshots, and image preview.", "1.0.0")]
    [ProvideMenuResource("Menus.ctmenu", 5)]
    [ProvideToolWindow(typeof(CodeRadarToolWindow), Style = VsDockStyle.Tabbed, Window = EnvDTE.Constants.vsWindowKindOutput)]
    // Force the package to load in the background as soon as a shell exists, so
    // that command handler registration and activity-log diagnostics run early
    // enough to show problems in %AppData%\...\ActivityLog.xml even if the user
    // never clicks the menu entry.
    [ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
    [Guid(PackageGuids.PackageGuidString)]
    public sealed class CodeRadarPackage : AsyncPackage
    {
        private const string ActivityLogSource = "Code Radar";

        private DebuggerService _debuggerService;
        private ExpressionEvaluatorService _expressionEvaluator;
        private ImageExtractor _imageExtractor;

        internal IDebuggerService DebuggerService => _debuggerService;

        internal IExpressionEvaluatorService ExpressionEvaluator => _expressionEvaluator;

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            try
            {
                await base.InitializeAsync(cancellationToken, progress);

                _debuggerService = new DebuggerService(this, JoinableTaskFactory);
                _expressionEvaluator = new ExpressionEvaluatorService(JoinableTaskFactory);
                _imageExtractor = new ImageExtractor(JoinableTaskFactory);

                await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

                try
                {
                    if (await GetServiceAsync(typeof(IMenuCommandService)).ConfigureAwait(true) is OleMenuCommandService mcs)
                    {
                        ShowCodeRadarWindowCommand.Register(this, mcs);
                        EditorContextCommands.Register(this, mcs);
                        await LogInfoAsync("Commands registered (ShowCodeRadarWindow + editor context commands).");
                    }
                    else
                    {
                        await LogErrorAsync("Could not obtain IMenuCommandService - Code Radar menu entries will not appear.");
                    }
                }
                catch (Exception ex)
                {
                    await LogErrorAsync("Command registration failed: " + ex);
                }

                try
                {
                    await _debuggerService.InitializeAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    await LogErrorAsync("DebuggerService initialization failed: " + ex);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // Always attempt to record load failures so users can diagnose why the package
                // didn't surface in 'View -> Other Windows' / 'Extensions'.
                await LogErrorAsync("Package InitializeAsync failed: " + ex);
                throw;
            }
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

        internal async Task LogInfoAsync(string message)
            => await WriteToActivityLogAsync(__ACTIVITYLOG_ENTRYTYPE.ALE_INFORMATION, message);

        internal async Task LogWarningAsync(string message)
            => await WriteToActivityLogAsync(__ACTIVITYLOG_ENTRYTYPE.ALE_WARNING, message);

        internal async Task LogErrorAsync(string message)
            => await WriteToActivityLogAsync(__ACTIVITYLOG_ENTRYTYPE.ALE_ERROR, message);

        private async Task WriteToActivityLogAsync(__ACTIVITYLOG_ENTRYTYPE entryType, string message)
        {
            try
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync();
                if (await GetServiceAsync(typeof(SVsActivityLog)) is IVsActivityLog log)
                {
                    log.LogEntry((uint)entryType, ActivityLogSource, message ?? string.Empty);
                }
            }
            catch
            {
                // Diagnostics are best-effort; never let logging crash the shell.
            }
        }
    }
}
