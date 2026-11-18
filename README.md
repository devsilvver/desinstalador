# Desinstalador Completo

Programa para Windows que desinstala **qualquer programa** (instalado por terceiros, MSI ou aplicativo da
Microsoft Store/UWP do Windows) e depois procura e remove **todos os resquícios**: pastas, arquivos
temporários, chaves e valores do registro, atalhos, serviços, tarefas agendadas, regras de firewall,
entradas na variável PATH, associações de arquivos e rastros de uso do Windows.

**Nada é excluído sem você confirmar.** E pastas de projetos ou arquivos pessoais nunca são varridas.

## Download

**[⬇️ Baixar a versão mais recente (DesinstaladorCompleto.exe)](https://github.com/devsilvver/desinstalador/releases/latest/download/DesinstaladorCompleto.exe)**

Também disponível na [página de Releases](https://github.com/devsilvver/desinstalador/releases). Não precisa instalar: é um
único arquivo portátil. Basta baixar e abrir.

**Requisitos**: Windows 10 ou 11 (usa o .NET Framework 4.5+, que já vem no Windows) e permissão de administrador.

> Como o executável não tem assinatura digital, o Windows SmartScreen pode exibir "O Windows protegeu o
> computador". Clique em **Mais informações → Executar assim mesmo**. Se preferir, compile você mesmo a
> partir do código (veja [Compilar](#compilar)).

## Como usar

1. Abra o `DesinstaladorCompleto.exe` baixado (o Windows pedirá permissão de administrador).
2. Encontre o programa: digite na busca ou use os filtros **Todos / Programas / Microsoft Store**.
3. Clique no programa. O painel à direita mostra os detalhes e os botões **Desinstalar**,
   **Remoção forçada** e **Abrir pasta de instalação**. Dois cliques ou Enter também desinstalam.
4. Confirme. Por padrão um ponto de restauração do sistema é criado antes.
5. O assistente mostra as etapas: **Desinstalar → Procurar resquícios → Revisar e excluir**.
   Siga as instruções da janela do desinstalador do programa.
6. Revise a lista de resquícios (agrupada por categoria, com etiquetas de confiança), desmarque o que
   **não** quer excluir e clique em **Excluir selecionados**. Uma nova confirmação resume o que será removido.

**Atalhos de teclado**: `Ctrl+F` pesquisar • `F5` atualizar • `Enter`/`Delete` desinstalar o selecionado •
na lista de resquícios, `Espaço` marca/desmarca e `Ctrl+A` seleciona todos. Começar a digitar com a lista
em foco já pesquisa.

**Remoção forçada**: para programas com desinstalador quebrado ou ausente. Pula o desinstalador e faz
apenas a varredura de resquícios (também com revisão e confirmação).

## Níveis de confiança

| Nível | Significado | Vem marcado? |
|-------|-------------|--------------|
| **Alta** | Certamente é do programa (ex.: aponta para a pasta de instalação dele) | Sim |
| **Média** | Muito provavelmente é do programa (ex.: pasta com o nome do executável) | Sim |
| **Baixa** | Precisa da sua revisão: nome parecido, pasta compartilhada com outro programa, contém projetos ou dados pessoais | **Não** |

Itens com aviso aparecem em vermelho. Passe o mouse para ver o motivo, ou dê dois cliques para abrir o local.

## Proteções de segurança

- **Só locais conhecidos de instalação são varridos**: Arquivos de Programas, AppData (Roaming/Local/LocalLow),
  ProgramData, Menu Iniciar, atalhos da Área de Trabalho e da barra de tarefas, Temp, Prefetch e relatórios
  de erro. **Documentos, Downloads, Área de Trabalho (exceto atalhos), OneDrive, `source\repos`, pastas de
  projetos etc. nunca são varridos nem excluídos.**
  Exemplo: ao remover o VS Code, saem `AppData\Roaming\Code`, `%USERPROFILE%\.vscode`, a pasta do programa,
  o menu de contexto, as entradas do PATH e as associações de arquivos. As pastas `.vscode` dentro dos seus
  projetos **não são tocadas**.
- Pastas com sinais de projeto (`.git`, `.sln`, `.csproj`, `htdocs`, `www`...) ou de dados pessoais
  (perfis de navegador/e-mail, `.pst`, `.kdbx`...) vêm **desmarcadas** e com aviso.
- Se outro programa instalado usa a mesma pasta ou o mesmo nome, o item vem desmarcado. Pastas que contêm
  a instalação de outro programa são ignoradas.
- Lista fixa de locais protegidos: pasta do Windows, raízes de unidades, perfis de usuário, pastas
  especiais, chaves de sistema do registro (`SOFTWARE\Microsoft\Windows`, `Classes\*`, extensões de
  arquivo...). Toda exclusão é validada de novo no momento de excluir.
- O registro só é alterado em locais conhecidos de programas. A variável PATH nunca é apagada inteira:
  só a entrada do programa é removida.
- **Backup automático**: antes de excluir, todas as chaves/valores do registro envolvidos são exportados
  em `.reg` (e as tarefas agendadas em `.xml`) para
  `%LOCALAPPDATA%\DesinstaladorCompleto\Backups\<data>_<programa>\`. Para restaurar, dê dois cliques no `.reg`.
- Arquivos e pastas vão para a **Lixeira** por padrão.
- Arquivos em uso podem ser agendados para exclusão na próxima reinicialização (com confirmação).

## O que é verificado

| Categoria | Detalhes |
|-----------|----------|
| Arquivos e pastas | Pasta de instalação, pastas com o nome do programa/editor em Arquivos de Programas, AppData, ProgramData e VirtualStore; pastas ocultas no perfil (`.programa`) |
| Atalhos | Menu Iniciar (usuário e todos), Área de Trabalho, barra de tarefas, Inicialização rápida, atalhos quebrados |
| Temporários | `%TEMP%`, `C:\Windows\Temp`, Prefetch, relatórios de erro (WER) e crash dumps |
| Registro | Entrada de desinstalação, `HKCU/HKLM\SOFTWARE\[Editor\]Programa` (32 e 64 bits), App Paths |
| Associações | ProgIDs, protocolos de URL, "Abrir com", componentes COM (CLSID), menu de contexto, extensões do Shell, aplicativos padrão |
| Inicialização | Chaves Run/RunOnce e o estado no Gerenciador de Tarefas |
| Serviços | Serviços cujo executável está na pasta do programa (parados e removidos com `sc delete`) |
| Tarefas agendadas | Tarefas que executam o programa (exceto as do Windows) |
| Firewall | Regras do Firewall do Windows para os executáveis do programa |
| Ambiente | Entradas no PATH e variáveis que apontam para o programa |
| Rastros | MuiCache, Assistente de Compatibilidade, FeatureUsage, Installer\Folders, SharedDLLs |

## Compilar

Não precisa instalar nada: usa o compilador C# que já vem no Windows (.NET Framework 4.x).

```bat
build.bat
```

Gera `bin\DesinstaladorCompleto.exe`. Para compilar também as ferramentas de teste:

```bat
build.bat test
```

- `bin\ScanTest.exe "nome"`: varredura **somente leitura** dos resquícios de um programa instalado (não desinstala nem exclui nada).
- `bin\E2ETest.exe`: teste de ponta a ponta com um programa **falso** criado só no seu perfil. Ele instala, desinstala, varre, exclui e confere o resultado.

## Limitações

- Os dados do perfil considerados são os do usuário que executa o programa. Se você confirmar o UAC com
  **outra** conta de administrador, o HKCU e o AppData analisados serão os dessa conta.
- Componentes essenciais do Windows (aplicativos marcados como não removíveis) não podem ser desinstalados.
- Alguns itens protegidos pelo sistema (ex.: chaves pertencentes ao TrustedInstaller) podem falhar na
  exclusão. Eles são listados no resultado.

## Estrutura

```
src/
  Program.cs           Ponto de entrada (exige administrador)
  MainForm.cs          Lista de programas instalados
  ConfirmDialog.cs     Confirmação inicial (ponto de restauração, modo silencioso)
  UninstallForm.cs     Assistente: desinstala → varre → revisão → exclusão
  ProgramScanner.cs    Lista programas (registro + Microsoft Store)
  ProgramSnapshot.cs   Captura pasta, executáveis e nomes do programa ANTES de desinstalar
  Uninstaller.cs       Executa o desinstalador e espera todos os processos filhos (Job Object)
  LeftoverScanner.cs   Procura resquícios (somente leitura)
  Cleaner.cs           Backup + exclusão dos itens confirmados
  SafetyGuard.cs       Regras do que NUNCA pode ser excluído
  Util.cs, RegUtil.cs, SystemHelpers.cs, NativeMethods.cs
tests/
  ScanTest.cs, E2ETest.cs
```
