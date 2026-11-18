using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace DesinstaladorCompleto
{
    /// <summary>Execução de processos de linha de comando (reg, sc, schtasks, PowerShell).</summary>
    internal static class Shell
    {
        public class Result
        {
            public int ExitCode = -1;
            public string StdOut = "";
            public string StdErr = "";
        }

        public static Encoding Oem
        {
            get
            {
                try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
                catch { return Encoding.Default; }
            }
        }

        public static string SystemExe(string name)
        {
            return Path.Combine(Environment.SystemDirectory, name);
        }

        public static Result Run(string exe, string args, int timeoutMs, Encoding encoding)
        {
            var r = new Result();
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetTempPath()
            };
            if (encoding != null)
            {
                psi.StandardOutputEncoding = encoding;
                psi.StandardErrorEncoding = encoding;
            }
            using (Process p = Process.Start(psi))
            {
                var outTask = p.StandardOutput.ReadToEndAsync();
                var errTask = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    r.StdErr = "Tempo limite excedido.";
                }
                try { r.StdOut = outTask.Result ?? ""; } catch { }
                try { r.StdErr += errTask.Result ?? ""; } catch { }
                r.ExitCode = p.HasExited ? p.ExitCode : -1;
            }
            return r;
        }

        public static Result RunPowerShell(string script, int timeoutMs)
        {
            string full = "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; $ProgressPreference='SilentlyContinue'; " + script;
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
            string exe = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
            return Run(exe, "-NoProfile -NonInteractive -EncodedCommand " + encoded, timeoutMs, Encoding.UTF8);
        }
    }

    internal static class ProcessUtil
    {
        public static string GetPath(int pid)
        {
            IntPtr h = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return NativeMethods.QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { NativeMethods.CloseHandle(h); }
        }

        /// <summary>Processos cujo executável está dentro de alguma das pastas (ou é um dos arquivos) informados.</summary>
        public static List<Process> FindUnder(IList<string> paths)
        {
            var result = new List<Process>();
            if (paths == null || paths.Count == 0) return result;
            int self = Process.GetCurrentProcess().Id;
            foreach (Process p in Process.GetProcesses())
            {
                bool keep = false;
                try
                {
                    if (p.Id != self && p.Id > 4)
                    {
                        string exe = PathUtil.Normalize(GetPath(p.Id));
                        keep = exe != null && paths.Any(d => PathUtil.IsSameOrUnder(exe, d));
                    }
                }
                catch { }
                if (keep) result.Add(p); else p.Dispose();
            }
            return result;
        }

        public static string Describe(IEnumerable<Process> procs)
        {
            var sb = new StringBuilder();
            foreach (Process p in procs)
            {
                string name;
                try { name = p.ProcessName + ".exe (PID " + p.Id + ")"; } catch { name = "processo"; }
                sb.AppendLine("  • " + name);
            }
            return sb.ToString();
        }

        public static void Kill(IEnumerable<Process> procs, Action<string> log)
        {
            foreach (Process p in procs)
            {
                try
                {
                    string name = p.ProcessName;
                    p.Kill();
                    p.WaitForExit(5000);
                    log("Processo encerrado: " + name + " (PID " + p.Id + ")");
                }
                catch (Exception ex) { log("Não foi possível encerrar um processo: " + ex.Message); }
            }
        }
    }

    /// <summary>Lê o destino de atalhos .lnk usando o objeto COM WScript.Shell.</summary>
    internal static class ShortcutResolver
    {
        private static object shell;
        private static readonly object sync = new object();

        public static string GetTarget(string lnkPath)
        {
            lock (sync)
            {
                try
                {
                    if (shell == null)
                    {
                        Type t = Type.GetTypeFromProgID("WScript.Shell");
                        if (t == null) return null;
                        shell = Activator.CreateInstance(t);
                    }
                    object sc = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                    try
                    {
                        string target = sc.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, sc, null) as string;
                        string icon = sc.GetType().InvokeMember("IconLocation", BindingFlags.GetProperty, null, sc, null) as string;
                        if (string.IsNullOrEmpty(target)) return icon;
                        return target;
                    }
                    finally { Marshal.ReleaseComObject(sc); }
                }
                catch { return null; }
            }
        }
    }

    internal static class IconUtil
    {
        /// <summary>Extrai o ícone de um executável/arquivo .ico e redimensiona para o tamanho pedido.</summary>
        public static Bitmap GetIcon(string iconSpec, int size)
        {
            int index;
            string path = PathUtil.IconPath(iconSpec, out index);
            if (path == null || !File.Exists(path)) return null;
            try
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".png" || ext == ".jpg" || ext == ".bmp")
                {
                    using (var img = Image.FromFile(path)) return Resize(img, size);
                }
                // API do Windows: ícone no tamanho exato (suporta .ico com imagens PNG, que o .NET decodifica mal)
                int want = size <= 16 ? 16 : size <= 24 ? 24 : size <= 32 ? 32 : size <= 48 ? 48 : size <= 64 ? 64 : 256;
                var handles = new IntPtr[1];
                var ids = new int[1];
                if (NativeMethods.PrivateExtractIcons(path, index, want, want, handles, ids, 1, 0) > 0 && handles[0] != IntPtr.Zero)
                {
                    try
                    {
                        using (Icon ic = Icon.FromHandle(handles[0]))
                        using (Bitmap b = ic.ToBitmap()) return Resize(b, size);
                    }
                    finally { NativeMethods.DestroyIcon(handles[0]); }
                }
                var large = new IntPtr[1];
                var small = new IntPtr[1];
                int n = NativeMethods.ExtractIconEx(path, index, large, small, 1);
                if (n > 0)
                {
                    IntPtr h = size > 16 && large[0] != IntPtr.Zero ? large[0] : (small[0] != IntPtr.Zero ? small[0] : large[0]);
                    Bitmap result = null;
                    if (h != IntPtr.Zero)
                    {
                        using (Icon ic = Icon.FromHandle(h)) using (Bitmap b = ic.ToBitmap()) result = Resize(b, size);
                    }
                    if (large[0] != IntPtr.Zero) NativeMethods.DestroyIcon(large[0]);
                    if (small[0] != IntPtr.Zero) NativeMethods.DestroyIcon(small[0]);
                    if (result != null) return result;
                }
                using (Icon ic = Icon.ExtractAssociatedIcon(path))
                {
                    if (ic != null) using (Bitmap b = ic.ToBitmap()) return Resize(b, size);
                }
            }
            catch { }
            return null;
        }

        public static Bitmap Resize(Image src, int size)
        {
            var bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, 0, 0, size, size);
            }
            return bmp;
        }
    }

    internal static class SystemRestore
    {
        /// <summary>Cria um ponto de restauração. Retorna null se deu certo, ou a mensagem de erro.</summary>
        public static string Create(string description)
        {
            try
            {
                var scope = new ManagementScope(@"\\.\root\default");
                using (var mc = new ManagementClass(scope, new ManagementPath("SystemRestore"), new ObjectGetOptions()))
                {
                    ManagementBaseObject inParams = mc.GetMethodParameters("CreateRestorePoint");
                    inParams["Description"] = description.Length > 60 ? description.Substring(0, 60) : description;
                    inParams["RestorePointType"] = 1;   // APPLICATION_UNINSTALL
                    inParams["EventType"] = 100;        // BEGIN_SYSTEM_CHANGE
                    ManagementBaseObject outParams = mc.InvokeMethod("CreateRestorePoint", inParams, null);
                    uint rv = Convert.ToUInt32(outParams["ReturnValue"]);
                    if (rv == 0) return null;
                    if (rv == 1058) return "a Proteção do Sistema está desativada neste computador (código 1058)";
                    return "código de retorno " + rv;
                }
            }
            catch (Exception ex) { return ex.Message; }
        }
    }
}
