using System;
using System.ComponentModel.Design;
using CodeRadar.Views;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace CodeRadar.Commands
{
    internal sealed class ShowCodeRadarWindowCommand
    {
        private readonly AsyncPackage _package;

        private ShowCodeRadarWindowCommand(AsyncPackage package)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
        }

        public static void Register(AsyncPackage package, OleMenuCommandService commandService)
        {
            if (package is null) throw new ArgumentNullException(nameof(package));
            if (commandService is null) throw new ArgumentNullException(nameof(commandService));

            var instance = new ShowCodeRadarWindowCommand(package);
            var id = new CommandID(PackageGuids.CommandSetGuid, PackageIds.ShowCodeRadarWindowCommandId);
            var menuItem = new OleMenuCommand(instance.Execute, id);
            commandService.AddCommand(menuItem);
        }

        private void Execute(object sender, EventArgs e)
        {
            _package.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await ShowToolWindowAsync();
                }
                catch (Exception ex)
                {
                    await LogAsync(ex);
                }
            });
        }

        private async Task ShowToolWindowAsync()
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();

            var window = await _package.ShowToolWindowAsync(
                typeof(CodeRadarToolWindow),
                id: 0,
                create: true,
                cancellationToken: _package.DisposalToken);

            if (window?.Frame is IVsWindowFrame frame)
            {
                ErrorHandler.ThrowOnFailure(frame.Show());
            }
        }

        private async Task LogAsync(Exception ex)
        {
            try
            {
                await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                var output = await _package.GetServiceAsync(typeof(SVsGeneralOutputWindowPane)) as IVsOutputWindowPane;
                output?.OutputStringThreadSafe($"[Code Radar] Failed to open tool window: {ex}\r\n");
            }
            catch
            {
            }
        }
    }
}
