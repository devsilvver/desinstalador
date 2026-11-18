using System;
using Microsoft.Win32;

namespace DesinstaladorCompleto
{
    internal enum ProgramKind { Win32, Msi, StoreApp }

    /// <summary>Um programa instalado (entrada de "Programas e Recursos" ou aplicativo da Store).</summary>
    internal class InstalledProgram
    {
        public string DisplayName;
        public string DisplayVersion;
        public string Publisher;
        public string InstallLocation;
        public string UninstallString;
        public string QuietUninstallString;
        public string DisplayIcon;
        public string InstallDate;
        public long EstimatedSizeKB;
        public bool IsSystemComponent;
        public bool IsUpdate;
        public bool NoRemove;
        public ProgramKind Kind;
        public RegistryHive Hive;
        public string RegistrySubKey;   // sempre relativo à visão de 64 bits do registro
        public string KeyName;
        public string SourceLabel;

        // Aplicativos da Microsoft Store / UWP
        public string PackageFullName;
        public string PackageFamilyName;
        public string PackageName;
        public string LogoPath;

        public string Id
        {
            get
            {
                if (Kind == ProgramKind.StoreApp) return "appx:" + (PackageFullName ?? "").ToLowerInvariant();
                return (RegUtil.ShortHive(Hive) + "\\" + RegistrySubKey).ToLowerInvariant();
            }
        }

        public string KindText
        {
            get
            {
                switch (Kind)
                {
                    case ProgramKind.Msi: return "MSI";
                    case ProgramKind.StoreApp: return "Store / UWP";
                    default: return "Win32";
                }
            }
        }

        public override string ToString() { return DisplayName; }
    }

    internal enum LeftoverType { Folder, File, RegistryKey, RegistryValue, PathEntry, Service, ScheduledTask }

    /// <summary>Alta = certamente do programa; Média = provável; Baixa = precisa de revisão (vem desmarcado).</summary>
    internal enum Confidence { Alta = 0, Media = 1, Baixa = 2 }

    /// <summary>Um resquício encontrado depois da desinstalação.</summary>
    internal class LeftoverItem
    {
        public LeftoverType Type;
        public string Category;
        public string Path;          // arquivo/pasta, subchave do registro, nome do serviço ou da tarefa
        public string Display;       // texto mostrado ao usuário
        public RegistryHive Hive;
        public string ValueName;
        public string Data;          // entrada da variável PATH a remover
        public string TaskFile;
        public string ServiceImage;
        public string Reason;
        public string Warning;
        public Confidence Confidence;
        public long SizeBytes = -1;
        public bool DeleteOnlyIfEmpty;
        public bool Deleted;
        public bool Marked;          // marcado para exclusão na interface

        public bool IsRegistry
        {
            get { return Type == LeftoverType.RegistryKey || Type == LeftoverType.RegistryValue || Type == LeftoverType.PathEntry; }
        }

        public string Key
        {
            get
            {
                return (Type + "|" + (IsRegistry ? RegUtil.ShortHive(Hive) : "") + "|" + Path + "|" + (ValueName ?? "") + "|" + (Data ?? "")).ToLowerInvariant();
            }
        }
    }

    internal static class Categories
    {
        public const string Pastas = "Arquivos e pastas do programa";
        public const string Atalhos = "Atalhos";
        public const string Temporarios = "Temporários, cache e relatórios de erro";
        public const string Registro = "Chaves de registro";
        public const string Associacoes = "Associações de arquivos, COM e menu de contexto";
        public const string Inicializacao = "Inicialização automática";
        public const string Servicos = "Serviços";
        public const string Tarefas = "Tarefas agendadas";
        public const string Firewall = "Regras de firewall";
        public const string Ambiente = "Variáveis de ambiente";
        public const string Rastros = "Rastros de uso no registro";

        public static readonly string[] Order =
        {
            Pastas, Atalhos, Temporarios, Registro, Associacoes, Inicializacao, Servicos, Tarefas, Firewall, Ambiente, Rastros
        };

        public static int IndexOf(string category)
        {
            int i = Array.IndexOf(Order, category);
            return i < 0 ? Order.Length : i;
        }
    }

    internal class UninstallOptions
    {
        public bool CreateRestorePoint;
        public bool Silent;
    }
}
