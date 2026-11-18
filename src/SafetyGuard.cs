using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DesinstaladorCompleto
{
    /// <summary>
    /// Regras de segurança: decide o que NUNCA pode ser excluído, independentemente do que a varredura encontrar.
    /// Toda exclusão passa por aqui duas vezes (na varredura e no momento de excluir).
    /// </summary>
    internal static class SafetyGuard
    {
        private static readonly List<string> protectedDirs = new List<string>();
        private static readonly List<string> userContentDirs = new List<string>();
        private static readonly List<string> desktopDirs = new List<string>();
        private static string winDir, winTemp, prefetchDir, appDir;

        /// <summary>Pastas ocultas do perfil do usuário que nunca são removidas (chaves SSH, repositórios, caches compartilhados...).</summary>
        public static readonly HashSet<string> DeniedDotFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".ssh", ".gnupg", ".git", ".svn", ".hg", ".config", ".local", ".cache", ".aws", ".azure", ".kube",
            ".m2", ".gradle", ".nuget", ".npm", ".yarn", ".pnpm-store", ".cargo", ".rustup", ".conda", ".ipython",
            ".jupyter", ".dotnet", ".vs", ".templateengine", ".ms-ad"
        };

        private static readonly HashSet<string> genericTokens = new HashSet<string>(StringComparer.Ordinal)
        {
            "microsoft", "windows", "windowsnt", "microsoftwindows", "common", "commonfiles", "programfiles", "programfilesx86",
            "program", "programs", "files", "file", "system", "system32", "syswow64", "sysnative", "temp", "tmp", "cache",
            "caches", "data", "appdata", "local", "roaming", "locallow", "user", "users", "default", "defaultuser", "public",
            "packages", "package", "update", "updates", "updater", "setup", "install", "installer", "installers",
            "installation", "uninstall", "uninstaller", "app", "apps", "application", "applications", "software", "classes",
            "clsid", "tools", "tool", "utilities", "utility", "driver", "drivers", "service", "services", "runtime",
            "runtimes", "shared", "sharedtools", "bin", "lib", "libs", "config", "configs", "configuration", "logs", "log",
            "backup", "backups", "help", "docs", "documentation", "crashreports", "crashdumps", "crashpad", "diagnostics",
            "telemetry", "settings", "preferences", "profiles", "profile", "plugins", "plugin", "extensions", "extension",
            "addins", "addons", "fonts", "icons", "images", "resources", "assets", "themes", "languages", "language",
            "locale", "locales", "sdk", "sdks", "redist", "redistributable", "vcredist", "framework", "frameworks",
            "netframework", "net", "dotnet", "desktop", "documents", "downloads", "pictures", "music", "videos",
            "favorites", "startmenu", "startup", "menu", "links", "temporary", "history", "cookies", "inetcache",
            "internetexplorer", "x64", "x86", "win32", "win64", "amd64", "games", "game", "launcher", "client", "server",
            "host", "agent", "helper", "manager", "monitor", "core", "main", "base", "bundle", "components", "component",
            "modules", "module", "scripts", "script", "packagecache", "installshield", "installshieldinstallationinformation",
            "wix", "msi", "msiexec", "inno", "nsis", "squirrel", "electron", "java", "oem", "intel", "vendor", "new",
            "old", "test", "tests", "sample", "samples", "source", "src", "projects", "project", "repos", "workspace"
        };

        private static readonly HashSet<string> protectedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> protectedClassNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "*", "Directory", "Folder", "Drive", "AllFilesystemObjects", "CLSID", "Interface", "TypeLib", "AppID",
            "Applications", "SystemFileAssociations", "Local Settings", "WOW6432Node", "Unknown", "exefile", "lnkfile",
            "batfile", "cmdfile", "comfile", "regfile", "txtfile", "htmlfile", "http", "https", "ftp", "mailto", "file",
            "InternetShortcut", "ms-settings", "DesktopBackground", "Network", "Printers", "PackagedCom", "Extensions",
            "MIME", "Component Categories", "Installer", "Msi.Package", "Msi.Patch", "CompressedFolder", "ActivatableClasses",
            "AudioCD", "DVD", "Microsoft.PowerShellScript.1", "VBSFile", "JSFile", "WSFFile", "dllfile", "sysfile",
            "inffile", "ocxfile", "scrfile", "piffile", "mscfile", "cplfile", "Shell.Application", "LibraryFolder",
            "DirectShow", "Media Type", "MediaFoundation", "Filter", "Protocols", "PROTOCOLS", "Theme.Theme",
            "Microsoft.Website", "jpegfile", "pngfile", "giffile", "zipfile", "SearchFolder", "Stack", "UserLibraryFolder"
        };

        private static readonly HashSet<string> microsoftSystemKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Windows", "Windows NT", "Cryptography", ".NETFramework", "NET Framework Setup", "Active Setup",
            "Windows Defender", "Internet Explorer", "Ole", "Rpc", "COM3", "WBEM", "SystemCertificates", "Security Center",
            "Windows Search", "Speech", "Speech_OneCore", "CTF", "Input", "Fusion", "PolicyManager", "Windows Script Host",
            "WindowsUpdate", "DirectX", "Direct3D", "DirectDraw", "DirectInput", "DirectPlay", "DirectShow", "Multimedia",
            "Notepad", "Shell", "Tracing", "Wisp", "WindowsRuntime", "Windows Error Reporting", "Windows Portable Devices",
            "Windows Photo Viewer", "Windows Mail", "MediaPlayer", "Ime", "InputMethod", "Keyboard", "MMC", "MSDTC",
            "MSLicensing", "Netsh", "Advanced INF Setup", "Assistance", "Command Processor", "DataAccess",
            "DeviceManagement", "DRM", "ESENT", "EventSystem", "Feeds", "IdentityCRL", "Lsa", "Mobile", "OOBE", "Osk",
            "PCHealth", "Print", "Provisioning", "Reliability Analysis", "RemovalTools", "SchedulingAgent", "Sensors",
            "Shared Tools", "Shared", "Software Protection", "SQMClient", "StrongName", "Sysprep", "SystemSettings",
            "TabletTip", "TaskScheduler", "Terminal Server Client", "Tpm", "Transaction Server", "Updates", "UserManager",
            "WAB", "Windows Advanced Threat Protection", "WindowsSelfHost", "WindowsStore", "XAML", "Office"
        };

        private static readonly HashSet<string> protectedEnvVars = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Path", "PATHEXT", "PSModulePath", "windir", "ComSpec", "TEMP", "TMP", "OS", "NUMBER_OF_PROCESSORS",
            "PROCESSOR_ARCHITECTURE", "PROCESSOR_IDENTIFIER", "PROCESSOR_LEVEL", "PROCESSOR_REVISION", "DriverData",
            "USERNAME", "OneDrive", "OneDriveConsumer", "OneDriveCommercial"
        };

        private static readonly Regex ClassesRx = new Regex(@"^SOFTWARE\\Classes\\(?<rest>.+)$", RegexOptions.IgnoreCase);
        private static readonly Regex GenericSoftwareRx = new Regex(@"^SOFTWARE(\\WOW6432Node)?\\(?<x>[^\\]+)(\\(?<y>[^\\]+))?$", RegexOptions.IgnoreCase);
        private const string ShellContainers = @"(\*|Directory|Directory\\Background|Folder|Drive|AllFilesystemObjects|DesktopBackground|LibraryFolder|LibraryFolder\\background)";

        private static readonly Regex[] allowedClassPatterns =
        {
            new Regex(@"^Applications\\[^\\]+$", RegexOptions.IgnoreCase),
            new Regex(@"^(WOW6432Node\\)?CLSID\\\{[0-9A-Fa-f-]+\}$", RegexOptions.IgnoreCase),
            new Regex("^" + ShellContainers + @"\\shell\\[^\\]+$", RegexOptions.IgnoreCase),
            new Regex("^" + ShellContainers + @"\\shellex\\[A-Za-z]+Handlers\\[^\\]+$", RegexOptions.IgnoreCase),
        };

        private static readonly Regex[] otherAllowedKeys =
        {
            new Regex(@"^SOFTWARE(\\WOW6432Node)?\\Microsoft\\Windows\\CurrentVersion\\(Uninstall|App Paths)\\[^\\]+$", RegexOptions.IgnoreCase),
            new Regex(@"^SOFTWARE(\\WOW6432Node)?\\Microsoft\\Windows\\CurrentVersion\\Explorer\\ShellIconOverlayIdentifiers\\[^\\]+$", RegexOptions.IgnoreCase),
            new Regex(@"^SOFTWARE\\WOW6432Node\\Classes\\CLSID\\\{[0-9A-Fa-f-]+\}$", RegexOptions.IgnoreCase),
            new Regex(@"^SOFTWARE(\\WOW6432Node)?\\Clients\\[^\\]+\\[^\\]+$", RegexOptions.IgnoreCase),
            new Regex(@"^SYSTEM\\CurrentControlSet\\Services\\[^\\]+$", RegexOptions.IgnoreCase),
        };

        private static readonly HashSet<string> deniedSoftwareRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Classes", "Policies", "Clients", "RegisteredApplications", "WOW6432Node", "ODBC"
        };

        static SafetyGuard()
        {
            InitDirs();
            InitKeys();
        }

        private static void P(string path)
        {
            string p = PathUtil.Normalize(path);
            if (p != null && !protectedDirs.Contains(p, StringComparer.OrdinalIgnoreCase)) protectedDirs.Add(p);
        }

        private static void U(string path)
        {
            string p = PathUtil.Normalize(path);
            if (p != null && !userContentDirs.Contains(p, StringComparer.OrdinalIgnoreCase)) userContentDirs.Add(p);
        }

        private static void InitDirs()
        {
            winDir = Folders.WinDir;
            winTemp = winDir + @"\Temp";
            prefetchDir = winDir + @"\Prefetch";
            appDir = PathUtil.Normalize(AppDomain.CurrentDomain.BaseDirectory);

            foreach (Environment.SpecialFolder f in Enum.GetValues(typeof(Environment.SpecialFolder))) P(Folders.SF(f));

            string[] envs =
            {
                "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "CommonProgramFiles", "CommonProgramFiles(x86)",
                "CommonProgramW6432", "PUBLIC", "ALLUSERSPROFILE", "ProgramData", "SystemRoot", "windir", "USERPROFILE",
                "APPDATA", "LOCALAPPDATA", "TEMP", "TMP", "OneDrive", "OneDriveConsumer", "OneDriveCommercial", "HOMEPATH"
            };
            foreach (string e in envs) P(Folders.Env(e));

            string profile = Folders.Profile;
            string local = Folders.Local;
            string roaming = Folders.Roaming;
            string programData = Folders.ProgramData;

            string[] profileSubs =
            {
                "Downloads", "source", @"source\repos", "Saved Games", "Contacts", "Links", "Searches", "3D Objects",
                "Favorites", "Documents", "Desktop", "Pictures", "Music", "Videos", "AppData", @"AppData\LocalLow",
                "Dropbox", "Google Drive", "iCloudDrive", "OneDrive", "repos", "projects", "Projetos", "dev", "workspace",
                "git", "Meus Documentos"
            };
            foreach (string s in profileSubs) P(Folders.Sub(profile, s));

            string[] localSubs =
            {
                "Programs", @"Programs\Common", "Packages", "Microsoft", @"Microsoft\Windows", @"Microsoft\WindowsApps",
                "Temp", "VirtualStore", "CrashDumps", "Package Cache", @"Microsoft\Windows\WER", @"Microsoft\Windows\WER\ReportArchive",
                @"Microsoft\Windows\WER\ReportQueue", "D3DSCache", "PackageManagement"
            };
            foreach (string s in localSubs) P(Folders.Sub(local, s));

            string[] roamingSubs =
            {
                "Microsoft", @"Microsoft\Windows", @"Microsoft\Windows\Start Menu", @"Microsoft\Windows\Start Menu\Programs",
                @"Microsoft\Windows\Start Menu\Programs\Startup", @"Microsoft\Internet Explorer",
                @"Microsoft\Internet Explorer\Quick Launch", @"Microsoft\Internet Explorer\Quick Launch\User Pinned",
                @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar",
                @"Microsoft\Internet Explorer\Quick Launch\User Pinned\StartMenu",
                @"Microsoft\Internet Explorer\Quick Launch\User Pinned\ImplicitAppShortcuts"
            };
            foreach (string s in roamingSubs) P(Folders.Sub(roaming, s));

            string[] programDataSubs =
            {
                "Microsoft", @"Microsoft\Windows", @"Microsoft\Windows\Start Menu", @"Microsoft\Windows\Start Menu\Programs",
                @"Microsoft\Windows\Start Menu\Programs\Startup", "Package Cache", "Packages", "Desktop", "Documents",
                @"Microsoft\Windows\WER", @"Microsoft\Windows\WER\ReportArchive", @"Microsoft\Windows\WER\ReportQueue"
            };
            foreach (string s in programDataSubs) P(Folders.Sub(programData, s));

            // Pastas de sistema dentro do Menu Iniciar
            string[] startMenuSystem =
            {
                "Administrative Tools", "Accessibility", "Accessories", "System Tools", "Windows PowerShell", "Maintenance",
                "Windows Administrative Tools", "Windows Accessories", "Windows Ease of Access", "Windows System", "Startup",
                "Windows Tools", "Ferramentas Administrativas", "Acessórios", "Acessibilidade", "Inicializar"
            };
            foreach (string menu in new[] { Folders.SF(Environment.SpecialFolder.Programs), Folders.SF(Environment.SpecialFolder.CommonPrograms) })
                foreach (string s in startMenuSystem) P(Folders.Sub(menu, s));

            // Subpastas do Windows dentro de Arquivos de Programas
            string[] pfSubs =
            {
                "WindowsApps", "ModifiableWindowsApps", "Common Files", "Windows Defender", "Windows Defender Advanced Threat Protection",
                "Windows NT", "Internet Explorer", "Windows Mail", "Windows Media Player", "Windows Photo Viewer",
                "Windows Sidebar", "WindowsPowerShell", "Windows Security", "Microsoft.NET", "Reference Assemblies",
                "MSBuild", "Uninstall Information", "InstallShield Installation Information"
            };
            foreach (string pf in new[] { Folders.SF(Environment.SpecialFolder.ProgramFiles), Folders.SF(Environment.SpecialFolder.ProgramFilesX86), Folders.Env("ProgramW6432") })
                foreach (string s in pfSubs) P(Folders.Sub(pf, s));

            // Pastas na raiz de cada unidade
            try
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    string root = d.Name;
                    foreach (string s in new[] { "$Recycle.Bin", "System Volume Information", "Recovery", "Boot", "EFI", "Users", "Windows", "Windows.old", "PerfLogs", "ProgramData", "Program Files", "Program Files (x86)", "Config.Msi", "$WinREAgent", "$Windows.~BT", "$Windows.~WS" })
                        P(Folders.Sub(root, s));
                }
            }
            catch { }

            // Perfis de todos os usuários
            string usersRoot = PathUtil.Parent(profile);
            P(usersRoot);
            foreach (string up in FsUtil.Dirs(usersRoot)) P(up);

            P(winTemp);
            P(prefetchDir);
            P(appDir);

            // Áreas de dados pessoais: nada aqui dentro é excluído (exceto atalhos soltos na Área de Trabalho)
            Environment.SpecialFolder[] content =
            {
                Environment.SpecialFolder.Desktop, Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.CommonDesktopDirectory,
                Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.CommonDocuments, Environment.SpecialFolder.MyPictures,
                Environment.SpecialFolder.MyMusic, Environment.SpecialFolder.MyVideos, Environment.SpecialFolder.CommonPictures,
                Environment.SpecialFolder.CommonMusic, Environment.SpecialFolder.CommonVideos, Environment.SpecialFolder.Favorites
            };
            foreach (var f in content) U(Folders.SF(f));
            foreach (string s in new[] { "Downloads", "source", "Saved Games", "Contacts", "Links", "Searches", "3D Objects", "Dropbox", "Google Drive", "iCloudDrive", "OneDrive", "repos", "projects", "Projetos", "workspace" })
                U(Folders.Sub(profile, s));
            foreach (string e in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" }) U(Folders.Env(e));

            foreach (var f in new[] { Environment.SpecialFolder.Desktop, Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.CommonDesktopDirectory })
            {
                string d = Folders.SF(f);
                if (d != null && !desktopDirs.Contains(d, StringComparer.OrdinalIgnoreCase)) desktopDirs.Add(d);
            }
        }

        private static void InitKeys()
        {
            string[] keys =
            {
                "SOFTWARE", @"SOFTWARE\WOW6432Node", @"SOFTWARE\Classes", @"SOFTWARE\Classes\WOW6432Node",
                @"SOFTWARE\WOW6432Node\Classes", @"SOFTWARE\Policies", @"SOFTWARE\WOW6432Node\Policies", @"SOFTWARE\Clients",
                @"SOFTWARE\WOW6432Node\Clients", @"SOFTWARE\RegisteredApplications", @"SOFTWARE\WOW6432Node\RegisteredApplications",
                @"SOFTWARE\ODBC", @"SOFTWARE\Classes\CLSID", @"SOFTWARE\Classes\WOW6432Node\CLSID", @"SOFTWARE\Classes\Applications",
                "SYSTEM", @"SYSTEM\CurrentControlSet", @"SYSTEM\CurrentControlSet\Services", @"SYSTEM\CurrentControlSet\Control",
                @"SYSTEM\ControlSet001", @"SYSTEM\Setup", "SAM", "SECURITY", "HARDWARE", "BCD00000000", "Environment",
                "Control Panel", "Console", "Keyboard Layout", "Network", "Printers", "AppEvents", "Volatile Environment", "EUDC"
            };
            foreach (string k in keys) protectedKeys.Add(k);

            foreach (string ms in new[] { @"SOFTWARE\Microsoft", @"SOFTWARE\WOW6432Node\Microsoft" })
            {
                protectedKeys.Add(ms);
                string[] subs =
                {
                    "Windows", @"Windows\CurrentVersion", @"Windows\CurrentVersion\Uninstall", @"Windows\CurrentVersion\Run",
                    @"Windows\CurrentVersion\RunOnce", @"Windows\CurrentVersion\App Paths", @"Windows\CurrentVersion\Explorer",
                    @"Windows\CurrentVersion\Installer", @"Windows\CurrentVersion\SharedDLLs", "Windows NT",
                    @"Windows NT\CurrentVersion", "Cryptography", ".NETFramework", "Active Setup",
                    @"Active Setup\Installed Components", "Windows Defender", "Internet Explorer",
                    @"Windows\CurrentVersion\Explorer\ShellIconOverlayIdentifiers"
                };
                foreach (string s in subs) protectedKeys.Add(ms + "\\" + s);
            }
        }

        public static bool IsGeneric(string normalizedName)
        {
            return string.IsNullOrEmpty(normalizedName) || genericTokens.Contains(normalizedName);
        }

        public static bool IsProtectedClassName(string name)
        {
            return protectedClassNames.Contains(name);
        }

        private static bool IsDesktopDir(string d)
        {
            return desktopDirs.Any(x => string.Equals(x, d, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Pode excluir este arquivo/pasta? Se não puder, explica o motivo.</summary>
        public static bool CanDeletePath(string path, bool isFile, out string reason)
        {
            string p = PathUtil.Normalize(path);
            if (p == null) { reason = "caminho inválido"; return false; }
            if (p.Length <= 3) { reason = "raiz de unidade"; return false; }

            foreach (string d in protectedDirs)
            {
                if (string.Equals(p, d, StringComparison.OrdinalIgnoreCase)) { reason = "pasta protegida do sistema ou do usuário"; return false; }
                if (PathUtil.IsStrictlyUnder(d, p)) { reason = "contém a pasta protegida " + d; return false; }
            }

            if (PathUtil.IsSameOrUnder(p, winDir))
            {
                bool allowed = PathUtil.IsStrictlyUnder(p, winTemp) ||
                               (isFile && PathUtil.IsStrictlyUnder(p, prefetchDir) && p.EndsWith(".pf", StringComparison.OrdinalIgnoreCase));
                if (!allowed) { reason = "pasta do Windows"; return false; }
            }

            foreach (string u in userContentDirs)
            {
                if (PathUtil.IsSameOrUnder(p, u))
                {
                    string ext = Path.GetExtension(p).ToLowerInvariant();
                    bool desktopShortcut = isFile && IsDesktopDir(u) && (ext == ".lnk" || ext == ".url") &&
                                           string.Equals(PathUtil.Parent(p), u, StringComparison.OrdinalIgnoreCase);
                    if (!desktopShortcut) { reason = "área de arquivos pessoais do usuário (" + u + ")"; return false; }
                }
            }

            if (appDir != null && PathUtil.IsSameOrUnder(appDir, p)) { reason = "pasta do próprio desinstalador"; return false; }

            reason = null;
            return true;
        }

        /// <summary>Pode excluir esta chave do registro? Só são permitidos locais conhecidos de programas.</summary>
        public static bool CanDeleteRegistryKey(RegistryHive hive, string subKey, out string reason)
        {
            reason = null;
            if (hive != RegistryHive.LocalMachine && hive != RegistryHive.CurrentUser) { reason = "ramo do registro não permitido"; return false; }
            string k = (subKey ?? "").Trim('\\');
            if (k.Length == 0 || k.IndexOf('\\') < 0) { reason = "chave de nível superior"; return false; }
            if (protectedKeys.Contains(k)) { reason = "chave protegida do sistema"; return false; }
            foreach (string pk in protectedKeys)
                if (pk.StartsWith(k + "\\", StringComparison.OrdinalIgnoreCase)) { reason = "contém a chave protegida " + pk; return false; }

            Match cm = ClassesRx.Match(k);
            if (cm.Success)
            {
                string rest = cm.Groups["rest"].Value;
                if (rest.IndexOf('\\') < 0)
                {
                    if (rest.StartsWith(".") || protectedClassNames.Contains(rest)) { reason = "classe do sistema/extensão de arquivo"; return false; }
                    return true;
                }
                if (allowedClassPatterns.Any(r => r.IsMatch(rest))) return true;
                reason = "local de classes não permitido";
                return false;
            }

            if (otherAllowedKeys.Any(r => r.IsMatch(k))) return true;

            Match gm = GenericSoftwareRx.Match(k);
            if (gm.Success)
            {
                string x = gm.Groups["x"].Value;
                string y = gm.Groups["y"].Success ? gm.Groups["y"].Value : null;
                if (deniedSoftwareRoots.Contains(x)) { reason = "chave do sistema"; return false; }
                if (x.Equals("Microsoft", StringComparison.OrdinalIgnoreCase) && (y == null || microsoftSystemKeys.Contains(y)))
                {
                    reason = "chave do Windows";
                    return false;
                }
                return true;
            }

            reason = "local do registro não permitido";
            return false;
        }

        public static bool CanDeleteRegistryValue(RegistryHive hive, string subKey, string valueName, out string reason)
        {
            reason = null;
            if (string.IsNullOrEmpty(valueName)) { reason = "valor padrão da chave"; return false; }
            if (IsEnvironmentKey(subKey) && protectedEnvVars.Contains(valueName)) { reason = "variável de ambiente do sistema"; return false; }
            return true;
        }

        public static bool IsEnvironmentKey(string subKey)
        {
            return string.Equals(subKey, "Environment", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(subKey, @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment", StringComparison.OrdinalIgnoreCase);
        }
    }
}
