using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Win32;

namespace DesinstaladorCompleto
{
    /// <summary>Lista os programas instalados: entradas de desinstalação do registro e aplicativos da Store/UWP.</summary>
    internal static class ProgramScanner
    {
        private class Root
        {
            public RegistryHive Hive;
            public string Path;
            public string Label;
        }

        private static readonly Regex KbRx = new Regex(@"\(KB\d{6,}\)|\bKB\d{6,}\b", RegexOptions.IgnoreCase);

        public static List<InstalledProgram> GetWin32Programs()
        {
            bool is64 = Environment.Is64BitOperatingSystem;
            var roots = new List<Root>
            {
                new Root { Hive = RegistryHive.LocalMachine, Path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", Label = is64 ? "Todos os usuários (64 bits)" : "Todos os usuários" },
                new Root { Hive = RegistryHive.CurrentUser, Path = @"Software\Microsoft\Windows\CurrentVersion\Uninstall", Label = "Usuário atual" }
            };
            if (is64)
            {
                roots.Insert(1, new Root { Hive = RegistryHive.LocalMachine, Path = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", Label = "Todos os usuários (32 bits)" });
                roots.Add(new Root { Hive = RegistryHive.CurrentUser, Path = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", Label = "Usuário atual (32 bits)" });
            }

            var list = new List<InstalledProgram>();
            foreach (Root r in roots)
            {
                using (RegistryKey root = RegUtil.Open(r.Hive, r.Path))
                {
                    if (root == null) continue;
                    foreach (string name in RegUtil.SubKeyNames(root))
                    {
                        try
                        {
                            using (RegistryKey k = root.OpenSubKey(name))
                            {
                                InstalledProgram p = Read(k, r, name);
                                if (p != null) list.Add(p);
                            }
                        }
                        catch { }
                    }
                }
            }
            return list;
        }

        private static InstalledProgram Read(RegistryKey k, Root r, string keyName)
        {
            if (k == null) return null;
            string name = RegUtil.GetString(k, "DisplayName");
            if (string.IsNullOrWhiteSpace(name)) return null;

            var p = new InstalledProgram
            {
                DisplayName = name.Trim(),
                DisplayVersion = RegUtil.GetString(k, "DisplayVersion"),
                Publisher = RegUtil.GetString(k, "Publisher"),
                InstallLocation = RegUtil.GetString(k, "InstallLocation"),
                UninstallString = RegUtil.GetString(k, "UninstallString"),
                QuietUninstallString = RegUtil.GetString(k, "QuietUninstallString"),
                DisplayIcon = RegUtil.GetString(k, "DisplayIcon"),
                InstallDate = RegUtil.GetString(k, "InstallDate"),
                Hive = r.Hive,
                RegistrySubKey = r.Path + "\\" + keyName,
                KeyName = keyName,
                SourceLabel = r.Label,
                Kind = RegUtil.GetInt(k, "WindowsInstaller", 0) == 1 ? ProgramKind.Msi : ProgramKind.Win32
            };
            int size = RegUtil.GetInt(k, "EstimatedSize", 0);
            if (size > 0) p.EstimatedSizeKB = size;
            p.IsSystemComponent = RegUtil.GetInt(k, "SystemComponent", 0) == 1;
            p.NoRemove = RegUtil.GetInt(k, "NoRemove", 0) == 1;

            string parent = RegUtil.GetString(k, "ParentKeyName");
            string release = RegUtil.GetString(k, "ReleaseType") ?? "";
            p.IsUpdate = !string.IsNullOrEmpty(parent) || release.IndexOf("Update", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         release.IndexOf("Hotfix", StringComparison.OrdinalIgnoreCase) >= 0 || KbRx.IsMatch(name);

            // Como o Painel de Controle: entradas sem desinstalador ficam ocultas por padrão
            if (string.IsNullOrWhiteSpace(p.UninstallString) && p.Kind != ProgramKind.Msi) p.IsSystemComponent = true;
            return p;
        }

        /// <summary>Aplicativos da Microsoft Store / UWP do usuário atual (via PowerShell Get-AppxPackage).</summary>
        public static List<InstalledProgram> GetStorePrograms()
        {
            var list = new List<InstalledProgram>();
            const string script =
                "Get-AppxPackage | ForEach-Object { [string]::Join([char]9, @($_.Name, $_.PackageFullName, $_.PackageFamilyName, " +
                "$_.Publisher, $_.Version, $_.InstallLocation, $_.IsFramework, $_.NonRemovable, $_.SignatureKind, $_.IsResourcePackage)) }";
            Shell.Result r;
            try { r = Shell.RunPowerShell(script, 90000); } catch { return list; }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in r.StdOut.Split('\n'))
            {
                string[] f = raw.TrimEnd('\r').Split('\t');
                if (f.Length < 9 || string.IsNullOrWhiteSpace(f[1]) || !seen.Add(f[1])) continue;
                bool isFramework = f[6] == "True";
                bool nonRemovable = f[7] == "True";
                bool isResource = f.Length > 9 && f[9] == "True";
                if (isResource) continue;

                var p = new InstalledProgram
                {
                    Kind = ProgramKind.StoreApp,
                    PackageName = f[0],
                    PackageFullName = f[1],
                    PackageFamilyName = f[2],
                    Publisher = ExtractCN(f[3]),
                    DisplayVersion = f[4],
                    InstallLocation = f[5],
                    NoRemove = nonRemovable,
                    IsSystemComponent = isFramework || nonRemovable || f[8] == "System",
                    SourceLabel = "Microsoft Store / Windows"
                };
                string logo, manifestPublisher;
                p.DisplayName = ReadManifest(p.InstallLocation, out logo, out manifestPublisher) ?? Prettify(p.PackageName);
                p.LogoPath = logo;
                if (!string.IsNullOrEmpty(manifestPublisher)) p.Publisher = manifestPublisher;
                list.Add(p);
            }
            return list;
        }

        private static string ExtractCN(string publisher)
        {
            if (string.IsNullOrEmpty(publisher)) return publisher;
            Match m = Regex.Match(publisher, @"(?:^|,\s*)(?:CN|O)=(""[^""]*""|[^,]*)");
            return m.Success ? m.Groups[1].Value.Trim('"').Trim() : publisher;
        }

        private static string Prettify(string packageName)
        {
            if (string.IsNullOrEmpty(packageName)) return packageName;
            string s = packageName;
            int dot = s.LastIndexOf('.');
            if (dot >= 0 && dot < s.Length - 1)
            {
                string last = s.Substring(dot + 1);
                if (!Regex.IsMatch(last, "^[0-9A-F]+$")) s = last;
            }
            s = Regex.Replace(s, "(?<=[a-z])(?=[A-Z])", " ");
            return s;
        }

        private static string ReadManifest(string installLocation, out string logo, out string publisher)
        {
            logo = null;
            publisher = null;
            try
            {
                string manifest = Path.Combine(installLocation ?? "", "AppxManifest.xml");
                if (!File.Exists(manifest)) return null;
                var doc = new XmlDocument();
                doc.Load(manifest);
                XmlNode logoNode = doc.SelectSingleNode("//*[local-name()='Properties']/*[local-name()='Logo']");
                if (logoNode != null) logo = FindScaledAsset(installLocation, logoNode.InnerText.Trim());
                XmlNode pubNode = doc.SelectSingleNode("//*[local-name()='Properties']/*[local-name()='PublisherDisplayName']");
                if (pubNode != null && !pubNode.InnerText.Trim().StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                    publisher = pubNode.InnerText.Trim();
                XmlNode n = doc.SelectSingleNode("//*[local-name()='Properties']/*[local-name()='DisplayName']");
                string name = n == null ? null : n.InnerText.Trim();
                if (string.IsNullOrEmpty(name) || name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)) return null;
                return name;
            }
            catch { return null; }
        }

        private static string FindScaledAsset(string root, string relative)
        {
            try
            {
                string full = Path.Combine(root, relative);
                if (File.Exists(full)) return full;
                string dir = Path.GetDirectoryName(full);
                string pattern = Path.GetFileNameWithoutExtension(full) + "*" + Path.GetExtension(full);
                string[] files = FsUtil.Files(dir, pattern);
                return files.Length > 0 ? files[0] : null;
            }
            catch { return null; }
        }

        /// <summary>O programa ainda aparece como instalado?</summary>
        public static bool IsStillInstalled(InstalledProgram p)
        {
            if (p.Kind == ProgramKind.StoreApp)
            {
                string fn = (p.PackageFullName ?? "").Replace("'", "''");
                var r = Shell.RunPowerShell("if (Get-AppxPackage | Where-Object { $_.PackageFullName -eq '" + fn + "' }) { 'SIM' } else { 'NAO' }", 60000);
                return r.StdOut.IndexOf("SIM", StringComparison.Ordinal) >= 0;
            }
            return RegUtil.KeyExists(p.Hive, p.RegistrySubKey);
        }
    }
}
