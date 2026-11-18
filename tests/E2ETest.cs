using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using DesinstaladorCompleto;
using Microsoft.Win32;

/// <summary>
/// Teste de ponta a ponta com um programa FALSO criado só no perfil do usuário (HKCU/AppData).
/// Cria o programa → executa o desinstalador dele → varre resquícios → exclui (exceto "Baixa") → confere o resultado.
/// Nenhum programa real é tocado; o PATH do usuário é comparado byte a byte antes/depois.
/// </summary>
internal static class E2ETest
{
    private const string Name = "TesteDesinstaladorZ9";
    private static int failures;

    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string installDir = Path.Combine(local, @"Programs\" + Name);
        string exe = Path.Combine(installDir, Name + ".exe");
        string dataDir = Path.Combine(roaming, Name);
        string dotDir = Path.Combine(profile, "." + Name.ToLowerInvariant());
        string workspace = Path.Combine(roaming, Name + "Workspace");
        string startMenuLnk = Path.Combine(roaming, @"Microsoft\Windows\Start Menu\Programs\" + Name + ".lnk");
        string tempFile = Path.Combine(Path.GetTempPath(), Name.ToLowerInvariant() + "-log.txt");
        const string uninstKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + Name;
        const string progId = Name + ".Arquivo";

        string originalPath;
        using (RegistryKey env = Registry.CurrentUser.OpenSubKey("Environment"))
            originalPath = env.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;

        try
        {
            // ---------------------------------------------------------- 1. "instala" o programa falso
            Console.WriteLine("== Criando programa falso");
            Directory.CreateDirectory(Path.Combine(installDir, "bin"));
            File.Copy(Path.Combine(Environment.SystemDirectory, "where.exe"), exe, true);
            File.WriteAllText(Path.Combine(installDir, "dados.txt"), "arquivo que o desinstalador esquece");
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(Path.Combine(dataDir, "config.json"), "{}");
            Directory.CreateDirectory(dotDir);
            File.WriteAllText(Path.Combine(dotDir, "settings"), "x");
            Directory.CreateDirectory(Path.Combine(workspace, ".git"));          // parece um projeto: NÃO pode ser excluído
            File.WriteAllText(Path.Combine(workspace, "codigo.txt"), "projeto do usuário");
            File.WriteAllText(tempFile, "log");
            CreateShortcut(startMenuLnk, exe);

            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(uninstKey))
            {
                k.SetValue("DisplayName", Name + " 1.0");
                k.SetValue("DisplayVersion", "1.0");
                k.SetValue("Publisher", "Empresa Teste Z9");
                k.SetValue("InstallLocation", installDir);
                k.SetValue("DisplayIcon", exe + ",0");
                // O "desinstalador" remove só o exe e a entrada do registro, deixando resquícios para trás
                k.SetValue("UninstallString", "\"" + Path.Combine(Environment.SystemDirectory, "cmd.exe") + "\" /c del /f /q \"" + exe +
                           "\" & reg delete \"HKCU\\" + uninstKey + "\" /f");
            }
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\" + Name)) k.SetValue("Opcao", 1);
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + progId + @"\shell\open\command")) k.SetValue("", "\"" + exe + "\" \"%1\"");
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.tdz9\OpenWithProgids")) k.SetValue(progId, new byte[0], RegistryValueKind.None);
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) k.SetValue(Name, "\"" + exe + "\" /background");
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey("Environment"))
                k.SetValue("Path", (originalPath ?? "").TrimEnd(';') + ";" + Path.Combine(installDir, "bin"), RegistryValueKind.ExpandString);

            // ---------------------------------------------------------- 2. fluxo do aplicativo
            InstalledProgram p = ProgramScanner.GetWin32Programs().FirstOrDefault(x => x.DisplayName == Name + " 1.0");
            Check(p != null, "programa falso aparece na lista");
            if (p == null) return 1;

            ProgramSnapshot snap = ProgramSnapshot.Capture(p, Console.WriteLine);
            Check(string.Equals(snap.InstallDir, installDir, StringComparison.OrdinalIgnoreCase), "pasta de instalação identificada");

            Console.WriteLine("== Executando o desinstalador");
            UninstallResult res = UninstallRunner.Run(p, false, m => Console.WriteLine("   " + m), CancellationToken.None);
            Check(res.Started && res.ExitCode == 0, "desinstalador executado (código " + res.ExitCode + ")");
            Check(!ProgramScanner.IsStillInstalled(p), "entrada de desinstalação removida pelo desinstalador");

            Console.WriteLine("== Varrendo resquícios");
            var others = ProgramScanner.GetWin32Programs().Where(x => x.Id != p.Id).ToList();
            List<LeftoverItem> items = new LeftoverScanner(snap, others, false, m => { }).Scan();
            foreach (LeftoverItem it in items)
                Console.WriteLine("   [" + it.Confidence + "] " + it.Display + (it.Warning != null ? "  !! " + it.Warning : ""));

            Func<string, LeftoverItem> find = s => items.FirstOrDefault(i => i.Display.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);
            Check(find(installDir) != null, "encontrou a pasta de instalação");
            Check(find(dataDir) != null, "encontrou a pasta de dados em AppData");
            Check(find(dotDir) != null, "encontrou a pasta oculta no perfil");
            Check(find(startMenuLnk) != null, "encontrou o atalho do Menu Iniciar");
            Check(find(tempFile) != null, "encontrou o arquivo temporário");
            Check(find(@"Software\" + Name) != null, "encontrou a chave HKCU\\Software");
            Check(find(progId) != null, "encontrou o ProgID");
            Check(items.Any(i => i.Type == LeftoverType.RegistryValue && i.ValueName == progId), "encontrou o valor OpenWithProgids");
            Check(items.Any(i => i.Category == Categories.Inicializacao), "encontrou a inicialização automática");
            Check(items.Any(i => i.Type == LeftoverType.PathEntry), "encontrou a entrada no PATH");
            LeftoverItem ws = find(workspace);
            Check(ws == null || ws.Confidence == Confidence.Baixa, "pasta com .git NÃO vem marcada (Baixa ou ausente)");

            // ---------------------------------------------------------- 3. exclusão (o que a interface marca por padrão)
            Console.WriteLine("== Excluindo itens Alta/Média");
            List<LeftoverItem> selected = items.Where(i => i.Confidence != Confidence.Baixa).ToList();
            CleanReport rep = Cleaner.Execute(selected, false, Name, m => Console.WriteLine("   " + m));
            Check(rep.Errors.Count == 0, "nenhuma falha na exclusão (" + rep.Errors.Count + ")");
            Check(Directory.GetFiles(rep.BackupDir, "*.reg").Length > 0, "backup .reg criado");

            Check(!Directory.Exists(installDir), "pasta de instalação excluída");
            Check(!Directory.Exists(dataDir), "pasta de dados excluída");
            Check(!Directory.Exists(dotDir), "pasta oculta excluída");
            Check(!File.Exists(startMenuLnk), "atalho excluído");
            Check(!File.Exists(tempFile), "temporário excluído");
            Check(Registry.CurrentUser.OpenSubKey(@"Software\" + Name) == null, "chave HKCU\\Software excluída");
            Check(Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + progId) == null, "ProgID excluído");
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\.tdz9\OpenWithProgids"))
                Check(k == null || !k.GetValueNames().Contains(progId), "valor OpenWithProgids excluído");
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                Check(k.GetValue(Name) == null, "inicialização automática excluída");
            Check(Directory.Exists(workspace) && Directory.Exists(Path.Combine(workspace, ".git")), "pasta de projeto (.git) PRESERVADA");

            string pathNow;
            using (RegistryKey env = Registry.CurrentUser.OpenSubKey("Environment"))
                pathNow = env.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            Check(string.Equals((pathNow ?? "").TrimEnd(';'), (originalPath ?? "").TrimEnd(';'), StringComparison.Ordinal),
                  "PATH do usuário igual ao original (só a entrada do programa foi removida)");

            try { Directory.Delete(rep.BackupDir, true); } catch { }
        }
        finally
        {
            // ---------------------------------------------------------- limpeza do teste (sempre)
            using (RegistryKey env = Registry.CurrentUser.OpenSubKey("Environment", true))
            {
                if (originalPath != null) env.SetValue("Path", originalPath, RegistryValueKind.ExpandString);
                else env.DeleteValue("Path", false);
            }
            foreach (string d in new[] { installDir, dataDir, dotDir, workspace }) { try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { } }
            foreach (string f in new[] { startMenuLnk, tempFile }) { try { File.Delete(f); } catch { } }
            Registry.CurrentUser.DeleteSubKeyTree(uninstKey, false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\" + Name, false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + progId, false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\.tdz9", false);
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)) k.DeleteValue(Name, false);
        }

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "TODOS OS TESTES PASSARAM" : failures + " TESTE(S) FALHARAM");
        return failures == 0 ? 0 : 1;
    }

    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  OK    " : "  FALHA ") + what);
        if (!ok) failures++;
    }

    private static void CreateShortcut(string lnk, string target)
    {
        Type t = Type.GetTypeFromProgID("WScript.Shell");
        object shell = Activator.CreateInstance(t);
        object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
        sc.GetType().InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
        sc.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
    }
}
