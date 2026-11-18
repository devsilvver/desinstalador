using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DesinstaladorCompleto
{
    /// <summary>
    /// Procura resquícios de um programa SOMENTE em locais conhecidos de instalação (Arquivos de Programas, AppData,
    /// ProgramData, Menu Iniciar, Temp, registro de programas, serviços, tarefas...). Pastas de projetos e documentos do
    /// usuário nunca são varridas. Nada é excluído aqui: apenas uma lista é montada para o usuário revisar.
    /// </summary>
    internal class LeftoverScanner
    {
        private class NameMatch
        {
            public Confidence Conf;
            public string Reason;
            public string Warning;
        }

        private readonly ProgramSnapshot s;
        private readonly Action<string> log;
        private readonly bool mayStillBeInstalled;
        private readonly Dictionary<string, LeftoverItem> items = new Dictionary<string, LeftoverItem>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> otherNames = new HashSet<string>();
        private readonly List<string> otherDirs = new List<string>();
        private readonly HashSet<string> foundProgIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> foundClsids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> runValueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> startupLnkNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly bool installShared;
        private readonly string winDir = Folders.WinDir;

        public LeftoverScanner(ProgramSnapshot snapshot, IEnumerable<InstalledProgram> others, bool mayStillBeInstalled, Action<string> log)
        {
            s = snapshot;
            this.log = log ?? (x => { });
            this.mayStillBeInstalled = mayStillBeInstalled;

            foreach (InstalledProgram o in others)
            {
                if (o.Kind == ProgramKind.StoreApp) continue;
                string pf, pfirst;
                NameUtil.NormalizePublisher(o.Publisher, out pf, out pfirst);
                string n = NameUtil.NormalizeName(o.DisplayName);
                AddOtherName(n);
                foreach (string pre in new[] { pf, pfirst })
                    if (pre.Length > 0 && n.StartsWith(pre) && n.Length - pre.Length >= 3) AddOtherName(n.Substring(pre.Length));

                var paths = new List<string> { PathUtil.Normalize(o.InstallLocation) };
                int idx;
                string icon = PathUtil.IconPath(o.DisplayIcon, out idx);
                if (icon != null) paths.Add(PathUtil.Parent(icon));
                string un = PathUtil.Normalize(PathUtil.ExtractExePath(o.UninstallString));
                if (un != null) paths.Add(PathUtil.Parent(un));
                bool first = true;
                foreach (string loc in paths)
                {
                    if (loc != null)
                    {
                        string why;
                        if (SafetyGuard.CanDeletePath(loc, false, out why) && !otherDirs.Contains(loc, StringComparer.OrdinalIgnoreCase))
                        {
                            otherDirs.Add(loc);
                            if (first) AddOtherName(NameUtil.NormalizeName(PathUtil.FileName(loc)));
                        }
                    }
                    first = false;
                }
            }

            if (s.InstallDir != null)
                installShared = otherDirs.Any(d => PathUtil.IsSameOrUnder(d, s.InstallDir));
        }

        private void AddOtherName(string n)
        {
            if (n != null && n.Length >= 3) otherNames.Add(n);
        }

        /// <summary>Confiança para itens encontrados por referência à pasta de instalação.</summary>
        private Confidence RefConf
        {
            get { return installShared ? Confidence.Baixa : s.InstallDirConfidence; }
        }

        private string RefWarning
        {
            get { return installShared ? "A pasta de instalação também é usada por outro programa instalado" : null; }
        }

        private bool CanRefer
        {
            get { return s.InstallDir != null || s.ExtraDirs.Count > 0 || !string.IsNullOrEmpty(s.PackageFamilyName) || s.MsiGuid != null; }
        }

        // ================================================================== varredura

        public List<LeftoverItem> Scan()
        {
            Step("pasta de instalação", ScanInstallDirs);
            if (s.Program.Kind == ProgramKind.StoreApp)
            {
                Step("dados do aplicativo", ScanStorePackage);
            }
            else
            {
                Step("pastas do programa (Arquivos de Programas, AppData, ProgramData)", ScanNamedFolders);
                Step("atalhos (Menu Iniciar, Área de Trabalho, barra de tarefas)", ScanShortcuts);
                Step("arquivos temporários", ScanTemp);
                Step("cache Prefetch e relatórios de erro", ScanPrefetchAndReports);
                Step("entrada de desinstalação", ScanUninstallEntry);
                Step("chaves de registro do programa", ScanSoftwareKeys);
                Step("caminhos de aplicativos (App Paths)", ScanAppPaths);
                Step("inicialização automática", ScanRunKeys);
                Step("associações de arquivos, componentes COM e menu de contexto", ScanClasses);
                Step("registro como aplicativo padrão", ScanClients);
                Step("serviços", ScanServices);
                Step("tarefas agendadas", ScanTasks);
                Step("variáveis de ambiente", ScanEnvironment);
            }
            Step("regras de firewall", ScanFirewall);
            Step("rastros de uso no registro", ScanValueTraces);
            Step("aplicativos registrados", ScanRegisteredApplications);
            return FinalizeList();
        }

        private void Step(string name, Action action)
        {
            log("Verificando " + name + "...");
            try { action(); }
            catch (Exception ex) { log("  Aviso: falha ao verificar " + name + ": " + ex.Message); }
        }

        // ================================================================== correspondência de nomes

        private NameMatch MatchName(string name, bool strictOnly)
        {
            string n = NameUtil.NormalizeName(name);
            if (n.Length < 3 || SafetyGuard.IsGeneric(n)) return null;
            var m = new NameMatch();
            if (s.StrongNames.Contains(n))
            {
                m.Conf = Confidence.Alta;
                m.Reason = "Nome corresponde ao programa";
            }
            else if (s.ExeNames.Contains(n))
            {
                m.Conf = Confidence.Media;
                m.Reason = "Nome corresponde ao executável " + n + ".exe";
            }
            else if (!strictOnly && s.StrongNames.Any(sn => sn.Length >= 6 && n.StartsWith(sn)))
            {
                if (otherNames.Contains(n)) return null; // é exatamente o nome de outro programa instalado
                m.Conf = Confidence.Baixa;
                m.Reason = "Nome parecido com o do programa";
            }
            else return null;

            if (otherNames.Contains(n))
            {
                m.Conf = Confidence.Baixa;
                m.Warning = "O nome também corresponde a outro programa instalado";
            }
            return m;
        }

        private bool IsPublisherName(string name)
        {
            string n = NameUtil.NormalizeName(name);
            return n.Length >= 3 && (n == s.PublisherNorm || n == s.PublisherFirst);
        }

        private bool Refers(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (s.InstallDir != null && PathUtil.ReferencesDir(text, s.InstallDir)) return true;
            foreach (string d in s.ExtraDirs) if (PathUtil.ReferencesDir(text, d)) return true;
            if (!string.IsNullOrEmpty(s.PackageFamilyName) && text.IndexOf(s.PackageFamilyName, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (s.MsiGuid != null && text.IndexOf(@"\Installer\" + s.MsiGuid, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private bool RefersPath(string normalizedPath)
        {
            if (normalizedPath == null) return false;
            if (s.InstallDir != null && PathUtil.IsSameOrUnder(normalizedPath, s.InstallDir)) return true;
            return s.ExtraDirs.Any(d => PathUtil.IsSameOrUnder(normalizedPath, d));
        }

        private static string Join(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b;
            if (string.IsNullOrEmpty(b)) return a;
            return a + "; " + b;
        }

        private bool ExeMissing(string commandOrPath, out string exeNorm)
        {
            exeNorm = "";
            string exe = PathUtil.Normalize(PathUtil.ExtractExePath(commandOrPath));
            if (exe == null) return false;
            exeNorm = NameUtil.NormalizeName(PathUtil.FileNameNoExt(exe));
            return !File.Exists(exe) && !Directory.Exists(exe);
        }

        // ================================================================== inclusão de itens

        private void Put(LeftoverItem it)
        {
            LeftoverItem existing;
            string key = it.Key;
            if (items.TryGetValue(key, out existing))
            {
                if (existing.DeleteOnlyIfEmpty && !it.DeleteOnlyIfEmpty) items[key] = it;
                else if (!existing.DeleteOnlyIfEmpty && it.DeleteOnlyIfEmpty) return;
                else if (it.Confidence < existing.Confidence) items[key] = it;
            }
            else items[key] = it;
        }

        private bool IsProposedPath(string path)
        {
            return items.Values.Any(i => (i.Type == LeftoverType.Folder || i.Type == LeftoverType.File) &&
                                         string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
        }

        private bool AddFolder(string path, string category, Confidence conf, string reason, string warning = null, bool onlyIfEmpty = false)
        {
            string p = PathUtil.Normalize(path);
            if (p == null || !Directory.Exists(p)) return false;
            string why;
            if (!SafetyGuard.CanDeletePath(p, false, out why))
            {
                log("  Ignorado por segurança: " + p + " (" + why + ")");
                return false;
            }
            foreach (string od in otherDirs)
            {
                if (string.Equals(od, p, StringComparison.OrdinalIgnoreCase))
                {
                    conf = Confidence.Baixa;
                    warning = Join(warning, "Pasta também registrada por outro programa instalado");
                }
                else if (PathUtil.IsStrictlyUnder(od, p))
                {
                    log("  Ignorado: " + p + " contém a instalação de outro programa (" + od + ")");
                    return false;
                }
            }
            if (!onlyIfEmpty)
            {
                bool isProfile;
                string marker = FindUserDataMarker(p, out isProfile);
                if (marker != null)
                {
                    conf = Confidence.Baixa;
                    warning = Join(warning, isProfile
                        ? "Contém \"" + marker + "\": dados pessoais (perfis, e-mails, favoritos, senhas ou configurações)"
                        : "Contém \"" + marker + "\": pode ter projetos ou arquivos do usuário");
                }
            }
            Put(new LeftoverItem
            {
                Type = LeftoverType.Folder,
                Category = category,
                Path = p,
                Display = p + (onlyIfEmpty ? "   (somente se ficar vazia)" : ""),
                Confidence = conf,
                Reason = reason,
                Warning = warning,
                DeleteOnlyIfEmpty = onlyIfEmpty,
                SizeBytes = onlyIfEmpty ? -1 : FsUtil.DirSize(p, 20000)
            });
            return true;
        }

        private bool AddFile(string path, string category, Confidence conf, string reason, string warning = null)
        {
            string p = PathUtil.Normalize(path);
            if (p == null || !File.Exists(p)) return false;
            string why;
            if (!SafetyGuard.CanDeletePath(p, true, out why))
            {
                log("  Ignorado por segurança: " + p + " (" + why + ")");
                return false;
            }
            long size = -1;
            try { size = new FileInfo(p).Length; } catch { }
            Put(new LeftoverItem
            {
                Type = LeftoverType.File, Category = category, Path = p, Display = p, Confidence = conf,
                Reason = reason, Warning = warning, SizeBytes = size
            });
            return true;
        }

        private bool AddRegKey(RegistryHive hive, string subKey, string category, Confidence conf, string reason, string warning = null, bool onlyIfEmpty = false)
        {
            if (!RegUtil.KeyExists(hive, subKey)) return false;
            string why;
            if (!SafetyGuard.CanDeleteRegistryKey(hive, subKey, out why))
            {
                log("  Ignorado por segurança: " + RegUtil.Display(hive, subKey, null) + " (" + why + ")");
                return false;
            }
            Put(new LeftoverItem
            {
                Type = LeftoverType.RegistryKey, Category = category, Hive = hive, Path = subKey,
                Display = RegUtil.Display(hive, subKey, null) + (onlyIfEmpty ? "   (somente se ficar vazia)" : ""),
                Confidence = conf, Reason = reason, Warning = warning, DeleteOnlyIfEmpty = onlyIfEmpty
            });
            return true;
        }

        private bool AddRegValue(RegistryHive hive, string subKey, string valueName, string category, Confidence conf, string reason, string display = null, string warning = null)
        {
            string why;
            if (!SafetyGuard.CanDeleteRegistryValue(hive, subKey, valueName, out why))
            {
                log("  Ignorado por segurança: " + RegUtil.Display(hive, subKey, valueName) + " (" + why + ")");
                return false;
            }
            Put(new LeftoverItem
            {
                Type = LeftoverType.RegistryValue, Category = category, Hive = hive, Path = subKey, ValueName = valueName,
                Display = display ?? RegUtil.Display(hive, subKey, valueName), Confidence = conf, Reason = reason, Warning = warning
            });
            return true;
        }

        // ================================================================== arquivos e pastas

        private void ScanInstallDirs()
        {
            if (s.InstallDir != null)
            {
                string reason = s.InstallDirInferred ? "Pasta de instalação (identificada pelo desinstalador/ícone)" : "Pasta de instalação do programa";
                if (AddFolder(s.InstallDir, Categories.Pastas, RefConf, reason, RefWarning))
                    ProposeParentIfEmpty(s.InstallDir, Categories.Pastas);
            }
            foreach (string d in s.ExtraDirs)
                AddFolder(d, Categories.Pastas, Confidence.Media, "Pasta auxiliar do desinstalador do programa");
        }

        /// <summary>Se a pasta-mãe é a pasta do editor (ex.: "Mozilla") e só contém itens que serão removidos, propõe removê-la se ficar vazia.</summary>
        private void ProposeParentIfEmpty(string child, string category)
        {
            string parent = PathUtil.Parent(child);
            if (parent == null) return;
            string pname = PathUtil.FileName(parent);
            if (!IsPublisherName(pname) && MatchName(pname, true) == null) return;
            if (SafetyGuard.IsGeneric(NameUtil.NormalizeName(pname))) return;
            string[] entries = FsUtil.Dirs(parent).Concat(FsUtil.Files(parent)).ToArray();
            if (entries.Length > 0 && entries.All(IsProposedPath))
                AddFolder(parent, category, Confidence.Media, "Pasta do editor que ficará vazia", null, true);
        }

        private void ScanNamedFolders()
        {
            string profile = Folders.Profile;
            string local = Folders.Local;
            var roots = new List<string>
            {
                Folders.SF(Environment.SpecialFolder.ProgramFiles), Folders.SF(Environment.SpecialFolder.ProgramFilesX86),
                Folders.Env("ProgramW6432"), Folders.SF(Environment.SpecialFolder.CommonProgramFiles),
                Folders.SF(Environment.SpecialFolder.CommonProgramFilesX86), Folders.Env("CommonProgramW6432"),
                Folders.ProgramData, Folders.Roaming, local, Folders.LocalLow, Folders.Sub(local, "Programs"),
                Folders.Sub(local, @"VirtualStore\Program Files"), Folders.Sub(local, @"VirtualStore\Program Files (x86)"),
                Folders.Sub(local, @"VirtualStore\ProgramData")
            };
            foreach (string root in roots.Where(r => r != null).Distinct(StringComparer.OrdinalIgnoreCase))
                ScanRootChildren(root, Categories.Pastas);

            // Pastas de configuração ocultas no perfil (ex.: %USERPROFILE%\.vscode). Somente no nível do perfil,
            // nunca dentro de pastas de projetos.
            foreach (string dir in FsUtil.Dirs(profile))
            {
                string name = PathUtil.FileName(dir);
                if (!name.StartsWith(".") || name.Length < 4 || SafetyGuard.DeniedDotFolders.Contains(name)) continue;
                NameMatch m = MatchName(name.Substring(1), true);
                if (m != null) AddFolder(dir, Categories.Pastas, m.Conf, m.Reason + " (configurações no perfil do usuário)", m.Warning);
            }
        }

        private void ScanRootChildren(string root, string category)
        {
            foreach (string dir in FsUtil.Dirs(root))
            {
                string name = PathUtil.FileName(dir);
                NameMatch m = MatchName(name, false);
                if (m != null)
                {
                    AddFolder(dir, category, m.Conf, m.Reason + " (em " + root + ")", m.Warning);
                    continue;
                }
                if (IsPublisherName(name)) ScanPublisherFolder(dir, category);
            }
        }

        private void ScanPublisherFolder(string pubDir, string category)
        {
            bool generic = SafetyGuard.IsGeneric(NameUtil.NormalizeName(PathUtil.FileName(pubDir)));
            string[] subdirs = FsUtil.Dirs(pubDir);
            string[] files = FsUtil.Files(pubDir);
            int proposed = 0;
            foreach (string d in subdirs)
            {
                NameMatch m = CapForGenericPublisher(MatchName(PathUtil.FileName(d), generic), generic);
                if (m != null) AddFolder(d, category, m.Conf, m.Reason + " (dentro da pasta do editor)", m.Warning);
                if (IsProposedPath(PathUtil.Normalize(d))) proposed++;
            }
            if (!generic && proposed > 0 && proposed == subdirs.Length && files.Length == 0)
                AddFolder(pubDir, category, Confidence.Media, "Pasta do editor que ficará vazia", null, true);
        }

        private static readonly HashSet<string> ProjectExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".sln", ".csproj", ".vbproj", ".fsproj", ".vcxproj", ".code-workspace"
        };
        private static readonly HashSet<string> ProjectDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".svn", ".hg", "htdocs", "www", "wwwroot", "projects", "projetos", "meus projetos", "workspace", "repos"
        };
        private static readonly HashSet<string> PersonalExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".pst", ".ost", ".mbox", ".kdbx", ".wallet"
        };
        private static readonly HashSet<string> PersonalFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "profiles.ini", "places.sqlite", "Bookmarks", "Login Data", "key4.db", "logins.json", "wallet.dat"
        };
        private static readonly HashSet<string> PersonalDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "User Data", "Profiles", "Mail", "ImapMail"
        };

        /// <summary>
        /// Procura sinais de que a pasta contém projetos do usuário (repositórios git, soluções, htdocs) ou dados
        /// pessoais (perfis de navegador/e-mail, bancos de senhas). Nesses casos o item vem desmarcado e com aviso.
        /// </summary>
        private static string FindUserDataMarker(string root, out bool isProfile)
        {
            isProfile = false;
            var queue = new Queue<KeyValuePair<string, int>>();
            queue.Enqueue(new KeyValuePair<string, int>(root, 0));
            int seen = 0;
            while (queue.Count > 0 && seen < 6000)
            {
                var cur = queue.Dequeue();
                foreach (string f in FsUtil.Files(cur.Key))
                {
                    seen++;
                    string name = PathUtil.FileName(f);
                    string ext = Path.GetExtension(f);
                    if (ProjectExts.Contains(ext)) return name;
                    if (PersonalExts.Contains(ext) || PersonalFiles.Contains(name)) { isProfile = true; return name; }
                }
                foreach (string d in FsUtil.Dirs(cur.Key))
                {
                    seen++;
                    string n = PathUtil.FileName(d);
                    bool vcs = n == ".git" || n == ".svn" || n == ".hg";
                    // .git/.svn em qualquer nível; htdocs/www/projects só logo no início (ex.: C:\xampp\htdocs)
                    if (ProjectDirs.Contains(n) && (vcs || cur.Value == 0)) return n;
                    if (cur.Value <= 1 && PersonalDirs.Contains(n)) { isProfile = true; return n; }
                    if (cur.Value < 4 && !FsUtil.IsReparse(d)) queue.Enqueue(new KeyValuePair<string, int>(d, cur.Value + 1));
                }
            }
            return null;
        }

        /// <summary>Dentro de pastas/chaves de editores genéricos (ex.: "Microsoft"), nomes curtos são ambíguos: no máximo "Média".</summary>
        private static NameMatch CapForGenericPublisher(NameMatch m, bool generic)
        {
            if (m != null && generic && m.Conf == Confidence.Alta) m.Conf = Confidence.Media;
            return m;
        }

        private void ScanStorePackage()
        {
            string fam = s.PackageFamilyName;
            if (string.IsNullOrEmpty(fam)) return;
            AddFolder(Folders.Sub(Folders.Local, @"Packages\" + fam), Categories.Pastas, Confidence.Alta, "Dados e configurações do aplicativo");
            AddFolder(Folders.Sub(Folders.ProgramData, @"Packages\" + fam), Categories.Pastas, Confidence.Alta, "Dados do aplicativo (todos os usuários)");
            AddFolder(Folders.Sub(Folders.Local, @"Microsoft\WindowsApps\" + fam), Categories.Pastas, Confidence.Alta, "Atalho de execução do aplicativo");
        }

        // ================================================================== atalhos

        private void ScanShortcuts()
        {
            foreach (string menu in new[] { Folders.SF(Environment.SpecialFolder.Programs), Folders.SF(Environment.SpecialFolder.CommonPrograms) })
                if (menu != null) ScanStartMenuDir(menu, 0);

            string ql = Folders.Sub(Folders.Roaming, @"Microsoft\Internet Explorer\Quick Launch");
            var startupDirs = new[] { Folders.SF(Environment.SpecialFolder.Startup), Folders.SF(Environment.SpecialFolder.CommonStartup) };
            var flat = new List<string>
            {
                Folders.SF(Environment.SpecialFolder.DesktopDirectory), Folders.SF(Environment.SpecialFolder.CommonDesktopDirectory),
                ql, Folders.Sub(ql, @"User Pinned\TaskBar"), Folders.Sub(ql, @"User Pinned\StartMenu")
            };
            flat.AddRange(startupDirs);
            foreach (string d in flat.Where(x => x != null).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                bool isStartup = startupDirs.Any(x => string.Equals(x, d, StringComparison.OrdinalIgnoreCase));
                foreach (string f in FsUtil.Files(d))
                    if (IsShortcut(f)) CheckShortcut(f, isStartup);
            }
        }

        private static bool IsShortcut(string f)
        {
            string ext = Path.GetExtension(f).ToLowerInvariant();
            return ext == ".lnk" || ext == ".url";
        }

        private int ScanStartMenuDir(string dir, int depth)
        {
            int proposed = 0;
            foreach (string sub in FsUtil.Dirs(dir))
            {
                NameMatch m = MatchName(PathUtil.FileName(sub), false);
                if (m != null)
                {
                    if (AddFolder(sub, Categories.Atalhos, m.Conf, "Pasta do programa no Menu Iniciar", m.Warning)) proposed++;
                    continue;
                }
                if (depth < 3)
                {
                    int inner = ScanStartMenuDir(sub, depth + 1);
                    int total = FsUtil.Dirs(sub).Length + FsUtil.Files(sub).Count(f => !PathUtil.FileName(f).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase));
                    if (inner > 0 && inner >= total &&
                        AddFolder(sub, Categories.Atalhos, Confidence.Media, "Pasta do Menu Iniciar que ficará vazia", null, true))
                        proposed++;
                }
            }
            foreach (string f in FsUtil.Files(dir))
                if (IsShortcut(f) && CheckShortcut(f, false)) proposed++;
            return proposed;
        }

        private bool CheckShortcut(string file, bool isStartup)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            string target = ext == ".lnk" ? ShortcutResolver.GetTarget(file) : ReadUrlFile(file);
            string name = PathUtil.FileNameNoExt(file);
            bool added = false;

            if (!string.IsNullOrEmpty(target) && Refers(target))
                added = AddFile(file, Categories.Atalhos, RefConf, isStartup ? "Atalho de inicialização automática do programa" : "Atalho para o programa", RefWarning);
            else if (!string.IsNullOrEmpty(target) && ext == ".lnk")
            {
                string exeNorm;
                if (ExeMissing(target, out exeNorm) && (s.ExeNames.Contains(exeNorm) || MatchName(name, true) != null))
                    added = AddFile(file, Categories.Atalhos, Confidence.Media, "Atalho quebrado (o destino não existe mais)");
            }
            if (added && isStartup) startupLnkNames.Add(PathUtil.FileName(file));
            return added;
        }

        private static string ReadUrlFile(string file)
        {
            try
            {
                var parts = File.ReadAllLines(file)
                    .Where(l => l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase) || l.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase))
                    .Select(l => l.Substring(l.IndexOf('=') + 1));
                return string.Join("|", parts);
            }
            catch { return null; }
        }

        // ================================================================== temporários / cache

        private void ScanTemp()
        {
            var roots = new[] { Folders.Temp, winDir + @"\Temp" };
            foreach (string root in roots.Where(r => r != null).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                int count = 0;
                foreach (string entry in FsUtil.Dirs(root).Concat(FsUtil.Files(root)))
                {
                    if (++count > 20000) break;
                    bool isDir = Directory.Exists(entry);
                    string n = NameUtil.NormalizeName(isDir ? PathUtil.FileName(entry) : PathUtil.FileNameNoExt(entry));
                    if (n.Length < 5 || otherNames.Contains(n)) continue;
                    Confidence? c = null;
                    string reason = null;
                    if (s.StrongNames.Any(sn => sn.Length >= 5 && n.StartsWith(sn)))
                    {
                        c = Confidence.Media;
                        reason = "Temporário com o nome do programa";
                    }
                    else if (s.ExeNames.Any(en => en.Length >= 5 && n.StartsWith(en)))
                    {
                        c = Confidence.Baixa;
                        reason = "Temporário com o nome do executável do programa";
                    }
                    if (c == null) continue;
                    if (isDir) AddFolder(entry, Categories.Temporarios, c.Value, reason);
                    else AddFile(entry, Categories.Temporarios, c.Value, reason);
                }
            }
        }

        private void ScanPrefetchAndReports()
        {
            string prefetch = winDir + @"\Prefetch";
            foreach (string exe in s.ExeFileNames)
                foreach (string f in FsUtil.Files(prefetch, exe + ".EXE-*.pf"))
                    AddFile(f, Categories.Temporarios, Confidence.Alta, "Cache de inicialização (Prefetch) de " + exe + ".exe");

            // Relatórios de erro do Windows (WER) e despejos de memória dos executáveis do programa
            var werRoots = new[]
            {
                Folders.Sub(Folders.ProgramData, @"Microsoft\Windows\WER\ReportArchive"), Folders.Sub(Folders.ProgramData, @"Microsoft\Windows\WER\ReportQueue"),
                Folders.Sub(Folders.Local, @"Microsoft\Windows\WER\ReportArchive"), Folders.Sub(Folders.Local, @"Microsoft\Windows\WER\ReportQueue")
            };
            var werRx = new Regex(@"^(AppCrash|AppHang|Critical|NonCritical|BEX|BEX64)_(?<exe>[^_]+)_", RegexOptions.IgnoreCase);
            foreach (string root in werRoots)
            {
                foreach (string d in FsUtil.Dirs(root))
                {
                    Match m = werRx.Match(PathUtil.FileName(d));
                    if (!m.Success) continue;
                    string seg = m.Groups["exe"].Value;
                    bool hit = s.ExeFileNames.Any(e =>
                        string.Equals(e + ".exe", seg, StringComparison.OrdinalIgnoreCase) ||
                        (seg.Length >= 8 && (e + ".exe").StartsWith(seg, StringComparison.OrdinalIgnoreCase)));
                    if (hit) AddFolder(d, Categories.Temporarios, Confidence.Alta, "Relatório de erro do Windows (WER) do programa");
                }
            }
            string dumps = Folders.Sub(Folders.Local, "CrashDumps");
            foreach (string f in FsUtil.Files(dumps, "*.dmp"))
            {
                string fname = PathUtil.FileName(f);
                if (s.ExeFileNames.Any(e => fname.StartsWith(e + ".exe.", StringComparison.OrdinalIgnoreCase)))
                    AddFile(f, Categories.Temporarios, Confidence.Alta, "Despejo de memória (crash dump) do programa");
            }
        }

        // ================================================================== registro

        private void ScanUninstallEntry()
        {
            var p = s.Program;
            if (p.RegistrySubKey == null) return;
            AddRegKey(p.Hive, p.RegistrySubKey, Categories.Registro, Confidence.Alta, "Entrada de desinstalação (Programas e Recursos)");
        }

        private static readonly HashSet<string> SkipSoftware = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Classes", "Clients", "Policies", "WOW6432Node", "RegisteredApplications", "ODBC"
        };

        private void ScanSoftwareKeys()
        {
            foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            {
                foreach (string basePath in new[] { "SOFTWARE", @"SOFTWARE\WOW6432Node" })
                {
                    using (RegistryKey bk = RegUtil.Open(hive, basePath))
                    {
                        if (bk == null) continue;
                        foreach (string name in RegUtil.SubKeyNames(bk))
                        {
                            if (SkipSoftware.Contains(name)) continue;
                            string path = basePath + "\\" + name;
                            NameMatch m = MatchName(name, false);
                            if (m != null) { AddRegKey(hive, path, Categories.Registro, m.Conf, m.Reason, m.Warning); continue; }
                            if (IsPublisherName(name)) ScanPublisherKey(hive, path);
                        }
                    }
                }
            }
        }

        private void ScanPublisherKey(RegistryHive hive, string path)
        {
            string last = path.Substring(path.LastIndexOf('\\') + 1);
            bool generic = SafetyGuard.IsGeneric(NameUtil.NormalizeName(last));
            using (RegistryKey k = RegUtil.Open(hive, path))
            {
                if (k == null) return;
                string[] subs = RegUtil.SubKeyNames(k);
                int added = 0;
                foreach (string sub in subs)
                {
                    NameMatch m = CapForGenericPublisher(MatchName(sub, generic), generic);
                    if (m != null && AddRegKey(hive, path + "\\" + sub, Categories.Registro, m.Conf, m.Reason + " (dentro da chave do editor)", m.Warning))
                        added++;
                }
                int values = 0;
                try { values = k.ValueCount; } catch { }
                if (!generic && added > 0 && added == subs.Length && values == 0)
                    AddRegKey(hive, path, Categories.Registro, Confidence.Media, "Chave do editor que ficará vazia", null, true);
            }
        }

        private void ScanAppPaths()
        {
            foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                foreach (string basePath in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths" })
                {
                    using (RegistryKey root = RegUtil.Open(hive, basePath))
                    {
                        if (root == null) continue;
                        foreach (string sub in RegUtil.SubKeyNames(root))
                        {
                            using (RegistryKey k = RegUtil.OpenSub(root, sub))
                            {
                                if (k == null) continue;
                                string def = RegUtil.GetString(k, "");
                                string path = RegUtil.GetString(k, "Path");
                                if (Refers(def) || Refers(path))
                                {
                                    AddRegKey(hive, basePath + "\\" + sub, Categories.Registro, RefConf, "Caminho de aplicativo (App Paths)", RefWarning);
                                    continue;
                                }
                                string exeNorm;
                                string subNorm = NameUtil.NormalizeName(PathUtil.FileNameNoExt(sub));
                                if (s.ExeNames.Contains(subNorm) && ExeMissing(def, out exeNorm))
                                    AddRegKey(hive, basePath + "\\" + sub, Categories.Registro, Confidence.Media, "Caminho de aplicativo órfão (o executável não existe mais)");
                            }
                        }
                    }
                }
            }
        }

        private void ScanRunKeys()
        {
            const string cv = @"SOFTWARE\Microsoft\Windows\CurrentVersion";
            const string cvw = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion";
            var runKeys = new List<KeyValuePair<RegistryHive, string>>
            {
                new KeyValuePair<RegistryHive, string>(RegistryHive.CurrentUser, cv + @"\Run"),
                new KeyValuePair<RegistryHive, string>(RegistryHive.CurrentUser, cv + @"\RunOnce"),
                new KeyValuePair<RegistryHive, string>(RegistryHive.LocalMachine, cv + @"\Run"),
                new KeyValuePair<RegistryHive, string>(RegistryHive.LocalMachine, cv + @"\RunOnce"),
                new KeyValuePair<RegistryHive, string>(RegistryHive.LocalMachine, cvw + @"\Run"),
                new KeyValuePair<RegistryHive, string>(RegistryHive.LocalMachine, cvw + @"\RunOnce"),
                new KeyValuePair<RegistryHive, string>(RegistryHive.CurrentUser, cv + @"\Policies\Explorer\Run"),
                new KeyValuePair<RegistryHive, string>(RegistryHive.LocalMachine, cv + @"\Policies\Explorer\Run")
            };
            foreach (var rk in runKeys)
            {
                using (RegistryKey k = RegUtil.Open(rk.Key, rk.Value))
                {
                    if (k == null) continue;
                    foreach (string vn in RegUtil.ValueNames(k))
                    {
                        if (vn.Length == 0) continue;
                        string data = RegUtil.GetString(k, vn);
                        if (Refers(data))
                        {
                            if (AddRegValue(rk.Key, rk.Value, vn, Categories.Inicializacao, RefConf, "Inicia junto com o Windows", null, RefWarning))
                                runValueNames.Add(vn);
                            continue;
                        }
                        string exeNorm;
                        if (ExeMissing(data, out exeNorm) && (s.ExeNames.Contains(exeNorm) || MatchName(vn, true) != null))
                        {
                            if (AddRegValue(rk.Key, rk.Value, vn, Categories.Inicializacao, Confidence.Media, "Inicialização automática órfã (o executável não existe mais)"))
                                runValueNames.Add(vn);
                        }
                    }
                }
            }

            // Estado (habilitado/desabilitado) mostrado no Gerenciador de Tarefas
            foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            {
                foreach (string sub in new[] { "Run", "Run32", "StartupFolder" })
                {
                    string path = cv + @"\Explorer\StartupApproved\" + sub;
                    using (RegistryKey k = RegUtil.Open(hive, path))
                    {
                        if (k == null) continue;
                        foreach (string vn in RegUtil.ValueNames(k))
                        {
                            bool hit = sub == "StartupFolder" ? startupLnkNames.Contains(vn) : runValueNames.Contains(vn);
                            if (hit) AddRegValue(hive, path, vn, Categories.Inicializacao, Confidence.Alta, "Estado de inicialização no Gerenciador de Tarefas");
                        }
                    }
                }
            }
        }

        private static readonly string[] ShellContainers =
        {
            "*", "Directory", @"Directory\Background", "Folder", "Drive", "AllFilesystemObjects", "DesktopBackground",
            "LibraryFolder", @"LibraryFolder\background"
        };

        private static readonly string[] HandlerTypes =
        {
            "ContextMenuHandlers", "DragDropHandlers", "PropertySheetHandlers", "ColumnHandlers", "CopyHookHandlers"
        };

        private void ScanClasses()
        {
            var roots = new[]
            {
                new KeyValuePair<RegistryHive, string>(RegistryHive.LocalMachine, @"SOFTWARE\Classes"),
                new KeyValuePair<RegistryHive, string>(RegistryHive.CurrentUser, @"Software\Classes")
            };

            // Applications\programa.exe ("Abrir com")
            foreach (var root in roots)
            {
                foreach (string exe in s.ExeFileNames)
                {
                    string path = root.Value + @"\Applications\" + exe + ".exe";
                    using (RegistryKey k = RegUtil.Open(root.Key, path))
                    {
                        if (k == null) continue;
                        string cmd = RegUtil.DefaultOf(k, @"shell\open\command") ?? RegUtil.DefaultOf(k, @"shell\edit\command");
                        string exeNorm;
                        if (Refers(cmd)) AddRegKey(root.Key, path, Categories.Associacoes, RefConf, "Registro do programa na lista \"Abrir com\"", RefWarning);
                        else if (cmd != null && ExeMissing(cmd, out exeNorm))
                            AddRegKey(root.Key, path, Categories.Associacoes, Confidence.Media, "Registro \"Abrir com\" órfão (o executável não existe mais)");
                    }
                }
            }

            if (!CanRefer) return;
            var extKeys = new List<KeyValuePair<RegistryHive, string>>();

            foreach (var root in roots)
            {
                // ProgIDs e protocolos de URL
                using (RegistryKey ck = RegUtil.Open(root.Key, root.Value))
                {
                    if (ck != null)
                    {
                        foreach (string name in RegUtil.SubKeyNames(ck))
                        {
                            if (name.StartsWith(".")) { extKeys.Add(new KeyValuePair<RegistryHive, string>(root.Key, root.Value + "\\" + name)); continue; }
                            if (SafetyGuard.IsProtectedClassName(name)) continue;
                            using (RegistryKey k = RegUtil.OpenSub(ck, name))
                            {
                                if (k == null) continue;
                                string cmd = RegUtil.DefaultOf(k, @"shell\open\command");
                                string icon = RegUtil.DefaultOf(k, "DefaultIcon");
                                if (Refers(cmd) || Refers(icon))
                                {
                                    bool isProtocol = RegUtil.GetString(k, "URL Protocol") != null;
                                    if (AddRegKey(root.Key, root.Value + "\\" + name, Categories.Associacoes, RefConf,
                                        isProtocol ? "Protocolo de URL registrado pelo programa" : "Tipo de arquivo (ProgID) registrado pelo programa", RefWarning))
                                        foundProgIds.Add(name);
                                }
                            }
                        }
                    }
                }

                // Componentes COM
                foreach (string clsRoot in new[] { root.Value + @"\CLSID", root.Value + @"\WOW6432Node\CLSID" })
                {
                    using (RegistryKey ck = RegUtil.Open(root.Key, clsRoot))
                    {
                        if (ck == null) continue;
                        foreach (string guid in RegUtil.SubKeyNames(ck))
                        {
                            using (RegistryKey k = RegUtil.OpenSub(ck, guid))
                            {
                                if (k == null) continue;
                                if (Refers(RegUtil.DefaultOf(k, "InprocServer32")) || Refers(RegUtil.DefaultOf(k, "LocalServer32")) || Refers(RegUtil.DefaultOf(k, "DefaultIcon")))
                                {
                                    if (AddRegKey(root.Key, clsRoot + "\\" + guid, Categories.Associacoes, RefConf, "Componente COM registrado pelo programa", RefWarning))
                                        foundClsids.Add(guid);
                                }
                            }
                        }
                    }
                }

                // Menu de contexto do Explorer
                foreach (string container in ShellContainers)
                {
                    string shellPath = root.Value + "\\" + container + @"\shell";
                    using (RegistryKey sk = RegUtil.Open(root.Key, shellPath))
                    {
                        foreach (string verb in RegUtil.SubKeyNames(sk))
                        {
                            using (RegistryKey vk = RegUtil.OpenSub(sk, verb))
                            {
                                if (vk == null) continue;
                                if (Refers(RegUtil.DefaultOf(vk, "command")) || Refers(RegUtil.GetString(vk, "Icon")))
                                    AddRegKey(root.Key, shellPath + "\\" + verb, Categories.Associacoes, RefConf, "Item do menu de contexto do Explorer", RefWarning);
                            }
                        }
                    }
                    foreach (string ht in HandlerTypes)
                    {
                        string hp = root.Value + "\\" + container + @"\shellex\" + ht;
                        using (RegistryKey hk = RegUtil.Open(root.Key, hp))
                        {
                            foreach (string h in RegUtil.SubKeyNames(hk))
                            {
                                string clsid;
                                using (RegistryKey k = RegUtil.OpenSub(hk, h)) clsid = RegUtil.GetString(k, "");
                                if (foundClsids.Contains(h) || (clsid != null && foundClsids.Contains(clsid.Trim())))
                                    AddRegKey(root.Key, hp + "\\" + h, Categories.Associacoes, RefConf, "Extensão do menu de contexto do Explorer", RefWarning);
                            }
                        }
                    }
                }
            }

            // Ícones sobrepostos e extensões do Shell aprovadas
            foreach (string basePath in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion" })
            {
                string overlays = basePath + @"\Explorer\ShellIconOverlayIdentifiers";
                using (RegistryKey ok = RegUtil.Open(RegistryHive.LocalMachine, overlays))
                {
                    foreach (string n in RegUtil.SubKeyNames(ok))
                    {
                        string clsid;
                        using (RegistryKey k = RegUtil.OpenSub(ok, n)) clsid = RegUtil.GetString(k, "");
                        if (clsid != null && foundClsids.Contains(clsid.Trim()))
                            AddRegKey(RegistryHive.LocalMachine, overlays + "\\" + n, Categories.Associacoes, RefConf, "Ícone sobreposto do Explorer", RefWarning);
                    }
                }
                string approved = basePath + @"\Shell Extensions\Approved";
                using (RegistryKey ak = RegUtil.Open(RegistryHive.LocalMachine, approved))
                {
                    foreach (string vn in RegUtil.ValueNames(ak))
                        if (foundClsids.Contains(vn))
                            AddRegValue(RegistryHive.LocalMachine, approved, vn, Categories.Associacoes, RefConf, "Extensão do Shell aprovada", null, RefWarning);
                }
            }

            // "Abrir com" de cada extensão apontando para ProgIDs removidos
            if (foundProgIds.Count > 0)
            {
                foreach (var ext in extKeys) CheckOpenWithProgids(ext.Key, ext.Value + @"\OpenWithProgids", PathUtil.FileName(ext.Value));
                const string fileExts = @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts";
                using (RegistryKey fe = RegUtil.Open(RegistryHive.CurrentUser, fileExts))
                {
                    foreach (string ext in RegUtil.SubKeyNames(fe))
                        CheckOpenWithProgids(RegistryHive.CurrentUser, fileExts + "\\" + ext + @"\OpenWithProgids", ext);
                }
            }
        }

        private void CheckOpenWithProgids(RegistryHive hive, string path, string ext)
        {
            using (RegistryKey k = RegUtil.Open(hive, path))
            {
                if (k == null) return;
                foreach (string vn in RegUtil.ValueNames(k))
                    if (foundProgIds.Contains(vn))
                        AddRegValue(hive, path, vn, Categories.Associacoes, RefConf, "Opção \"Abrir com\" para arquivos " + ext, null, RefWarning);
            }
        }

        private void ScanClients()
        {
            if (!CanRefer) return;
            foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                foreach (string basePath in new[] { @"SOFTWARE\Clients", @"SOFTWARE\WOW6432Node\Clients" })
                {
                    using (RegistryKey bk = RegUtil.Open(hive, basePath))
                    {
                        foreach (string type in RegUtil.SubKeyNames(bk))
                        {
                            using (RegistryKey tk = RegUtil.OpenSub(bk, type))
                            {
                                foreach (string client in RegUtil.SubKeyNames(tk))
                                {
                                    using (RegistryKey k = RegUtil.OpenSub(tk, client))
                                    {
                                        if (k == null) continue;
                                        string reinstall = null;
                                        using (RegistryKey ii = RegUtil.OpenSub(k, "InstallInfo")) reinstall = RegUtil.GetString(ii, "ReinstallCommand");
                                        if (Refers(RegUtil.DefaultOf(k, @"shell\open\command")) || Refers(RegUtil.DefaultOf(k, "DefaultIcon")) || Refers(reinstall))
                                            AddRegKey(hive, basePath + "\\" + type + "\\" + client, Categories.Associacoes, RefConf, "Registro como aplicativo padrão (" + type + ")", RefWarning);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private void ScanRegisteredApplications()
        {
            foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                foreach (string basePath in new[] { @"SOFTWARE\RegisteredApplications", @"SOFTWARE\WOW6432Node\RegisteredApplications" })
                {
                    using (RegistryKey k = RegUtil.Open(hive, basePath))
                    {
                        if (k == null) continue;
                        foreach (string vn in RegUtil.ValueNames(k))
                        {
                            string data = RegUtil.GetString(k, vn);
                            if (string.IsNullOrEmpty(data)) continue;
                            string cap = data.Trim('\\');
                            RegistryHive h = hive;
                            bool covered = items.Values.Any(it => it.Type == LeftoverType.RegistryKey && it.Hive == h && !it.DeleteOnlyIfEmpty &&
                                (string.Equals(it.Path, cap, StringComparison.OrdinalIgnoreCase) || RegUtil.IsStrictlyUnderKey(cap, it.Path)));
                            if (covered)
                                AddRegValue(hive, basePath, vn, Categories.Associacoes, Confidence.Alta, "Registro em \"Aplicativos padrão\" do Windows");
                            else if (!RegUtil.KeyExists(hive, cap) && MatchName(vn, true) != null)
                                AddRegValue(hive, basePath, vn, Categories.Associacoes, Confidence.Media, "Registro em \"Aplicativos padrão\" órfão");
                        }
                    }
                }
            }
        }

        // ================================================================== serviços, tarefas, firewall, ambiente

        private string FixImagePath(string img)
        {
            if (string.IsNullOrEmpty(img)) return img;
            string t = img.Trim();
            if (t.StartsWith(@"\??\")) t = t.Substring(4);
            if (t.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase)) t = winDir + t.Substring(11);
            else if (t.StartsWith(@"System32\", StringComparison.OrdinalIgnoreCase) || t.StartsWith(@"SysWOW64\", StringComparison.OrdinalIgnoreCase)) t = winDir + "\\" + t;
            return t;
        }

        private void ScanServices()
        {
            const string root = @"SYSTEM\CurrentControlSet\Services";
            using (RegistryKey svcs = RegUtil.Open(RegistryHive.LocalMachine, root))
            {
                if (svcs == null) return;
                foreach (string name in RegUtil.SubKeyNames(svcs))
                {
                    using (RegistryKey k = RegUtil.OpenSub(svcs, name))
                    {
                        string img = RegUtil.GetString(k, "ImagePath");
                        if (string.IsNullOrEmpty(img)) continue;
                        string full = FixImagePath(img);
                        Confidence? conf = null;
                        string reason = null;
                        if (Refers(full)) { conf = RefConf; reason = "Serviço instalado pelo programa"; }
                        else
                        {
                            string dn = RegUtil.GetString(k, "DisplayName");
                            bool nameHit = s.StrongNames.Contains(NameUtil.NormalizeName(name)) ||
                                           (dn != null && !dn.StartsWith("@") && s.StrongNames.Contains(NameUtil.NormalizeName(dn)));
                            string exeNorm;
                            if (nameHit && ExeMissing(full, out exeNorm)) { conf = Confidence.Media; reason = "Serviço órfão com o nome do programa (o executável não existe mais)"; }
                        }
                        if (conf == null) continue;
                        string why;
                        if (!SafetyGuard.CanDeleteRegistryKey(RegistryHive.LocalMachine, root + "\\" + name, out why)) continue;
                        Put(new LeftoverItem
                        {
                            Type = LeftoverType.Service, Category = Categories.Servicos, Hive = RegistryHive.LocalMachine,
                            Path = name, ServiceImage = img, Display = "Serviço \"" + name + "\"  —  " + img,
                            Confidence = conf.Value, Reason = reason, Warning = conf == RefConf ? RefWarning : null
                        });
                    }
                }
            }
        }

        private void ScanTasks()
        {
            string tasksDir = Path.Combine(Environment.SystemDirectory, "Tasks");
            if (!Directory.Exists(tasksDir)) return;
            var cmdRx = new Regex("<Command>(.*?)</Command>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var stack = new Stack<string>();
            stack.Push(tasksDir);
            while (stack.Count > 0)
            {
                string dir = stack.Pop();
                foreach (string sub in FsUtil.Dirs(dir))
                {
                    if (string.Equals(sub, tasksDir + @"\Microsoft", StringComparison.OrdinalIgnoreCase)) continue; // tarefas do Windows
                    stack.Push(sub);
                }
                foreach (string file in FsUtil.Files(dir))
                {
                    string text;
                    try { text = File.ReadAllText(file); } catch { continue; }
                    text = text.Replace("&quot;", "\"").Replace("&amp;", "&");
                    string rel = file.Substring(tasksDir.Length);
                    string name = PathUtil.FileName(file);
                    Confidence? conf = null;
                    string reason = null;
                    if (Refers(text)) { conf = RefConf; reason = "Tarefa agendada que executa o programa"; }
                    else
                    {
                        Match cm = cmdRx.Match(text);
                        string exeNorm;
                        if (cm.Success && ExeMissing(cm.Groups[1].Value.Trim(), out exeNorm) &&
                            (s.ExeNames.Contains(exeNorm) || MatchName(name, true) != null || s.StrongNames.Any(sn => sn.Length >= 5 && NameUtil.NormalizeName(name).StartsWith(sn))))
                        {
                            conf = Confidence.Media;
                            reason = "Tarefa agendada órfã (o executável não existe mais)";
                        }
                    }
                    if (conf == null) continue;
                    Put(new LeftoverItem
                    {
                        Type = LeftoverType.ScheduledTask, Category = Categories.Tarefas, Path = rel, TaskFile = file,
                        Display = "Tarefa \"" + rel + "\"", Confidence = conf.Value, Reason = reason
                    });
                }
            }
        }

        private void ScanFirewall()
        {
            const string path = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules";
            var appRx = new Regex(@"\|App=([^|]*)", RegexOptions.IgnoreCase);
            var nameRx = new Regex(@"\|Name=([^|]*)", RegexOptions.IgnoreCase);
            using (RegistryKey k = RegUtil.Open(RegistryHive.LocalMachine, path))
            {
                if (k == null) return;
                foreach (string vn in RegUtil.ValueNames(k))
                {
                    string data = RegUtil.GetString(k, vn);
                    if (string.IsNullOrEmpty(data)) continue;
                    Match am = appRx.Match(data);
                    string app = am.Success ? am.Groups[1].Value : "";
                    string ruleName = nameRx.Match(data).Groups[1].Value;
                    bool storeHit = !string.IsNullOrEmpty(s.PackageFamilyName) && data.IndexOf(s.PackageFamilyName, StringComparison.OrdinalIgnoreCase) >= 0;
                    string exeNorm;
                    if ((app.Length > 0 && Refers(app)) || storeHit)
                        AddRegValue(RegistryHive.LocalMachine, path, vn, Categories.Firewall, storeHit ? Confidence.Alta : RefConf,
                            "Regra do Firewall do Windows para o programa", "Regra de firewall \"" + ruleName + "\"  —  " + app, storeHit ? null : RefWarning);
                    else if (app.Length > 0 && ExeMissing(app, out exeNorm) && s.ExeNames.Contains(exeNorm))
                        AddRegValue(RegistryHive.LocalMachine, path, vn, Categories.Firewall, Confidence.Media,
                            "Regra de firewall órfã (o executável não existe mais)", "Regra de firewall \"" + ruleName + "\"  —  " + app);
                }
            }
        }

        private void ScanEnvironment()
        {
            if (s.InstallDir == null && s.ExtraDirs.Count == 0) return;
            var envKeys = new[]
            {
                new KeyValuePair<RegistryHive, string>(RegistryHive.CurrentUser, "Environment"),
                new KeyValuePair<RegistryHive, string>(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment")
            };
            foreach (var ek in envKeys)
            {
                string scope = ek.Key == RegistryHive.CurrentUser ? "usuário" : "sistema";
                using (RegistryKey k = RegUtil.Open(ek.Key, ek.Value))
                {
                    if (k == null) continue;
                    foreach (string vn in RegUtil.ValueNames(k))
                    {
                        string raw = RegUtil.GetRawString(k, vn);
                        if (string.IsNullOrEmpty(raw)) continue;
                        bool isList = raw.IndexOf(';') >= 0 || vn.Equals("Path", StringComparison.OrdinalIgnoreCase) || vn.Equals("PSModulePath", StringComparison.OrdinalIgnoreCase);
                        if (isList)
                        {
                            foreach (string part in raw.Split(';'))
                            {
                                if (part.Trim().Length == 0) continue;
                                if (RefersPath(PathUtil.Normalize(part)))
                                {
                                    Put(new LeftoverItem
                                    {
                                        Type = LeftoverType.PathEntry, Category = Categories.Ambiente, Hive = ek.Key, Path = ek.Value,
                                        ValueName = vn, Data = part,
                                        Display = "Variável " + vn + " (" + scope + "): remover a entrada \"" + part + "\"",
                                        Confidence = RefConf, Reason = "Entrada da variável de ambiente que aponta para o programa", Warning = RefWarning
                                    });
                                }
                            }
                        }
                        else if (RefersPath(PathUtil.Normalize(raw)))
                        {
                            AddRegValue(ek.Key, ek.Value, vn, Categories.Ambiente, RefConf, "Variável de ambiente que aponta para o programa",
                                "Variável " + vn + " (" + scope + ") = " + raw, RefWarning);
                        }
                    }
                }
            }
        }

        private void ScanValueTraces()
        {
            const string cv = @"Software\Microsoft\Windows\CurrentVersion";
            var keys = new List<KeyValuePair<RegistryHive, string>>
            {
                new KeyValuePair<RegistryHive, string>(RegistryHive.CurrentUser, @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache"),
                new KeyValuePair<RegistryHive, string>(RegistryHive.CurrentUser, @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Compatibility Assistant\Store"),
                new KeyValuePair<RegistryHive, string>(RegistryHive.CurrentUser, @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers"),
                new KeyValuePair<RegistryHive, string>(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers"),
                new KeyValuePair<RegistryHive, string>(RegistryHive.CurrentUser, cv + @"\Explorer\FeatureUsage\AppSwitched"),
                new KeyValuePair<RegistryHive, string>(RegistryHive.CurrentUser, cv + @"\Explorer\FeatureUsage\ShowJumpView"),
                new KeyValuePair<RegistryHive, string>(RegistryHive.CurrentUser, cv + @"\Explorer\FeatureUsage\AppBadgeUpdated"),
                new KeyValuePair<RegistryHive, string>(RegistryHive.CurrentUser, cv + @"\Explorer\FeatureUsage\AppLaunch"),
                new KeyValuePair<RegistryHive, string>(RegistryHive.CurrentUser, cv + @"\Search\JumplistData"),
                new KeyValuePair<RegistryHive, string>(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\Folders"),
                new KeyValuePair<RegistryHive, string>(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs"),
                new KeyValuePair<RegistryHive, string>(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\SharedDLLs")
            };
            if (!CanRefer) return;
            foreach (var kv in keys)
            {
                using (RegistryKey k = RegUtil.Open(kv.Key, kv.Value))
                {
                    if (k == null) continue;
                    foreach (string vn in RegUtil.ValueNames(k))
                    {
                        if (vn.Length == 0) continue;
                        bool hit = Refers(vn);
                        if (!hit)
                        {
                            string data = RegUtil.GetString(k, vn);
                            hit = data != null && data.Length < 4096 && Refers(data);
                        }
                        if (hit) AddRegValue(kv.Key, kv.Value, vn, Categories.Rastros, RefConf, "Rastro deixado pelo Windows (uso, compatibilidade ou instalador)", null, RefWarning);
                    }
                }
            }
        }

        // ================================================================== finalização

        private static bool Covers(LeftoverItem parent, LeftoverItem child)
        {
            if (parent.Type == LeftoverType.Folder && (child.Type == LeftoverType.Folder || child.Type == LeftoverType.File))
                return PathUtil.IsStrictlyUnder(child.Path, parent.Path);
            if (parent.Type == LeftoverType.RegistryKey && child.Hive == parent.Hive)
            {
                if (child.Type == LeftoverType.RegistryKey) return RegUtil.IsStrictlyUnderKey(child.Path, parent.Path);
                if (child.Type == LeftoverType.RegistryValue)
                    return string.Equals(child.Path, parent.Path, StringComparison.OrdinalIgnoreCase) || RegUtil.IsStrictlyUnderKey(child.Path, parent.Path);
            }
            return false;
        }

        private List<LeftoverItem> FinalizeList()
        {
            List<LeftoverItem> all = items.Values.ToList();
            var result = new List<LeftoverItem>();
            foreach (LeftoverItem a in all)
            {
                bool covered = all.Any(b => !ReferenceEquals(a, b) && !b.DeleteOnlyIfEmpty && b.Confidence <= a.Confidence && Covers(b, a));
                if (!covered) result.Add(a);
            }
            if (mayStillBeInstalled)
            {
                foreach (LeftoverItem it in result)
                {
                    if (it.Confidence == Confidence.Alta)
                    {
                        it.Confidence = Confidence.Media;
                        it.Warning = Join(it.Warning, "O programa ainda parece estar instalado");
                    }
                }
            }
            result.Sort((x, y) =>
            {
                int c = Categories.IndexOf(x.Category).CompareTo(Categories.IndexOf(y.Category));
                if (c != 0) return c;
                c = x.Confidence.CompareTo(y.Confidence);
                if (c != 0) return c;
                return string.Compare(x.Display, y.Display, StringComparison.OrdinalIgnoreCase);
            });
            log("Varredura concluída: " + result.Count + " item(ns) encontrado(s).");
            return result;
        }
    }
}
