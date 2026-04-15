using System;
using System.Windows;
using System.Windows.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CodeRadar.Views
{
    internal static class DialogPresenter
    {
        public static void Show(Func<Window> factory)
        {
            if (factory == null) return;

            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var dlg = factory();
                if (dlg == null) return;

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
                catch
                {
                }
            }
        }

        private static IntPtr TryGetMainWindow()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var uiShell = Package.GetGlobalService(typeof(SVsUIShell)) as IVsUIShell;
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
