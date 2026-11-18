using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using Microsoft.Win32;

namespace DesinstaladorCompleto
{
    internal class CleanReport
    {
        public int Deleted;
        public int Kept;
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Remaining = new List<string>();   // caminhos em uso (podem ser agendados para a reinicialização)
        public string BackupDir;
        public bool EnvChanged;
    }

    /// <summary>
    /// Exclui os itens que o usuário confirmou. Antes de qualquer exclusão, faz backup (.reg) de todas as
    /// chaves/valores do registro envolvidos e das tarefas agendadas. Cada item é revalidado pelo SafetyGuard.
    /// </summary>
    internal static class Cleaner
    {
        private const string KeptMarker = "\u0001mantido";

        public static string BackupRoot
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesinstaladorCompleto", "Backups"); }
        }

        public static CleanReport Execute(List<LeftoverItem> items, bool recycle, string programName, Action<string> log)
        {
            var rep = new CleanReport();
            rep.BackupDir = Path.Combine(BackupRoot, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_" + SafeName(programName));
            Directory.CreateDirectory(rep.BackupDir);
            log("Salvando backup em: " + rep.BackupDir);
            Backup(items, rep.BackupDir, log);

            foreach (LeftoverItem it in items.Where(i => i.Type == LeftoverType.Service))
            {
                LeftoverItem item = it;
                Do(rep, item, log, () => DeleteService(item, log));
            }
            foreach (LeftoverItem it in items.Where(i => i.Type == LeftoverType.ScheduledTask))
            {
                LeftoverItem item = it;
                Do(rep, item, log, () => DeleteTask(item));
            }
            foreach (LeftoverItem it in items.Where(i => i.Type == LeftoverType.RegistryValue))
            {
                LeftoverItem item = it;
                Do(rep, item, log, () => DeleteRegValue(item));
                if (SafetyGuard.IsEnvironmentKey(item.Path)) rep.EnvChanged = true;
            }
            foreach (LeftoverItem it in items.Where(i => i.Type == LeftoverType.PathEntry))
            {
                LeftoverItem item = it;
                Do(rep, item, log, () => RemovePathEntry(item));
                rep.EnvChanged = true;
            }
            foreach (LeftoverItem it in items.Where(i => i.Type == LeftoverType.RegistryKey)
                                             .OrderBy(i => i.DeleteOnlyIfEmpty).ThenByDescending(i => Depth(i.Path)))
            {
                LeftoverItem item = it;
                Do(rep, item, log, () => DeleteRegKey(item));
            }
            foreach (LeftoverItem it in items.Where(i => i.Type == LeftoverType.File))
            {
                LeftoverItem item = it;
                Do(rep, item, log, () => DeleteFile(item, recycle, rep));
            }
            foreach (LeftoverItem it in items.Where(i => i.Type == LeftoverType.Folder)
                                             .OrderBy(i => i.DeleteOnlyIfEmpty).ThenByDescending(i => Depth(i.Path)))
            {
                LeftoverItem item = it;
                Do(rep, item, log, () => DeleteFolder(item, recycle, rep));
            }

            if (rep.EnvChanged) BroadcastEnvironmentChange();
            WriteLog(items, rep);
            return rep;
        }

        private static int Depth(string path)
        {
            return (path ?? "").Count(c => c == '\\');
        }

        private static string SafeName(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "programa")
                sb.Append(Path.GetInvalidFileNameChars().Contains(c) ? '_' : c);
            string r = sb.ToString().Trim();
            return r.Length > 50 ? r.Substring(0, 50) : r;
        }

        private static void Do(CleanReport rep, LeftoverItem it, Action<string> log, Func<string> action)
        {
            try
            {
                string err = action();
                if (err == null)
                {
                    rep.Deleted++;
                    it.Deleted = true;
                    log("Excluído: " + it.Display);
                }
                else if (err == KeptMarker)
                {
                    rep.Kept++;
                    log("Mantido (não ficou vazio): " + it.Display);
                }
                else
                {
                    rep.Errors.Add(it.Display + "  —  " + err);
                    log("FALHA: " + it.Display + "  —  " + err);
                }
            }
            catch (Exception ex)
            {
                rep.Errors.Add(it.Display + "  —  " + ex.Message);
                log("FALHA: " + it.Display + "  —  " + ex.Message);
            }
        }

        // ------------------------------------------------------------------ backup

        private static void Backup(List<LeftoverItem> items, string dir, Action<string> log)
        {
            var exported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int n = 0;
            foreach (LeftoverItem it in items)
            {
                string key = null;
                if (it.IsRegistry) key = RegUtil.LongHive(it.Hive) + "\\" + it.Path;
                else if (it.Type == LeftoverType.Service) key = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\" + it.Path;
                if (key == null || !exported.Add(key)) continue;
                n++;
                string file = Path.Combine(dir, "registro_" + n.ToString("000") + ".reg");
                try
                {
                    Shell.Result r = Shell.Run(Shell.SystemExe("reg.exe"), "export \"" + key + "\" \"" + file + "\" /y /reg:64", 60000, Shell.Oem);
                    if (r.ExitCode != 0) log("  Aviso: não foi possível exportar " + key + ": " + r.StdErr.Trim());
                }
                catch (Exception ex) { log("  Aviso: falha no backup de " + key + ": " + ex.Message); }
            }

            foreach (LeftoverItem it in items.Where(i => i.Type == LeftoverType.ScheduledTask && i.TaskFile != null))
            {
                try
                {
                    string tdir = Path.Combine(dir, "tarefas");
                    Directory.CreateDirectory(tdir);
                    File.Copy(it.TaskFile, Path.Combine(tdir, SafeName(it.Path.Trim('\\').Replace('\\', '_')) + ".xml"), true);
                }
                catch { }
            }

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Itens selecionados para exclusão em " + DateTime.Now);
                sb.AppendLine("Para restaurar o registro, dê dois cliques nos arquivos .reg desta pasta.");
                sb.AppendLine("Arquivos e pastas enviados para a Lixeira podem ser restaurados por ela.");
                sb.AppendLine();
                foreach (LeftoverItem it in items) sb.AppendLine("[" + it.Category + "] " + it.Display);
                File.WriteAllText(Path.Combine(dir, "itens_selecionados.txt"), sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        private static void WriteLog(List<LeftoverItem> items, CleanReport rep)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Resultado: " + rep.Deleted + " excluído(s), " + rep.Kept + " mantido(s), " + rep.Errors.Count + " falha(s).");
                sb.AppendLine();
                foreach (LeftoverItem it in items) sb.AppendLine((it.Deleted ? "[EXCLUÍDO] " : "[NÃO EXCLUÍDO] ") + it.Display);
                if (rep.Errors.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Falhas:");
                    foreach (string e in rep.Errors) sb.AppendLine("  " + e);
                }
                File.WriteAllText(Path.Combine(rep.BackupDir, "resultado.txt"), sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        // ------------------------------------------------------------------ exclusões

        private static string DeleteService(LeftoverItem it, Action<string> log)
        {
            string why;
            if (!SafetyGuard.CanDeleteRegistryKey(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\" + it.Path, out why))
                return "bloqueado por segurança: " + why;
            try
            {
                using (var sc = new ServiceController(it.Path))
                {
                    if (sc.Status != ServiceControllerStatus.Stopped)
                    {
                        log("Parando o serviço " + it.Path + "...");
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
                    }
                }
            }
            catch (Exception ex) { log("  Aviso ao parar o serviço: " + ex.Message); }

            Shell.Result r = Shell.Run(Shell.SystemExe("sc.exe"), "delete \"" + it.Path + "\"", 30000, Shell.Oem);
            if (r.ExitCode == 0 || r.ExitCode == 1060 /* não existe */ || r.ExitCode == 1072 /* já marcado para exclusão */) return null;
            return "sc delete retornou " + r.ExitCode + ": " + (r.StdOut + r.StdErr).Trim();
        }

        private static string DeleteTask(LeftoverItem it)
        {
            if (it.TaskFile != null && !File.Exists(it.TaskFile)) return null;
            Shell.Result r = Shell.Run(Shell.SystemExe("schtasks.exe"), "/Delete /TN \"" + it.Path + "\" /F", 30000, Shell.Oem);
            if (r.ExitCode == 0) return null;
            return "schtasks retornou " + r.ExitCode + ": " + (r.StdOut + r.StdErr).Trim();
        }

        private static string DeleteRegValue(LeftoverItem it)
        {
            string why;
            if (!SafetyGuard.CanDeleteRegistryValue(it.Hive, it.Path, it.ValueName, out why)) return "bloqueado por segurança: " + why;
            using (RegistryKey k = RegUtil.Open(it.Hive, it.Path, true))
            {
                if (k == null) return RegUtil.KeyExists(it.Hive, it.Path) ? "sem permissão para alterar a chave" : null;
                k.DeleteValue(it.ValueName, false);
            }
            return null;
        }

        private static string RemovePathEntry(LeftoverItem it)
        {
            using (RegistryKey k = RegUtil.Open(it.Hive, it.Path, true))
            {
                if (k == null) return "sem permissão para alterar as variáveis de ambiente";
                string raw = RegUtil.GetRawString(k, it.ValueName);
                if (raw == null) return null;
                RegistryValueKind kind = k.GetValueKind(it.ValueName);
                string[] parts = raw.Split(';');
                string[] kept = parts.Where(x => !string.Equals(x.Trim(), it.Data.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
                if (kept.Length == parts.Length) return null;
                k.SetValue(it.ValueName, string.Join(";", kept), kind == RegistryValueKind.ExpandString ? RegistryValueKind.ExpandString : RegistryValueKind.String);
            }
            return null;
        }

        private static string DeleteRegKey(LeftoverItem it)
        {
            string why;
            if (!SafetyGuard.CanDeleteRegistryKey(it.Hive, it.Path, out why)) return "bloqueado por segurança: " + why;
            int i = it.Path.LastIndexOf('\\');
            string parent = it.Path.Substring(0, i), name = it.Path.Substring(i + 1);
            using (RegistryKey pk = RegUtil.Open(it.Hive, parent, true))
            {
                if (pk == null) return RegUtil.KeyExists(it.Hive, it.Path) ? "sem permissão" : null;
                using (RegistryKey k = pk.OpenSubKey(name))
                {
                    if (k == null) return null;
                    if (it.DeleteOnlyIfEmpty && (k.SubKeyCount > 0 || k.ValueCount > 0)) return KeptMarker;
                }
                pk.DeleteSubKeyTree(name, false);
            }
            return null;
        }

        private static string DeleteFile(LeftoverItem it, bool recycle, CleanReport rep)
        {
            string why;
            if (!SafetyGuard.CanDeletePath(it.Path, true, out why)) return "bloqueado por segurança: " + why;
            if (!File.Exists(it.Path)) return null;
            try
            {
                if (recycle) NativeMethods.SendToRecycleBin(it.Path);
                if (File.Exists(it.Path))
                {
                    if (recycle) throw new IOException("não foi possível enviar para a Lixeira (arquivo em uso?)");
                    File.SetAttributes(it.Path, FileAttributes.Normal);
                    File.Delete(it.Path);
                }
                return null;
            }
            catch (Exception ex)
            {
                if (File.Exists(it.Path)) rep.Remaining.Add(it.Path);
                return ex.Message;
            }
        }

        private static string DeleteFolder(LeftoverItem it, bool recycle, CleanReport rep)
        {
            string why;
            if (!SafetyGuard.CanDeletePath(it.Path, false, out why)) return "bloqueado por segurança: " + why;
            if (!Directory.Exists(it.Path)) return null;

            if (it.DeleteOnlyIfEmpty)
            {
                var entries = FsUtil.Dirs(it.Path).Concat(FsUtil.Files(it.Path)).ToArray();
                if (entries.Any(e => !PathUtil.FileName(e).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))) return KeptMarker;
                foreach (string e in entries) { try { File.SetAttributes(e, FileAttributes.Normal); File.Delete(e); } catch { } }
                try { Directory.Delete(it.Path, false); } catch (Exception ex) { return ex.Message; }
                return null;
            }

            if (recycle)
            {
                NativeMethods.SendToRecycleBin(it.Path);
                if (!Directory.Exists(it.Path)) return null;
                rep.Remaining.Add(it.Path);
                return "alguns arquivos não puderam ir para a Lixeira (provavelmente em uso)";
            }

            var failures = new List<string>();
            DeletePermanent(it.Path, failures);
            if (!Directory.Exists(it.Path)) return null;
            rep.Remaining.Add(it.Path);
            return failures.Count + " arquivo(s) não puderam ser excluídos (provavelmente em uso)";
        }

        /// <summary>Exclusão recursiva que NUNCA segue junções/links simbólicos (remove apenas o link).</summary>
        private static void DeletePermanent(string dir, List<string> failures)
        {
            foreach (string f in FsUtil.Files(dir))
            {
                try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); }
                catch { failures.Add(f); }
            }
            foreach (string d in FsUtil.Dirs(dir))
            {
                if (FsUtil.IsReparse(d))
                {
                    try { Directory.Delete(d, false); } catch { failures.Add(d); }
                }
                else DeletePermanent(d, failures);
            }
            try
            {
                File.SetAttributes(dir, FileAttributes.Directory);
                Directory.Delete(dir, false);
            }
            catch { failures.Add(dir); }
        }

        /// <summary>Agenda a exclusão (definitiva) para a próxima inicialização do Windows.</summary>
        public static int ScheduleOnReboot(IEnumerable<string> paths, Action<string> log)
        {
            int count = 0;
            foreach (string p in paths)
            {
                bool isDir = Directory.Exists(p);
                string why;
                if (!SafetyGuard.CanDeletePath(p, !isDir, out why)) { log("Não agendado (segurança): " + p); continue; }
                if (isDir)
                {
                    var files = new List<string>();
                    var dirs = new List<string>();
                    Collect(p, files, dirs);
                    foreach (string f in files) if (NativeMethods.MoveFileEx(f, null, NativeMethods.MOVEFILE_DELAY_UNTIL_REBOOT)) count++;
                    foreach (string d in dirs.OrderByDescending(Depth)) NativeMethods.MoveFileEx(d, null, NativeMethods.MOVEFILE_DELAY_UNTIL_REBOOT);
                    NativeMethods.MoveFileEx(p, null, NativeMethods.MOVEFILE_DELAY_UNTIL_REBOOT);
                }
                else if (File.Exists(p) && NativeMethods.MoveFileEx(p, null, NativeMethods.MOVEFILE_DELAY_UNTIL_REBOOT)) count++;
                log("Agendado para exclusão na reinicialização: " + p);
            }
            return count;
        }

        private static void Collect(string dir, List<string> files, List<string> dirs)
        {
            files.AddRange(FsUtil.Files(dir));
            foreach (string d in FsUtil.Dirs(dir))
            {
                dirs.Add(d);
                if (!FsUtil.IsReparse(d)) Collect(d, files, dirs);
            }
        }

        private static void BroadcastEnvironmentChange()
        {
            try
            {
                IntPtr result;
                NativeMethods.SendMessageTimeout(NativeMethods.HWND_BROADCAST, NativeMethods.WM_SETTINGCHANGE, IntPtr.Zero,
                    "Environment", NativeMethods.SMTO_ABORTIFHUNG, 5000, out result);
            }
            catch { }
        }
    }
}
