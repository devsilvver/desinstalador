using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace DesinstaladorCompleto
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Theme.Initialize();

#if PREVIEW
            // Compilação de pré-visualização (build.bat preview): sem elevação, só para conferir a interface.
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length >= 3 && args[1] == "--scan")
            {
                InstalledProgram target = null;
                foreach (InstalledProgram p in ProgramScanner.GetWin32Programs())
                    if (p.DisplayName.IndexOf(args[2], StringComparison.OrdinalIgnoreCase) >= 0) { target = p; break; }
                if (target != null) Application.Run(new UninstallForm(target, true, new UninstallOptions(), IconUtil.GetIcon(target.DisplayIcon, Theme.S(56))));
                return;
            }
            if (args.Length >= 3 && args[1] == "--confirm")
            {
                foreach (InstalledProgram p in ProgramScanner.GetWin32Programs())
                    if (p.DisplayName.IndexOf(args[2], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Application.Run(new ConfirmDialog(p, args.Length > 3, IconUtil.GetIcon(p.DisplayIcon, Theme.S(56))));
                        break;
                    }
                return;
            }
            Application.Run(new MainForm { PreviewSelect = args.Length >= 3 && args[1] == "--select" ? args[2] : null });
            return;
#endif

            if (!IsAdministrator())
            {
                // O manifesto já pede elevação; isto é só uma garantia caso o .exe seja iniciado de outra forma.
                try
                {
                    Process.Start(new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = true, Verb = "runas" });
                }
                catch
                {
                    MessageBox.Show("O Desinstalador Completo precisa ser executado como administrador para remover programas e limpar o registro.",
                        "Desinstalador Completo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }

            Application.ThreadException += (s, e) => ShowError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowError(e.ExceptionObject as Exception);
            Application.Run(new MainForm());
        }

        private static System.Drawing.Icon appIcon;

        /// <summary>Ícone do aplicativo (embutido no .exe) para usar nas janelas.</summary>
        public static System.Drawing.Icon AppIcon
        {
            get
            {
                if (appIcon == null)
                {
                    try { appIcon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
                }
                return appIcon;
            }
        }

        private static bool IsAdministrator()
        {
            using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static void ShowError(Exception ex)
        {
            MessageBox.Show("Ocorreu um erro inesperado:\n\n" + (ex != null ? ex.Message : "desconhecido"),
                "Desinstalador Completo", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
