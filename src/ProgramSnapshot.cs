using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DesinstaladorCompleto
{
    /// <summary>
    /// "Fotografia" do programa tirada ANTES da desinstalação: pasta de instalação, executáveis e nomes
    /// que identificam o programa. Depois da desinstalação a entrada do registro some, então isso precisa ser guardado antes.
    /// </summary>
    internal class ProgramSnapshot
    {
        public InstalledProgram Program;
        public string InstallDir;
        public bool InstallDirInferred;
        public Confidence InstallDirConfidence = Confidence.Alta;
        public readonly List<string> ExtraDirs = new List<string>();
        public readonly List<string> ExeFileNames = new List<string>();          // nomes reais (sem .exe), não genéricos
        public readonly HashSet<string> ExeNames = new HashSet<string>();        // nomes normalizados dos executáveis principais
        public readonly List<string> StrongNames = new List<string>();           // nomes normalizados do programa
        public string PublisherNorm = "", PublisherFirst = "";
        public string MsiGuid;
        public string PackageFamilyName;

        private static readonly Regex GenericExe = new Regex(
            @"^(unins\d*|uninst.*|uninstall.*|.*uninstaller.*|setup.*|install.*|.*updater.*|update.*|autoupdate.*|.*crash.*|" +
            @"elevat.*|.*helper.*|launcher|vc_?redist.*|dotnet.*|ndp.*|maintenanceservice.*|repair|createdump|squirrel|cmd|" +
            @"conhost|rundll32|msiexec|java|javaw|javaws|jabswitch|jaccessinspector|keytool|python|pythonw|py|pyw|node|perl|" +
            @"ruby|bash|sh|git|curl|wget|7z|7za|7zg|ffmpeg|ffprobe|adb|fastboot|openssl|unzip|zip|tar|ssh|scp|sftp|" +
            @"chrome_proxy|nacl64|cefsharp.*|cef.*|subprocess|worker|service|svc|agent|daemon|server|client|host|app|" +
            @"application|main|start|run|test|debug|report.*|send.*|feedback.*|register.*|regasm|regsvr32|dxsetup|" +
            @"vcruntime.*|runtime.*|.*_x64|.*_x86|7zfm)$", RegexOptions.IgnoreCase);

        private static readonly string[] UninstallSubdirNames = { "uninstall", "uninst", "_uninst", "uninstaller", "uninstall information", "_uninstall" };

        public static bool IsGenericExe(string nameNoExt)
        {
            return string.IsNullOrEmpty(nameNoExt) || nameNoExt.Length < 3 || GenericExe.IsMatch(nameNoExt);
        }

        public static ProgramSnapshot Capture(InstalledProgram p, Action<string> log)
        {
            var s = new ProgramSnapshot { Program = p, PackageFamilyName = p.PackageFamilyName };
            NameUtil.NormalizePublisher(p.Publisher, out s.PublisherNorm, out s.PublisherFirst);
            s.MsiGuid = UninstallRunner.FindMsiGuid(p);

            // Nomes do programa e executável principal (do ícone)
            s.AddStrong(p.DisplayName);
            int idx;
            string icon = PathUtil.IconPath(p.DisplayIcon, out idx);
            if (icon != null && icon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !PathUtil.IsSameOrUnder(icon, Folders.WinDir))
                s.AddMainExe(PathUtil.FileNameNoExt(icon));
            if (p.Kind == ProgramKind.StoreApp)
            {
                s.AddStrong(p.PackageName);
                if (p.PackageName != null && p.PackageName.Contains("."))
                    s.AddStrong(p.PackageName.Substring(p.PackageName.LastIndexOf('.') + 1));
            }
            else
            {
                string key = Regex.Replace(p.KeyName ?? "", "_is1$", "", RegexOptions.IgnoreCase);
                if (!NameUtil.IsGuidLike(key) && !key.StartsWith("{")) s.AddStrong(key);
                s.DetermineInstallDir(log);
                if (s.InstallDir != null) s.AddStrong(PathUtil.FileName(s.InstallDir));
                s.DetermineExtraDirs();
            }

            // Executáveis (coletados antes que o desinstalador os remova)
            if (s.InstallDir != null && Directory.Exists(s.InstallDir))
            {
                foreach (string f in FsUtil.Files(s.InstallDir, "*.exe")) s.AddMainExe(PathUtil.FileNameNoExt(f));
                int count = 0;
                s.CollectExes(s.InstallDir, 0, ref count);
            }
            return s;
        }

        public void AddStrong(string raw)
        {
            string n = NameUtil.NormalizeName(raw);
            AddStrongNorm(n);
            foreach (string pre in new[] { PublisherNorm, PublisherFirst })
            {
                if (!string.IsNullOrEmpty(pre) && n.StartsWith(pre) && n.Length - pre.Length >= 3)
                    AddStrongNorm(n.Substring(pre.Length));
            }
        }

        private void AddStrongNorm(string n)
        {
            if (n.Length >= 3 && n.Count(char.IsLetter) >= 2 && !SafetyGuard.IsGeneric(n) && !StrongNames.Contains(n)) StrongNames.Add(n);
        }

        private void AddMainExe(string name)
        {
            if (IsGenericExe(name)) return;
            string n = NameUtil.NormalizeName(name);
            if (n.Length >= 3 && !SafetyGuard.IsGeneric(n)) ExeNames.Add(n);
            AddExeFile(name);
        }

        private void AddExeFile(string name)
        {
            if (IsGenericExe(name)) return;
            if (!ExeFileNames.Contains(name, StringComparer.OrdinalIgnoreCase)) ExeFileNames.Add(name);
        }

        private void CollectExes(string dir, int depth, ref int count)
        {
            if (depth > 3 || count > 400) return;
            foreach (string f in FsUtil.Files(dir, "*.exe"))
            {
                if (++count > 400) return;
                AddExeFile(PathUtil.FileNameNoExt(f));
            }
            foreach (string d in FsUtil.Dirs(dir))
                if (!FsUtil.IsReparse(d)) CollectExes(d, depth + 1, ref count);
        }

        public bool NameMatchesProgram(string folderName)
        {
            string n = NameUtil.NormalizeName(folderName);
            return n.Length >= 3 && (StrongNames.Contains(n) || ExeNames.Contains(n));
        }

        private static bool IsAcceptableInstallDir(string dir, out string why)
        {
            if (!SafetyGuard.CanDeletePath(dir, false, out why)) return false;
            string temp = Folders.Temp;
            if (temp != null && PathUtil.IsSameOrUnder(dir, temp)) { why = "pasta temporária"; return false; }
            foreach (string cache in new[] { Folders.Sub(Folders.ProgramData, "Package Cache"), Folders.Sub(Folders.Local, "Package Cache") })
                if (cache != null && PathUtil.IsSameOrUnder(dir, cache)) { why = "cache de instaladores"; return false; }
            return true;
        }

        private static readonly HashSet<string> InstallValueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "InstallPath", "InstallLocation", "InstallDir", "Install_Dir", "InstallFolder", "InstallationPath",
            "InstallRoot", "InstallDirectory", "AppPath", "ApplicationPath", "ExecutablePath"
        };

        /// <summary>
        /// Procura a pasta de instalação nas chaves do registro do próprio programa (ex.: HKLM\SOFTWARE\Python\PythonCore\3.14\InstallPath).
        /// Só é aceita se houver exatamente uma pasta candidata (se houver várias, pode ser de outra versão).
        /// </summary>
        private string FindInstallDirInRegistry()
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                foreach (string basePath in new[] { "SOFTWARE", @"SOFTWARE\WOW6432Node" })
                {
                    using (RegistryKey bk = RegUtil.Open(hive, basePath))
                    {
                        foreach (string name in RegUtil.SubKeyNames(bk))
                        {
                            string n = NameUtil.NormalizeName(name);
                            if (StrongNames.Contains(n)) { SearchInstallValues(hive, basePath + "\\" + name, 0, found); continue; }
                            if (n.Length >= 3 && (n == PublisherNorm || n == PublisherFirst) && !SafetyGuard.IsGeneric(n))
                            {
                                using (RegistryKey pk = RegUtil.OpenSub(bk, name))
                                    foreach (string sub in RegUtil.SubKeyNames(pk))
                                        if (StrongNames.Contains(NameUtil.NormalizeName(sub)))
                                            SearchInstallValues(hive, basePath + "\\" + name + "\\" + sub, 0, found);
                            }
                        }
                    }
                }
            }
            return found.Count == 1 ? found.First() : null;
        }

        private void SearchInstallValues(RegistryHive hive, string path, int depth, HashSet<string> found)
        {
            if (depth > 4 || found.Count > 5) return;
            using (RegistryKey k = RegUtil.Open(hive, path))
            {
                if (k == null) return;
                string keyName = path.Substring(path.LastIndexOf('\\') + 1);
                var candidates = new List<string>();
                foreach (string vn in RegUtil.ValueNames(k))
                    if (InstallValueNames.Contains(vn)) candidates.Add(RegUtil.GetString(k, vn));
                if (InstallValueNames.Contains(keyName)) candidates.Add(RegUtil.GetString(k, ""));
                foreach (string c in candidates)
                {
                    string dir = PathUtil.Normalize(c);
                    if (dir == null) continue;
                    if (File.Exists(dir)) dir = PathUtil.Parent(dir);
                    string why;
                    if (dir != null && Directory.Exists(dir) && IsAcceptableInstallDir(dir, out why)) found.Add(dir);
                }
                string[] subs = RegUtil.SubKeyNames(k);
                if (subs.Length > 200) return;
                foreach (string sub in subs) SearchInstallValues(hive, path + "\\" + sub, depth + 1, found);
            }
        }

        private static IEnumerable<string> InstallRoots()
        {
            yield return Folders.SF(Environment.SpecialFolder.ProgramFiles);
            yield return Folders.SF(Environment.SpecialFolder.ProgramFilesX86);
            yield return Folders.Env("ProgramW6432");
            yield return Folders.Sub(Folders.Local, "Programs");
            yield return Folders.Local;
            yield return Folders.Roaming;
            yield return Folders.ProgramData;
        }

        private static bool IsChildOrGrandchildOfInstallRoot(string dir)
        {
            string parent = PathUtil.Parent(dir);
            string grand = PathUtil.Parent(parent);
            foreach (string r in InstallRoots())
            {
                if (r == null) continue;
                if (string.Equals(parent, r, StringComparison.OrdinalIgnoreCase) || string.Equals(grand, r, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private void DetermineInstallDir(Action<string> log)
        {
            var p = Program;
            string why;
            string loc = PathUtil.Normalize(p.InstallLocation);
            if (loc != null)
            {
                if (IsAcceptableInstallDir(loc, out why)) { InstallDir = loc; return; }
                log("Pasta de instalação registrada ignorada por segurança: " + loc + " (" + why + ")");
            }

            // Sem InstallLocation: procura nas chaves de registro do próprio programa
            string fromReg = FindInstallDirInRegistry();
            if (fromReg != null)
            {
                InstallDir = fromReg;
                InstallDirInferred = true;
                string fn = NameUtil.NormalizeName(PathUtil.FileName(fromReg));
                bool nameOk = NameMatchesProgram(PathUtil.FileName(fromReg)) || StrongNames.Any(sn => sn.Length >= 4 && fn.StartsWith(sn));
                InstallDirConfidence = nameOk || IsChildOrGrandchildOfInstallRoot(fromReg) ? Confidence.Alta : Confidence.Media;
                return;
            }

            // Senão, deduz a partir do ícone ou do desinstalador
            int idx;
            var candidates = new List<string>();
            string icon = PathUtil.IconPath(p.DisplayIcon, out idx);
            if (icon != null) candidates.Add(PathUtil.Parent(icon));
            string un = PathUtil.Normalize(PathUtil.ExtractExePath(p.UninstallString));
            if (un != null) candidates.Add(PathUtil.Parent(un));

            foreach (string c in candidates)
            {
                string dir = c;
                if (dir == null) continue;
                if (UninstallSubdirNames.Contains(PathUtil.FileName(dir).ToLowerInvariant())) dir = PathUtil.Parent(dir);
                if (dir == null || !IsAcceptableInstallDir(dir, out why)) continue;
                bool nameOk = NameMatchesProgram(PathUtil.FileName(dir));
                if (nameOk || IsChildOrGrandchildOfInstallRoot(dir))
                {
                    InstallDir = dir;
                    InstallDirInferred = true;
                    InstallDirConfidence = nameOk ? Confidence.Alta : Confidence.Media;
                    return;
                }
            }
        }

        private void DetermineExtraDirs()
        {
            var p = Program;
            int idx;
            var candidates = new List<string>();
            string icon = PathUtil.IconPath(p.DisplayIcon, out idx);
            if (icon != null) candidates.Add(PathUtil.Parent(icon));
            string un = PathUtil.Normalize(PathUtil.ExtractExePath(p.UninstallString));
            if (un != null) candidates.Add(PathUtil.Parent(un));

            string keyGuid = Regex.Replace(p.KeyName ?? "", "_is1$", "", RegexOptions.IgnoreCase);
            foreach (string d in candidates)
            {
                if (d == null) continue;
                if (InstallDir != null && PathUtil.IsSameOrUnder(d, InstallDir)) continue;
                if (ExtraDirs.Contains(d, StringComparer.OrdinalIgnoreCase)) continue;
                string why;
                if (!SafetyGuard.CanDeletePath(d, false, out why)) continue;
                string name = PathUtil.FileName(d);
                bool guidFolder = (NameUtil.IsGuidLike(keyGuid) && name.IndexOf(keyGuid.Trim('{', '}'), StringComparison.OrdinalIgnoreCase) >= 0) ||
                                  (MsiGuid != null && name.IndexOf(MsiGuid.Trim('{', '}'), StringComparison.OrdinalIgnoreCase) >= 0);
                string norm = NameUtil.NormalizeName(name);
                if (guidFolder || StrongNames.Contains(norm)) ExtraDirs.Add(d);
            }
        }
    }
}
