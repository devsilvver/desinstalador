using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace DesinstaladorCompleto
{
    internal class UninstallResult
    {
        public bool Started;
        public int ExitCode = -1;
        public string Error;
        public string CommandLine;
        public bool StoppedWaiting;
    }

    /// <summary>
    /// Executa o desinstalador original do programa e espera ele terminar — inclusive os processos filhos
    /// (muitos desinstaladores se copiam para a pasta Temp e fecham o processo original). Para isso, o
    /// desinstalador é colocado em um "Job Object" do Windows e acompanhamos todos os processos do job.
    /// </summary>
    internal static class UninstallRunner
    {
        private static readonly Regex GuidRx = new Regex(@"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}");

        public static string FindMsiGuid(InstalledProgram p)
        {
            if (p == null || p.Kind == ProgramKind.StoreApp) return null;
            if (p.UninstallString != null && p.UninstallString.IndexOf("msiexec", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Match m = GuidRx.Match(p.UninstallString);
                if (m.Success) return m.Value.ToUpperInvariant();
            }
            if (p.Kind == ProgramKind.Msi && p.KeyName != null)
            {
                Match m = GuidRx.Match(p.KeyName);
                if (m.Success && m.Index == 0 && m.Length == p.KeyName.Length) return m.Value.ToUpperInvariant();
            }
            return null;
        }

        private static bool IsInno(string exe)
        {
            return Regex.IsMatch(PathUtil.FileNameNoExt(exe ?? ""), @"^unins\d{3}$", RegexOptions.IgnoreCase);
        }

        public static bool SupportsSilent(InstalledProgram p)
        {
            if (p.Kind == ProgramKind.StoreApp) return false;
            if (FindMsiGuid(p) != null) return true;
            if (!string.IsNullOrWhiteSpace(p.QuietUninstallString)) return true;
            return IsInno(PathUtil.ExtractExePath(p.UninstallString));
        }

        /// <summary>Caminho do desinstalador, se ele for um executável que deveria existir no disco.</summary>
        public static string UninstallerExe(InstalledProgram p)
        {
            if (p.Kind == ProgramKind.StoreApp || FindMsiGuid(p) != null) return null;
            string exe = PathUtil.Normalize(PathUtil.ExtractExePath(p.UninstallString));
            return exe;
        }

        public static string BuildCommand(InstalledProgram p, bool silent, Action<string> log)
        {
            string guid = FindMsiGuid(p);
            if (guid != null)
            {
                // MSI: sempre /x (algumas entradas usam /I, que abre o modo "Modificar")
                return "\"" + Shell.SystemExe("msiexec.exe") + "\" /x " + guid + (silent ? " /qn /norestart" : "");
            }
            if (silent && !string.IsNullOrWhiteSpace(p.QuietUninstallString)) return Expand(p.QuietUninstallString);
            if (string.IsNullOrWhiteSpace(p.UninstallString)) return null;
            string cmd = Expand(p.UninstallString);
            if (silent)
            {
                if (IsInno(PathUtil.ExtractExePath(cmd))) cmd += " /VERYSILENT /SUPPRESSMSGBOXES /NORESTART";
                else log("Modo silencioso não disponível para este programa; o desinstalador será exibido normalmente.");
            }
            return cmd;
        }

        private static string Expand(string s)
        {
            try { return Environment.ExpandEnvironmentVariables(s.Trim()); } catch { return s.Trim(); }
        }

        public static UninstallResult Run(InstalledProgram p, bool silent, Action<string> log, CancellationToken ct)
        {
            if (p.Kind == ProgramKind.StoreApp) return RunStore(p, log);
            string cmd = BuildCommand(p, silent, log);
            if (cmd == null) return new UninstallResult { Error = "O programa não possui um comando de desinstalação registrado." };
            log("Comando: " + cmd);
            return RunWithJob(cmd, log, ct);
        }

        private static UninstallResult RunStore(InstalledProgram p, Action<string> log)
        {
            var r = new UninstallResult { Started = true, CommandLine = "Remove-AppxPackage " + p.PackageFullName };
            string fn = (p.PackageFullName ?? "").Replace("'", "''");
            log("Removendo o pacote " + p.PackageFullName + " ...");
            Shell.Result res = Shell.RunPowerShell(
                "$ErrorActionPreference='Stop'; try { Remove-AppxPackage -Package '" + fn + "' -AllUsers } catch { Remove-AppxPackage -Package '" + fn + "' }",
                600000);
            r.ExitCode = res.ExitCode;
            if (res.ExitCode != 0) r.Error = (res.StdErr ?? "").Trim();
            return r;
        }

        private static UninstallResult RunWithJob(string cmd, Action<string> log, CancellationToken ct)
        {
            var r = new UninstallResult { CommandLine = cmd };
            IntPtr job = NativeMethods.CreateJobObject(IntPtr.Zero, null);
            IntPtr hProcess = IntPtr.Zero;
            bool inJob = false;
            Process shellProc = null;
            // A pasta de trabalho é a Temp: se fosse a pasta do programa, o próprio processo a manteria bloqueada.
            string workDir = Path.GetTempPath();
            try
            {
                var si = new NativeMethods.STARTUPINFO();
                si.cb = Marshal.SizeOf(typeof(NativeMethods.STARTUPINFO));
                NativeMethods.PROCESS_INFORMATION pi;
                var sb = new StringBuilder(cmd, Math.Max(cmd.Length + 1, 1024));
                if (NativeMethods.CreateProcess(null, sb, IntPtr.Zero, IntPtr.Zero, false, NativeMethods.CREATE_SUSPENDED,
                                                IntPtr.Zero, workDir, ref si, out pi))
                {
                    hProcess = pi.hProcess;
                    inJob = job != IntPtr.Zero && NativeMethods.AssignProcessToJobObject(job, pi.hProcess);
                    NativeMethods.ResumeThread(pi.hThread);
                    NativeMethods.CloseHandle(pi.hThread);
                    r.Started = true;
                }
                else
                {
                    int err = Marshal.GetLastWin32Error();
                    log("Não foi possível iniciar diretamente (erro " + err + "); tentando pelo Shell do Windows...");
                    string exe, args;
                    PathUtil.SplitCommand(cmd, out exe, out args);
                    if (!string.IsNullOrEmpty(exe))
                    {
                        shellProc = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true, WorkingDirectory = workDir });
                        if (shellProc != null)
                        {
                            r.Started = true;
                            try
                            {
                                hProcess = shellProc.Handle;
                                inJob = job != IntPtr.Zero && NativeMethods.AssignProcessToJobObject(job, hProcess);
                            }
                            catch { }
                        }
                    }
                }

                if (!r.Started)
                {
                    r.Error = "Não foi possível iniciar o desinstalador.";
                    return r;
                }

                log("Desinstalador iniciado. Siga as instruções na janela do desinstalador; aguardando a conclusão...");
                bool mainExited = false, announced = false;
                while (true)
                {
                    if (ct.IsCancellationRequested)
                    {
                        r.StoppedWaiting = true;
                        log("Espera interrompida pelo usuário (o desinstalador pode continuar em execução).");
                        break;
                    }
                    if (!mainExited && hProcess != IntPtr.Zero && NativeMethods.WaitForSingleObject(hProcess, 0) == 0)
                    {
                        mainExited = true;
                        uint code;
                        if (NativeMethods.GetExitCodeProcess(hProcess, out code)) r.ExitCode = unchecked((int)code);
                    }
                    if (inJob)
                    {
                        NativeMethods.JOBOBJECT_BASIC_ACCOUNTING_INFORMATION info;
                        if (NativeMethods.QueryInformationJobObject(job, NativeMethods.JobObjectBasicAccountingInformation, out info,
                            Marshal.SizeOf(typeof(NativeMethods.JOBOBJECT_BASIC_ACCOUNTING_INFORMATION)), IntPtr.Zero))
                        {
                            if (info.ActiveProcesses == 0) break;
                            if (mainExited && !announced)
                            {
                                announced = true;
                                log("O desinstalador abriu processos auxiliares; aguardando que terminem...");
                            }
                        }
                        else if (mainExited) break;
                    }
                    else if (mainExited || hProcess == IntPtr.Zero) break;
                    Thread.Sleep(400);
                }
            }
            catch (Exception ex)
            {
                r.Error = ex.Message;
            }
            finally
            {
                if (shellProc != null) shellProc.Dispose();
                else if (hProcess != IntPtr.Zero) NativeMethods.CloseHandle(hProcess);
                if (job != IntPtr.Zero) NativeMethods.CloseHandle(job); // sem KILL_ON_JOB_CLOSE: nada é encerrado
            }
            return r;
        }
    }
}
