using System;
using System.Linq;
using System.Text;
using DesinstaladorCompleto;

/// <summary>
/// Teste SOMENTE LEITURA: lista programas e faz a varredura de resquícios de um programa sem desinstalar e sem excluir nada.
/// Uso: ScanTest.exe              → lista os programas
///      ScanTest.exe "nome"       → varre os resquícios do primeiro programa cujo nome contém "nome"
///      ScanTest.exe --store      → lista apps da Store
/// </summary>
internal static class ScanTest
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var progs = ProgramScanner.GetWin32Programs();
        if (args.Length == 0)
        {
            Console.WriteLine("Programas Win32: " + progs.Count);
            foreach (var p in progs.OrderBy(x => x.DisplayName))
                Console.WriteLine((p.IsSystemComponent ? "[sys] " : "") + (p.IsUpdate ? "[upd] " : "") + p.DisplayName + " | " + p.DisplayVersion + " | " + p.Publisher + " | " + p.InstallLocation);
            return 0;
        }
        if (args[0] == "--store")
        {
            var st = ProgramScanner.GetStorePrograms();
            Console.WriteLine("Apps da Store: " + st.Count);
            foreach (var p in st.OrderBy(x => x.DisplayName)) Console.WriteLine((p.IsSystemComponent ? "[sys] " : "") + p.DisplayName + " | " + p.PackageFamilyName + " | " + p.Publisher);
            return 0;
        }

        if (args[0] == "--all")
        {
            // Varre todos os programas visíveis e mostra só pastas e chaves (para revisar conflitos entre programas)
            foreach (var p in progs.Where(x => !x.IsSystemComponent && !x.IsUpdate).OrderBy(x => x.DisplayName))
            {
                var sn = ProgramSnapshot.Capture(p, m => { });
                var oth = progs.Where(x => x.Id != p.Id).ToList();
                var res = new LeftoverScanner(sn, oth, false, m => { }).Scan();
                Console.WriteLine("### " + p.DisplayName + "  [dir=" + (sn.InstallDir ?? "-") + "] nomes=" + string.Join(",", sn.StrongNames) + " exe=" + string.Join(",", sn.ExeNames));
                foreach (var it in res.Where(i => i.Type == LeftoverType.Folder || i.Type == LeftoverType.RegistryKey || i.Type == LeftoverType.Service || i.Type == LeftoverType.ScheduledTask)
                                      .Where(i => i.Category != Categories.Associacoes && i.Category != Categories.Temporarios))
                    Console.WriteLine("   [" + it.Confidence + "] " + it.Display + (it.Warning != null ? "  !! " + it.Warning : ""));
            }
            return 0;
        }

        var target = progs.FirstOrDefault(p => p.DisplayName.IndexOf(args[0], StringComparison.OrdinalIgnoreCase) >= 0);
        if (target == null) { Console.WriteLine("Programa não encontrado."); return 1; }
        Console.WriteLine("Programa: " + target.DisplayName + "  (" + target.RegistrySubKey + ")");
        var snap = ProgramSnapshot.Capture(target, Console.WriteLine);
        Console.WriteLine("InstallDir: " + (snap.InstallDir ?? "(nenhuma)") + (snap.InstallDirInferred ? " [deduzida]" : ""));
        Console.WriteLine("ExtraDirs: " + string.Join(", ", snap.ExtraDirs));
        Console.WriteLine("Nomes: " + string.Join(", ", snap.StrongNames));
        Console.WriteLine("Exe principais: " + string.Join(", ", snap.ExeNames));
        Console.WriteLine("Exe (todos): " + snap.ExeFileNames.Count + " → " + string.Join(", ", snap.ExeFileNames.Take(15)));
        Console.WriteLine("Editor: " + snap.PublisherNorm + " / " + snap.PublisherFirst + "   MSI: " + snap.MsiGuid);
        var others = progs.Where(x => x.Id != target.Id).ToList();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var items = new LeftoverScanner(snap, others, false, m => Console.WriteLine("  .. " + m)).Scan();
        Console.WriteLine("Tempo da varredura: " + sw.ElapsedMilliseconds + " ms");
        Console.WriteLine();
        foreach (var it in items)
            Console.WriteLine("[" + it.Confidence + "] {" + it.Category + "} " + it.Display + "   <" + it.Reason + ">" + (it.Warning != null ? "  !! " + it.Warning : ""));
        return 0;
    }
}
