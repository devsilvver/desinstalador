using System;
using System.Globalization;
using Microsoft.Win32;

namespace DesinstaladorCompleto
{
    /// <summary>Acesso ao registro, sempre pela visão de 64 bits (os caminhos WOW6432Node são usados explicitamente).</summary>
    internal static class RegUtil
    {
        private static readonly string[] Empty = new string[0];

        public static RegistryKey Open(RegistryHive hive, string subKey, bool writable = false)
        {
            try
            {
                using (RegistryKey b = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                    return b.OpenSubKey(subKey, writable);
            }
            catch { return null; }
        }

        public static bool KeyExists(RegistryHive hive, string subKey)
        {
            using (RegistryKey k = Open(hive, subKey)) return k != null;
        }

        public static string[] SubKeyNames(RegistryKey k)
        {
            if (k == null) return Empty;
            try { return k.GetSubKeyNames(); } catch { return Empty; }
        }

        public static string[] ValueNames(RegistryKey k)
        {
            if (k == null) return Empty;
            try { return k.GetValueNames(); } catch { return Empty; }
        }

        public static RegistryKey OpenSub(RegistryKey k, string name)
        {
            if (k == null) return null;
            try { return k.OpenSubKey(name); } catch { return null; }
        }

        public static string GetString(RegistryKey k, string name)
        {
            if (k == null) return null;
            try
            {
                object o = k.GetValue(name);
                if (o == null) return null;
                string[] arr = o as string[];
                if (arr != null) return string.Join("\n", arr);
                if (o is byte[]) return null;
                return Convert.ToString(o, CultureInfo.InvariantCulture);
            }
            catch { return null; }
        }

        /// <summary>Lê o valor sem expandir variáveis de ambiente (%SystemRoot% etc.).</summary>
        public static string GetRawString(RegistryKey k, string name)
        {
            if (k == null) return null;
            try { return k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string; }
            catch { return null; }
        }

        public static int GetInt(RegistryKey k, string name, int defaultValue)
        {
            if (k == null) return defaultValue;
            try
            {
                object o = k.GetValue(name);
                if (o is int) return (int)o;
                if (o is long) return (int)(long)o;
                int n;
                if (o is string && int.TryParse((string)o, out n)) return n;
            }
            catch { }
            return defaultValue;
        }

        /// <summary>Valor padrão de uma subchave (ex.: "shell\open\command").</summary>
        public static string DefaultOf(RegistryKey k, string sub)
        {
            if (k == null) return null;
            try
            {
                using (RegistryKey s = k.OpenSubKey(sub)) return s == null ? null : GetString(s, "");
            }
            catch { return null; }
        }

        public static string ShortHive(RegistryHive h)
        {
            switch (h)
            {
                case RegistryHive.LocalMachine: return "HKLM";
                case RegistryHive.CurrentUser: return "HKCU";
                case RegistryHive.ClassesRoot: return "HKCR";
                case RegistryHive.Users: return "HKU";
                default: return h.ToString();
            }
        }

        public static string LongHive(RegistryHive h)
        {
            switch (h)
            {
                case RegistryHive.LocalMachine: return "HKEY_LOCAL_MACHINE";
                case RegistryHive.CurrentUser: return "HKEY_CURRENT_USER";
                case RegistryHive.ClassesRoot: return "HKEY_CLASSES_ROOT";
                case RegistryHive.Users: return "HKEY_USERS";
                default: return h.ToString();
            }
        }

        public static string Display(RegistryHive h, string subKey, string valueName)
        {
            string s = ShortHive(h) + "\\" + subKey;
            if (valueName != null) s += "  →  " + (valueName.Length == 0 ? "(Padrão)" : valueName);
            return s;
        }

        public static bool IsStrictlyUnderKey(string sub, string parent)
        {
            if (sub == null || parent == null) return false;
            string p = parent.TrimEnd('\\') + "\\";
            return sub.Length > p.Length && sub.StartsWith(p, StringComparison.OrdinalIgnoreCase);
        }
    }
}
