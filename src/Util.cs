using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DesinstaladorCompleto
{
    /// <summary>Funções de caminho de arquivo.</summary>
    internal static class PathUtil
    {
        private static readonly Regex DriveOnly = new Regex(@"^[A-Za-z]:$");

        /// <summary>Expande variáveis, remove aspas e devolve o caminho absoluto sem barra final (ou null se inválido).</summary>
        public static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                string p = path.Trim().Trim('"').Trim();
                if (p.IndexOf('%') >= 0) p = Environment.ExpandEnvironmentVariables(p);
                if (p.StartsWith(@"\??\")) p = p.Substring(4);
                else if (p.StartsWith(@"\\?\")) p = p.Substring(4);
                p = p.Replace('/', '\\');
                if (DriveOnly.IsMatch(p)) p += "\\";
                if (p.Length < 3) return null;
                if (p.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return null;
                bool unc = p.StartsWith(@"\\");
                if (!unc && !(char.IsLetter(p[0]) && p[1] == ':' && p[2] == '\\')) return null;
                p = Path.GetFullPath(p);
                if (p.Length > 3) p = p.TrimEnd('\\');
                return p;
            }
            catch { return null; }
        }

        public static bool IsSameOrUnder(string path, string dir)
        {
            if (path == null || dir == null) return false;
            if (string.Equals(path, dir, StringComparison.OrdinalIgnoreCase)) return true;
            return IsStrictlyUnder(path, dir);
        }

        public static bool IsStrictlyUnder(string path, string dir)
        {
            if (path == null || dir == null) return false;
            string d = dir.EndsWith("\\") ? dir : dir + "\\";
            return path.Length > d.Length && path.StartsWith(d, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Verifica se um texto (linha de comando, valor do registro...) contém uma referência à pasta,
        /// respeitando os limites do nome (C:\App não casa com C:\AppOutro).
        /// </summary>
        public static bool ReferencesDir(string text, string dir)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(dir)) return false;
            string t = text;
            if (t.IndexOf('%') >= 0) { try { t = Environment.ExpandEnvironmentVariables(t); } catch { } }
            if (t.IndexOf('/') >= 0) t = t.Replace('/', '\\');
            string d = dir.TrimEnd('\\');
            if (d.Length < 4) return false; // nunca casar com a raiz de uma unidade
            int idx = 0;
            while (idx < t.Length && (idx = t.IndexOf(d, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                int end = idx + d.Length;
                bool startOk = idx == 0 || !char.IsLetterOrDigit(t[idx - 1]);
                bool endOk = end >= t.Length;
                if (!endOk)
                {
                    char c = t[end];
                    if (c == '\\' || c == '"' || c == '\'' || c == ',' || c == ';' || c == '|' || c == '\0' ||
                        c == '\r' || c == '\n' || c == '\t' || c == ')' || c == '<' || c == '&')
                        endOk = true;
                    else if (c == ' ')
                    {
                        int j = end;
                        while (j < t.Length && t[j] == ' ') j++;
                        endOk = j >= t.Length || t[j] == '-' || t[j] == '/';
                    }
                }
                if (startOk && endOk) return true;
                idx = end;
            }
            return false;
        }

        /// <summary>Separa uma linha de comando em executável e argumentos.</summary>
        public static void SplitCommand(string cmd, out string exe, out string args)
        {
            exe = null;
            args = "";
            if (string.IsNullOrWhiteSpace(cmd)) return;
            string c = cmd.Trim();
            if (c.StartsWith("\""))
            {
                int end = c.IndexOf('"', 1);
                if (end > 0) { exe = c.Substring(1, end - 1); args = c.Substring(end + 1).Trim(); }
                else exe = c.Trim('"');
                return;
            }
            int from = 0;
            while (true)
            {
                int idx = c.IndexOf(".exe", from, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) break;
                int after = idx + 4;
                if (after == c.Length || c[after] == ' ' || c[after] == '"')
                {
                    exe = c.Substring(0, after);
                    args = c.Substring(after).Trim();
                    return;
                }
                from = after;
            }
            int sp = c.IndexOf(' ');
            if (sp > 0) { exe = c.Substring(0, sp); args = c.Substring(sp + 1).Trim(); }
            else exe = c;
        }

        public static string ExtractExePath(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return null;
            string c = cmd;
            if (c.IndexOf('%') >= 0) { try { c = Environment.ExpandEnvironmentVariables(c); } catch { } }
            c = c.Trim();
            if (c.StartsWith(@"\??\")) c = c.Substring(4);
            string exe, args;
            SplitCommand(c, out exe, out args);
            return exe;
        }

        /// <summary>Interpreta "C:\pasta\app.exe,0" devolvendo o caminho e o índice do ícone.</summary>
        public static string IconPath(string spec, out int index)
        {
            index = 0;
            if (string.IsNullOrWhiteSpace(spec)) return null;
            string s = spec.Trim();
            string rest = "";
            if (s.StartsWith("\""))
            {
                int end = s.IndexOf('"', 1);
                if (end > 0) { rest = s.Substring(end + 1); s = s.Substring(1, end - 1); }
                else s = s.Trim('"');
                int comma = rest.IndexOf(',');
                if (comma >= 0) int.TryParse(rest.Substring(comma + 1).Trim(), out index);
            }
            else
            {
                int comma = s.LastIndexOf(',');
                if (comma > 0)
                {
                    int n;
                    if (int.TryParse(s.Substring(comma + 1).Trim(), out n)) { index = n; s = s.Substring(0, comma); }
                }
            }
            return Normalize(s);
        }

        public static string FileNameNoExt(string path)
        {
            try { return Path.GetFileNameWithoutExtension(path) ?? ""; } catch { return ""; }
        }

        public static string FileName(string path)
        {
            try { return Path.GetFileName(path) ?? ""; } catch { return ""; }
        }

        public static string Parent(string path)
        {
            try { return Normalize(Path.GetDirectoryName(path)); } catch { return null; }
        }
    }

    /// <summary>Normalização de nomes para comparar pastas/chaves com o nome do programa.</summary>
    internal static class NameUtil
    {
        private static readonly Regex Brackets = new Regex(@"\([^)]*\)|\[[^\]]*\]", RegexOptions.Compiled);
        private static readonly Regex Versions = new Regex(@"(?<![a-z0-9])v?\d+(\.\d+)+[a-z0-9-]*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex Arch = new Regex(@"(?<![a-z0-9])(x64|x86|amd64|arm64|win64|win32|64-bit|32-bit|64 bit|32 bit|64bit|32bit)(?![a-z0-9])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex GuidLike = new Regex(@"^\{?[0-9A-Fa-f]{8}-?[0-9A-Fa-f]{4}-?[0-9A-Fa-f]{4}-?[0-9A-Fa-f]{4}-?[0-9A-Fa-f]{12}\}?$", RegexOptions.Compiled);

        private static readonly HashSet<string> PublisherNoise = new HashSet<string>(new[]
        {
            "inc", "incorporated", "corporation", "corp", "co", "company", "ltd", "limited", "llc", "gmbh", "sa", "ag",
            "bv", "pty", "srl", "ltda", "plc", "kg", "oy", "ab", "as", "sas", "spa", "kk", "the", "software",
            "technologies", "technology", "systems", "labs", "group", "foundation", "development", "community",
            "team", "project", "contributors", "international", "holdings"
        });

        public static string StripAccents(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s.Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString();
        }

        /// <summary>"Python 3.14.8 (64-bit)" → "python"; "Notepad++" → "notepad"; "Microsoft VS Code" → "microsoftvscode".</summary>
        public static string NormalizeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string t = StripAccents(s.ToLowerInvariant());
            t = Brackets.Replace(t, " ");
            t = Versions.Replace(t, " ");
            t = Arch.Replace(t, " ");
            var sb = new StringBuilder(t.Length);
            foreach (char c in t)
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) sb.Append(c);
            return sb.ToString();
        }

        public static void NormalizePublisher(string publisher, out string full, out string first)
        {
            full = "";
            first = "";
            if (string.IsNullOrWhiteSpace(publisher)) return;
            string t = Brackets.Replace(StripAccents(publisher.ToLowerInvariant()), " ");
            var words = Regex.Split(t, @"[^a-z0-9]+").Where(w => w.Length > 0 && !PublisherNoise.Contains(w)).ToList();
            if (words.Count == 0) return;
            full = string.Concat(words);
            first = words[0];
            if (full.Length < 3) full = "";
            if (first.Length < 3) first = "";
        }

        public static bool IsGuidLike(string s)
        {
            return !string.IsNullOrEmpty(s) && GuidLike.IsMatch(s.Trim());
        }
    }

    internal static class FsUtil
    {
        private static readonly string[] Empty = new string[0];

        public static string[] Dirs(string p)
        {
            if (p == null) return Empty;
            try { return Directory.Exists(p) ? Directory.GetDirectories(p) : Empty; } catch { return Empty; }
        }

        public static string[] Files(string p)
        {
            return Files(p, "*");
        }

        public static string[] Files(string p, string pattern)
        {
            if (p == null) return Empty;
            try { return Directory.Exists(p) ? Directory.GetFiles(p, pattern) : Empty; } catch { return Empty; }
        }

        public static bool IsReparse(string p)
        {
            try { return (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0; } catch { return false; }
        }

        public static long DirSize(string root, int maxEntries)
        {
            long total = 0;
            int seen = 0;
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0 && seen < maxEntries)
            {
                string d = stack.Pop();
                foreach (string f in Files(d))
                {
                    seen++;
                    try { total += new FileInfo(f).Length; } catch { }
                }
                foreach (string sd in Dirs(d))
                {
                    seen++;
                    if (!IsReparse(sd)) stack.Push(sd);
                }
            }
            return total;
        }

        public static string FormatSize(long bytes)
        {
            if (bytes < 0) return "";
            if (bytes < 1024) return bytes + " B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return kb.ToString("0.#") + " KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return mb.ToString("0.#") + " MB";
            return (mb / 1024.0).ToString("0.##") + " GB";
        }
    }

    /// <summary>Pastas conhecidas do Windows.</summary>
    internal static class Folders
    {
        public static string SF(Environment.SpecialFolder f)
        {
            try { return PathUtil.Normalize(Environment.GetFolderPath(f, Environment.SpecialFolderOption.DoNotVerify)); }
            catch { return null; }
        }

        public static string Env(string name)
        {
            try { return PathUtil.Normalize(Environment.GetEnvironmentVariable(name)); } catch { return null; }
        }

        public static string Sub(string root, string relative)
        {
            if (root == null) return null;
            return PathUtil.Normalize(root + "\\" + relative);
        }

        public static string WinDir { get { return SF(Environment.SpecialFolder.Windows) ?? Env("SystemRoot") ?? @"C:\Windows"; } }
        public static string Profile { get { return SF(Environment.SpecialFolder.UserProfile); } }
        public static string Roaming { get { return SF(Environment.SpecialFolder.ApplicationData); } }
        public static string Local { get { return SF(Environment.SpecialFolder.LocalApplicationData); } }
        public static string LocalLow { get { return Sub(Profile, @"AppData\LocalLow"); } }
        public static string ProgramData { get { return SF(Environment.SpecialFolder.CommonApplicationData); } }
        public static string Temp { get { return PathUtil.Normalize(Path.GetTempPath()); } }
    }
}
