using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DesinstaladorCompleto
{
    /// <summary>
    /// Assistente de desinstalação: executa o desinstalador, procura resquícios e mostra a lista para o usuário
    /// marcar o que deseja excluir. Só exclui depois de uma confirmação explícita.
    /// </summary>
    internal class UninstallForm : Form
    {
        private static int S(int px) { return Theme.S(px); }

        private readonly InstalledProgram program;
        private readonly bool forced;
        private readonly UninstallOptions options;

        private readonly Label lblTitle, lblStep;
        private readonly Stepper stepper;
        private readonly ThinProgress progress;
        private readonly Panel listHeader;
        private readonly Label lblListTitle, lblListSub;
        private readonly ToggleSwitch tglRecycle;
        private readonly ModernListView lv;
        private readonly Panel overlay;
        private readonly GlyphBadge overlayBadge;
        private readonly Label overlayTitle, overlayText;
        private readonly ModernButton overlayButton;
        private readonly Panel logPanel;
        private readonly TextBox txtLog;
        private readonly Panel footer;
        private readonly ModernButton btnAll, btnNone, btnHigh, btnOpen, btnLog, btnStop, btnDelete, btnClose;
        private readonly Label lblSummary;

        private CancellationTokenSource waitCts;
        private bool runningUninstaller, deleting, scanning, working, abandoned;
        private string lastBackupDir;

        public UninstallForm(InstalledProgram program, bool forced, UninstallOptions options, Bitmap programIcon)
        {
            this.program = program;
            this.forced = forced;
            this.options = options ?? new UninstallOptions();

            Text = (forced ? "Remoção forçada — " : "Desinstalando — ") + program.DisplayName;
            AutoScaleMode = AutoScaleMode.None;
            Font = Theme.Body;
            BackColor = Theme.Background;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(S(1160), S(800));
            MinimumSize = new Size(S(920), S(620));
            MinimizeBox = false;
            ShowInTaskbar = false;
            KeyPreview = true;
            if (Program.AppIcon != null) Icon = Program.AppIcon;

            // ================================================== cabeçalho
            var header = new Panel { Dock = DockStyle.Top, Height = S(100), BackColor = Theme.Surface };
            header.Paint += (s, e) => { using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, 0, header.Height - 1, header.Width, header.Height - 1); };
            var pic = new PictureBox { Size = new Size(S(52), S(52)), Location = new Point(S(24), S(24)), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Surface };
            pic.Image = programIcon ?? Theme.GlyphBitmap(Glyphs.App, S(52), Theme.TextSubtle);
            lblTitle = new Label { Text = program.DisplayName, Font = Theme.Heading, ForeColor = Theme.Text, AutoSize = false, AutoEllipsis = true, UseMnemonic = false, Location = new Point(S(92), S(22)) };
            lblStep = new Label { Text = "Preparando...", ForeColor = Theme.TextMuted, AutoSize = false, AutoEllipsis = true, Location = new Point(S(92), S(52)) };
            stepper = new Stepper { Steps = new[] { forced ? "Preparar" : "Desinstalar", "Procurar resquícios", "Revisar e excluir" } };
            header.Controls.AddRange(new Control[] { pic, lblTitle, lblStep, stepper });
            header.Resize += (s, e) =>
            {
                stepper.Location = new Point(header.Width - stepper.Width - S(28), (header.Height - stepper.Height) / 2);
                int w = Math.Max(S(200), stepper.Left - S(92) - S(24));
                lblTitle.SetBounds(S(92), S(22), w, S(28));
                lblStep.SetBounds(S(92), S(54), w, S(22));
            };
            progress = new ThinProgress { Dock = DockStyle.Top };

            // ================================================== corpo
            var body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Padding = new Padding(S(20), S(16), S(20), S(14)) };
            var card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(S(6), S(4), S(6), S(6)) };

            listHeader = new Panel { Dock = DockStyle.Top, Height = S(64), BackColor = Theme.Surface };
            lblListTitle = new Label { Text = "Resquícios encontrados", Font = Theme.Heading, ForeColor = Theme.Text, AutoSize = true, Location = new Point(S(14), S(10)) };
            lblListSub = new Label { Text = "Aguardando...", ForeColor = Theme.TextMuted, Font = Theme.Small, AutoSize = true, Location = new Point(S(15), S(38)) };
            tglRecycle = new ToggleSwitch { Text = "Enviar para a Lixeira (recomendado)", Checked = true };
            listHeader.Controls.AddRange(new Control[] { lblListTitle, lblListSub, tglRecycle });
            listHeader.Resize += (s, e) => tglRecycle.Location = new Point(listHeader.Width - tglRecycle.Width - S(14), S(18));
            listHeader.Paint += PaintLegend;

            lv = new ModernListView { Dock = DockStyle.Fill, ShowGroups = true, ShowItemToolTips = true, HeaderStyle = ColumnHeaderStyle.Nonclickable };
            lv.SetRowHeight(S(50));
            lv.Columns.Add("Item", S(600));
            lv.Columns.Add("Confiança", S(110));
            lv.Columns.Add("Tamanho", S(100), HorizontalAlignment.Right);

            overlay = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
            overlayBadge = new GlyphBadge { Size = new Size(S(72), S(72)) };
            overlayTitle = new Label { Font = Theme.Heading, ForeColor = Theme.Text, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter };
            overlayText = new Label { ForeColor = Theme.TextMuted, AutoSize = false, TextAlign = ContentAlignment.TopCenter, UseMnemonic = false };
            overlayButton = new ModernButton { Kind = ButtonKind.Secondary, Glyph = Glyphs.FolderOpen, Text = "Abrir pasta do backup", AutoSize = true, Visible = false };
            overlayButton.Click += (s, e) => { if (lastBackupDir != null && Directory.Exists(lastBackupDir)) Process.Start("explorer.exe", "\"" + lastBackupDir + "\""); };
            overlay.Controls.AddRange(new Control[] { overlayBadge, overlayTitle, overlayText, overlayButton });
            overlay.Resize += (s, e) => LayoutOverlay();

            card.Controls.Add(lv);
            card.Controls.Add(overlay);
            card.Controls.Add(listHeader);
            overlay.BringToFront();

            logPanel = new Panel { Dock = DockStyle.Bottom, Height = S(170), BackColor = Theme.Background, Padding = new Padding(0, S(12), 0, 0), Visible = false };
            var logCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(S(12), S(8), S(8), S(8)) };
            txtLog = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 9F), BackColor = Theme.Surface, ForeColor = Theme.TextMuted, WordWrap = false
            };
            logCard.Controls.Add(txtLog);
            logPanel.Controls.Add(logCard);

            body.Controls.Add(card);
            body.Controls.Add(logPanel);

            // ================================================== rodapé
            footer = new Panel { Dock = DockStyle.Bottom, Height = S(72), BackColor = Theme.Surface };
            footer.Paint += (s, e) => { using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, 0, 0, footer.Width, 0); };
            btnAll = new ModernButton { Kind = ButtonKind.Ghost, Glyph = Glyphs.SelectAll, Text = "Marcar todos", AutoSize = true };
            btnNone = new ModernButton { Kind = ButtonKind.Ghost, Glyph = Glyphs.ClearSelection, Text = "Desmarcar", AutoSize = true };
            btnHigh = new ModernButton { Kind = ButtonKind.Ghost, Glyph = Glyphs.Accept, Text = "Só confiança alta", AutoSize = true };
            btnOpen = new ModernButton { Kind = ButtonKind.Ghost, Glyph = Glyphs.FolderOpen, Text = "Abrir local", AutoSize = true };
            btnLog = new ModernButton { Kind = ButtonKind.Ghost, Glyph = Glyphs.List, Text = "Mostrar atividades", AutoSize = true };
            btnStop = new ModernButton { Kind = ButtonKind.Warning, Glyph = Glyphs.Stop, Text = "Parar de aguardar", AutoSize = true, Visible = false };
            btnDelete = new ModernButton { Kind = ButtonKind.Danger, Glyph = Glyphs.Delete, Text = "Excluir selecionados", AutoSize = true, PreferredHeight = S(42) };
            btnClose = new ModernButton { Kind = ButtonKind.Secondary, Text = "Fechar", AutoSize = true, PreferredHeight = S(42) };
            lblSummary = new Label { AutoSize = true, Font = Theme.BodyBold, ForeColor = Theme.Text, BackColor = Theme.Surface };
            footer.Controls.AddRange(new Control[] { btnAll, btnNone, btnHigh, btnOpen, btnLog, btnStop, lblSummary, btnDelete, btnClose });
            footer.Resize += (s, e) => LayoutFooter();

            Controls.Add(body);
            Controls.Add(footer);
            Controls.Add(progress);
            Controls.Add(header);

            var tips = new ToolTip();
            tips.SetToolTip(btnHigh, "Marca apenas os itens que com certeza pertencem ao programa");
            tips.SetToolTip(btnOpen, "Abre o local do item selecionado (Explorer, Editor do Registro...)");
            tips.SetToolTip(btnStop, "Use se o desinstalador já terminou mas a janela continua aguardando");
            tips.SetToolTip(tglRecycle, "Arquivos e pastas vão para a Lixeira e podem ser recuperados");

            // ================================================== eventos
            btnAll.Click += (s, e) => SetMarks(it => true);
            btnNone.Click += (s, e) => SetMarks(it => false);
            btnHigh.Click += (s, e) => SetMarks(it => it.Confidence == Confidence.Alta);
            btnOpen.Click += (s, e) => OpenSelectedLocation();
            btnLog.Click += (s, e) => ToggleLog(!logPanel.Visible);
            btnStop.Click += (s, e) => { if (waitCts != null) waitCts.Cancel(); };
            btnDelete.Click += OnDeleteClick;
            btnClose.Click += (s, e) => Close();
            lv.DrawSubItem += OnDrawCell;
            lv.Resize += (s, e) => lv.FillFirstColumn();
            lv.MouseDown += OnListMouseDown;
            lv.MouseDoubleClick += (s, e) => { if (!InCheckZone(e.X)) OpenSelectedLocation(); };
            lv.KeyDown += OnListKeyDown;
            FormClosing += OnFormClosing;
            Shown += OnShown;

            SetListButtons(false);
            ShowOverlay(Glyphs.Info, Theme.AccentSoft, Theme.Accent, "Preparando", "Coletando informações do programa...");
        }

        // ------------------------------------------------------------------ layout

        private void LayoutFooter()
        {
            int y = (footer.Height - S(40)) / 2;
            int x = S(16);
            foreach (ModernButton b in new[] { btnAll, btnNone, btnHigh, btnOpen, btnLog })
            {
                b.Size = b.GetPreferredSize(Size.Empty);
                b.Location = new Point(x, (footer.Height - b.Height) / 2);
                x = b.Right + S(2);
            }
            int leftEnd = x;
            int r = footer.Width - S(20);
            foreach (ModernButton b in new[] { btnClose, btnDelete, btnStop })
            {
                if (!b.Visible) continue;
                b.Size = b.GetPreferredSize(Size.Empty);
                b.Location = new Point(r - b.Width, (footer.Height - b.Height) / 2);
                r = b.Left - S(10);
            }
            lblSummary.Location = new Point(r - lblSummary.Width - S(6), (footer.Height - lblSummary.Height) / 2);
            lblSummary.Visible = lblSummary.Left > leftEnd + S(8);
        }

        private void LayoutOverlay()
        {
            int w = overlay.ClientSize.Width;
            int y = Math.Max(S(20), overlay.ClientSize.Height / 2 - S(120));
            overlayBadge.Location = new Point((w - overlayBadge.Width) / 2, y);
            overlayTitle.SetBounds(S(20), overlayBadge.Bottom + S(18), w - S(40), S(32));
            overlayText.SetBounds(S(40), overlayTitle.Bottom + S(4), w - S(80), S(66));
            overlayButton.Size = overlayButton.GetPreferredSize(Size.Empty);
            overlayButton.Location = new Point((w - overlayButton.Width) / 2, overlayText.Bottom + S(8));
        }

        private void PaintLegend(object sender, PaintEventArgs e)
        {
            if (!lv.Visible || lv.Items.Count == 0) return;
            Graphics g = e.Graphics;
            int x = tglRecycle.Left - S(24);
            var legend = new[]
            {
                Tuple.Create("Baixa = verifique", Color.FromArgb(243, 244, 246), Theme.TextMuted),
                Tuple.Create("Média = provável", Theme.WarningLight, Theme.WarningDark),
                Tuple.Create("Alta = do programa", Theme.SuccessLight, Theme.SuccessDark)
            };
            foreach (var l in legend)
            {
                int w = TextRenderer.MeasureText(g, l.Item1, Theme.SmallBold, Size.Empty, TextFormatFlags.NoPadding).Width + S(16);
                x -= w;
                if (x < lblListTitle.Right + S(20)) break;
                Theme.DrawPill(g, new Rectangle(x, S(16), w, S(30)), l.Item1, l.Item2, l.Item3);
                x -= S(6);
            }
        }

        // ------------------------------------------------------------------ estados visuais

        private void ShowOverlay(string glyph, Color circle, Color fg, string title, string text, bool backupButton = false)
        {
            overlayBadge.Set(glyph, circle, fg);
            overlayTitle.Text = title;
            overlayText.Text = text;
            overlayButton.Visible = backupButton;
            overlay.Visible = true;
            overlay.BringToFront();
            LayoutOverlay();
        }

        private void HideOverlay()
        {
            overlay.Visible = false;
        }

        private void ToggleLog(bool show)
        {
            logPanel.Visible = show;
            btnLog.Text = show ? "Ocultar atividades" : "Mostrar atividades";
            LayoutFooter();
        }

        private void Log(string msg)
        {
            if (IsDisposed) return;
            txtLog.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg + Environment.NewLine);
        }

        private void LogFromThread(string msg)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                if (InvokeRequired) BeginInvoke(new Action<string>(Log), msg);
                else Log(msg);
            }
            catch { }
        }

        private void SetStep(string text, bool busy)
        {
            lblStep.Text = text;
            progress.Running = busy;
        }

        private void Finish(string text)
        {
            SetStep(text, false);
            btnStop.Visible = false;
            LayoutFooter();
        }

        // ------------------------------------------------------------------ fluxo principal

        private async void OnShown(object sender, EventArgs e)
        {
            bool stillInstalled = false;
            working = true;
            try
            {
                stepper.Current = 0;
                SetStep("Coletando informações do programa...", true);
                Log((forced ? "Remoção forçada de: " : "Desinstalando: ") + program.DisplayName);
                ProgramSnapshot snapshot = await Task.Run(() => ProgramSnapshot.Capture(program, LogFromThread));
                Log("Pasta de instalação: " + (snapshot.InstallDir ?? "(não identificada)") + (snapshot.InstallDirInferred ? " (deduzida)" : ""));

                if (options.CreateRestorePoint)
                {
                    SetStep("Criando ponto de restauração do sistema...", true);
                    ShowOverlay(Glyphs.Shield, Theme.SuccessLight, Theme.SuccessDark, "Criando ponto de restauração",
                        "Isso permite desfazer as alterações pelo Windows, se necessário. Pode levar alguns minutos.");
                    Log("Criando ponto de restauração...");
                    string err = await Task.Run(() => SystemRestore.Create("Antes de remover " + program.DisplayName));
                    if (err == null) Log("Ponto de restauração criado (o Windows pode ignorar se já houver um das últimas 24 horas).");
                    else
                    {
                        Log("Não foi possível criar o ponto de restauração: " + err);
                        if (MessageBox.Show(this, "Não foi possível criar o ponto de restauração:\n" + err + "\n\nDeseja continuar mesmo assim?",
                                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                        {
                            Cancelled("Operação cancelada. Nada foi alterado.");
                            return;
                        }
                    }
                }

                if (!forced)
                {
                    if (snapshot.InstallDir != null)
                    {
                        string dir = snapshot.InstallDir;
                        List<Process> procs = await Task.Run(() => ProcessUtil.FindUnder(new[] { dir }));
                        if (procs.Count > 0)
                        {
                            DialogResult ans = MessageBox.Show(this,
                                "Os seguintes processos do programa estão em execução:\n\n" + ProcessUtil.Describe(procs) +
                                "\nRecomenda-se fechá-los antes de desinstalar. Deseja encerrá-los agora?\n\n" +
                                "Sim = encerrar   •   Não = continuar sem encerrar   •   Cancelar = desistir",
                                Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                            if (ans == DialogResult.Cancel) { Cancelled("Operação cancelada. Nada foi alterado."); return; }
                            if (ans == DialogResult.Yes) await Task.Run(() => ProcessUtil.Kill(procs, LogFromThread));
                        }
                    }

                    SetStep("Executando o desinstalador do programa...", true);
                    ShowOverlay(Glyphs.Delete, Theme.AccentSoft, Theme.Accent, "Desinstalando",
                        options.Silent ? "A desinstalação silenciosa está em andamento. Aguarde..."
                                       : "Siga as instruções na janela do desinstalador do programa.\nQuando ele terminar, a busca por resquícios começa automaticamente.");
                    waitCts = new CancellationTokenSource();
                    btnStop.Visible = true;
                    LayoutFooter();
                    runningUninstaller = true;
                    bool silent = options.Silent;
                    CancellationToken token = waitCts.Token;
                    UninstallResult res = await Task.Run(() => UninstallRunner.Run(program, silent, LogFromThread, token));
                    runningUninstaller = false;
                    if (abandoned || IsDisposed) return;   // a janela foi fechada durante a espera
                    btnStop.Visible = false;
                    LayoutFooter();

                    if (!res.Started) Log("ERRO: " + (res.Error ?? "o desinstalador não pôde ser iniciado."));
                    else if (!res.StoppedWaiting)
                        Log("Desinstalador finalizado (código de saída " + res.ExitCode + ")." + (string.IsNullOrEmpty(res.Error) ? "" : " " + res.Error));
                    if (res.ExitCode == 3010 || res.ExitCode == 1641) Log("O desinstalador informou que é necessário reiniciar o computador para concluir.");

                    SetStep("Verificando se o programa foi removido...", true);
                    stillInstalled = await Task.Run(() => ProgramScanner.IsStillInstalled(program));
                    if (stillInstalled)
                    {
                        Log("O programa ainda aparece como instalado.");
                        if (MessageBox.Show(this,
                                "O programa ainda aparece como instalado (a desinstalação pode ter sido cancelada, ter falhado ou exigir reinicialização).\n\n" +
                                "Deseja procurar resquícios mesmo assim? Os itens serão marcados com mais cautela.",
                                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                        {
                            stepper.MarkFailed();
                            Cancelled("A desinstalação não foi concluída. Nenhum resquício foi removido.");
                            return;
                        }
                    }
                    else Log("O programa não aparece mais na lista de programas instalados.");
                }

                // Varredura de resquícios
                stepper.Current = 1;
                SetStep("Procurando resquícios...", true);
                ShowOverlay(Glyphs.Search, Theme.AccentSoft, Theme.Accent, "Procurando resquícios",
                    "Verificando pastas, arquivos temporários, registro, atalhos, serviços, tarefas agendadas e firewall.");
                scanning = true;
                InstalledProgram target = program;
                bool caution = stillInstalled;
                List<LeftoverItem> found = await Task.Run(() =>
                {
                    List<InstalledProgram> others = ProgramScanner.GetWin32Programs().Where(x => x.Id != target.Id).ToList();
                    return new LeftoverScanner(snapshot, others, caution, LogFromThread).Scan();
                });
                scanning = false;
                ShowResults(found);
            }
            catch (Exception ex)
            {
                runningUninstaller = false;
                scanning = false;
                if (!abandoned && !IsDisposed)
                {
                    Log("ERRO: " + ex.Message);
                    stepper.MarkFailed();
                    ShowOverlay(Glyphs.Warning, Theme.DangerLight, Theme.Danger, "Ocorreu um erro", ex.Message);
                    ToggleLog(true);
                    Finish("Ocorreu um erro.");
                }
            }
            finally
            {
                working = false;
            }
        }

        private void Cancelled(string text)
        {
            ShowOverlay(Glyphs.Info, Theme.Background, Theme.TextMuted, "Operação interrompida", text);
            lblListSub.Text = text;
            Finish(text);
        }

        private void ShowResults(List<LeftoverItem> found)
        {
            lv.BeginUpdate();
            lv.Items.Clear();
            lv.Groups.Clear();
            var groups = new Dictionary<string, ListViewGroup>();
            foreach (LeftoverItem it in found)
            {
                ListViewGroup g;
                if (!groups.TryGetValue(it.Category, out g))
                {
                    g = new ListViewGroup(it.Category, it.Category);
                    groups[it.Category] = g;
                    lv.Groups.Add(g);
                }
                it.Marked = it.Confidence != Confidence.Baixa;
                var li = new ListViewItem(it.Display, g) { Tag = it };
                li.SubItems.Add(ConfidenceText(it.Confidence));
                li.SubItems.Add(it.SizeBytes >= 0 ? FsUtil.FormatSize(it.SizeBytes) : "");
                li.ToolTipText = it.Display + "\n" + it.Reason + (it.Warning != null ? "\nAtenção: " + it.Warning : "");
                lv.Items.Add(li);
            }
            foreach (var kv in groups)
                kv.Value.Header = kv.Key + "  (" + found.Count(i => i.Category == kv.Key) + ")";
            lv.EndUpdate();
            lv.FillFirstColumn();

            if (found.Count == 0)
            {
                stepper.Current = 3;
                ShowOverlay(Glyphs.Check, Theme.SuccessLight, Theme.Success, "Tudo limpo!", "Nenhum resquício foi encontrado. O programa foi removido por completo.");
                lblListSub.Text = "Nenhum resquício encontrado.";
                Finish("Concluído: nenhum resquício encontrado.");
                SetListButtons(false);
            }
            else
            {
                stepper.Current = 2;
                HideOverlay();
                Finish("Varredura concluída. Revise a lista e clique em \"Excluir selecionados\".");
                SetListButtons(true);
                lv.Focus();
            }
            listHeader.Invalidate();
            UpdateSummary();
        }

        private static string ConfidenceText(Confidence c)
        {
            switch (c)
            {
                case Confidence.Alta: return "Alta";
                case Confidence.Media: return "Média";
                default: return "Baixa";
            }
        }

        // ------------------------------------------------------------------ lista

        private static string CategoryGlyph(string category, out Color color)
        {
            color = Theme.Accent;
            switch (category)
            {
                case Categories.Pastas: color = Theme.Warning; return Glyphs.Folder;
                case Categories.Atalhos: return Glyphs.Link;
                case Categories.Temporarios: color = Theme.TextMuted; return Glyphs.History;
                case Categories.Registro: color = Theme.Indigo; return Glyphs.Settings;
                case Categories.Associacoes: color = Theme.Indigo; return Glyphs.Tag;
                case Categories.Inicializacao: color = Theme.Success; return "\uE768";
                case Categories.Servicos: color = Theme.TextMuted; return Glyphs.Repair;
                case Categories.Tarefas: return Glyphs.Calendar;
                case Categories.Firewall: color = Theme.Danger; return Glyphs.Shield;
                case Categories.Ambiente: return Glyphs.Code;
                default: color = Theme.TextMuted; return Glyphs.Clock;
            }
        }

        private bool InCheckZone(int x)
        {
            return x >= 0 && x < S(48);
        }

        private Rectangle CheckBoxRect(Rectangle cell)
        {
            int sz = S(20);
            return new Rectangle(cell.X + S(16), cell.Y + (cell.Height - sz) / 2, sz, sz);
        }

        private void OnDrawCell(object sender, DrawListViewSubItemEventArgs e)
        {
            var it = (LeftoverItem)e.Item.Tag;
            Rectangle b = lv.CellBounds(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            const TextFormatFlags common = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            switch (e.ColumnIndex)
            {
                case 0:
                {
                    Rectangle cb = CheckBoxRect(b);
                    if (it.Marked)
                    {
                        Theme.FillRound(g, cb, S(5), Theme.Accent);
                        Theme.DrawGlyph(g, Glyphs.Check, 9f, Color.White, cb);
                    }
                    else
                    {
                        Theme.FillRound(g, cb, S(5), Theme.Surface);
                        Theme.DrawRound(g, cb, S(5), e.ItemIndex == lv.HotIndex ? Theme.Accent : Theme.BorderStrong, 1.5f);
                    }
                    Color gc;
                    string glyph = CategoryGlyph(it.Category, out gc);
                    var gr = new Rectangle(cb.Right + S(14), b.Y, S(20), b.Height);
                    Theme.DrawGlyph(g, glyph, 12f, gc, gr);
                    int tx = gr.Right + S(12);
                    int mid = b.Y + b.Height / 2;
                    TextRenderer.DrawText(g, it.Display, Theme.Body, new Rectangle(tx, b.Y, b.Right - tx - S(8), mid - b.Y + S(1)),
                        it.Marked ? Theme.Text : Theme.TextMuted, common | TextFormatFlags.Bottom | TextFormatFlags.PathEllipsis);
                    string sub = it.Warning != null ? "⚠  " + it.Warning : it.Reason;
                    TextRenderer.DrawText(g, sub, Theme.Small, new Rectangle(tx, mid + S(3), b.Right - tx - S(8), b.Bottom - mid - S(3)),
                        it.Warning != null ? Theme.Danger : Theme.TextMuted, common | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
                    break;
                }
                case 1:
                {
                    Color bg, fg;
                    switch (it.Confidence)
                    {
                        case Confidence.Alta: bg = Theme.SuccessLight; fg = Theme.SuccessDark; break;
                        case Confidence.Media: bg = Theme.WarningLight; fg = Theme.WarningDark; break;
                        default: bg = Color.FromArgb(243, 244, 246); fg = Theme.TextMuted; break;
                    }
                    Theme.DrawPill(g, new Rectangle(b.X + S(12), b.Y, b.Width - S(16), b.Height), e.SubItem.Text, bg, fg);
                    break;
                }
                default:
                    TextRenderer.DrawText(g, e.SubItem.Text, Theme.Body, new Rectangle(b.X, b.Y, b.Width - S(16), b.Height), Theme.TextMuted,
                        common | TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
                    break;
            }
        }

        private void OnListMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !InCheckZone(e.X)) return;
            ListViewItem li = lv.GetItemAt(e.X, e.Y);
            if (li == null) return;
            var it = (LeftoverItem)li.Tag;
            it.Marked = !it.Marked;
            lv.InvalidateRow(li.Index);
            UpdateSummary();
        }

        private void OnListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space && lv.SelectedItems.Count > 0)
            {
                var sel = lv.SelectedItems.Cast<ListViewItem>().Select(li => (LeftoverItem)li.Tag).ToList();
                bool mark = !sel.All(i => i.Marked);
                foreach (LeftoverItem i in sel) i.Marked = mark;
                lv.Invalidate();
                UpdateSummary();
                e.Handled = e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.A && e.Control)
            {
                foreach (ListViewItem li in lv.Items) li.Selected = true;
                e.Handled = true;
            }
        }

        private void SetMarks(Func<LeftoverItem, bool> predicate)
        {
            foreach (ListViewItem li in lv.Items)
            {
                var it = (LeftoverItem)li.Tag;
                it.Marked = predicate(it);
            }
            lv.Invalidate();
            UpdateSummary();
        }

        private List<LeftoverItem> MarkedItems()
        {
            return lv.Items.Cast<ListViewItem>().Select(li => (LeftoverItem)li.Tag).Where(i => i.Marked && !i.Deleted).ToList();
        }

        private void UpdateSummary()
        {
            List<LeftoverItem> marked = MarkedItems();
            int n = marked.Count;
            long bytes = marked.Where(i => i.SizeBytes > 0).Sum(i => i.SizeBytes);
            lblSummary.Text = "";
            if (lv.Items.Count > 0 && !overlay.Visible)
                lblListSub.Text = n + " de " + lv.Items.Count + " selecionado(s)" + (bytes > 0 ? " (" + FsUtil.FormatSize(bytes) + ")" : "") +
                                  "  •  Desmarque o que NÃO deseja excluir. Itens de confiança baixa vêm desmarcados.";
            btnDelete.Enabled = !deleting && n > 0 && btnAll.Enabled;
            btnDelete.Text = n > 0 ? "Excluir selecionados (" + n + ")" : "Excluir selecionados";
            LayoutFooter();
        }

        private void SetListButtons(bool enabled)
        {
            btnAll.Enabled = btnNone.Enabled = btnHigh.Enabled = btnOpen.Enabled = enabled;
            tglRecycle.Enabled = enabled;
            UpdateSummary();
        }

        // ------------------------------------------------------------------ exclusão

        private async void OnDeleteClick(object sender, EventArgs e)
        {
            List<LeftoverItem> sel = MarkedItems();
            if (sel.Count == 0) return;

            var sb = new StringBuilder();
            sb.AppendLine("Serão excluídos " + sel.Count + " item(ns):");
            sb.AppendLine();
            foreach (var g in sel.GroupBy(i => i.Category).OrderBy(g => Categories.IndexOf(g.Key)))
                sb.AppendLine("   • " + g.Key + ": " + g.Count());
            int risky = sel.Count(i => i.Confidence == Confidence.Baixa || i.Warning != null);
            if (risky > 0)
            {
                sb.AppendLine();
                sb.AppendLine("ATENÇÃO: você marcou " + risky + " item(ns) de baixa confiança ou com aviso.");
                sb.AppendLine("Confirme que eles não pertencem a outro programa nem são arquivos seus.");
            }
            sb.AppendLine();
            sb.AppendLine(tglRecycle.Checked ? "Arquivos e pastas irão para a Lixeira." : "Arquivos e pastas serão excluídos PERMANENTEMENTE.");
            sb.AppendLine("Antes da exclusão, será feito um backup (.reg) das chaves e valores do registro.");
            sb.AppendLine();
            sb.Append("Deseja excluir os itens selecionados?");
            if (MessageBox.Show(this, sb.ToString(), "Confirmar exclusão", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            var paths = sel.Where(i => i.Type == LeftoverType.Folder || i.Type == LeftoverType.File).Select(i => i.Path).ToList();
            List<Process> procs = await Task.Run(() => ProcessUtil.FindUnder(paths));
            if (procs.Count > 0)
            {
                DialogResult ans = MessageBox.Show(this,
                    "Os processos abaixo estão sendo executados a partir dos itens que serão excluídos:\n\n" + ProcessUtil.Describe(procs) +
                    "\nDeseja encerrá-los agora?\n\nSim = encerrar   •   Não = tentar excluir assim mesmo   •   Cancelar = voltar",
                    Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                if (ans == DialogResult.Cancel) return;
                if (ans == DialogResult.Yes) await Task.Run(() => ProcessUtil.Kill(procs, LogFromThread));
            }

            deleting = true;
            SetListButtons(false);
            btnClose.Enabled = false;
            SetStep("Excluindo os itens selecionados...", true);
            bool recycle = tglRecycle.Checked;
            string name = program.DisplayName;
            CleanReport rep;
            try
            {
                rep = await Task.Run(() => Cleaner.Execute(sel, recycle, name, LogFromThread));
            }
            catch (Exception ex)
            {
                deleting = false;
                btnClose.Enabled = true;
                SetListButtons(true);
                Log("ERRO: " + ex.Message);
                ToggleLog(true);
                Finish("Erro durante a exclusão: " + ex.Message);
                return;
            }
            deleting = false;
            btnClose.Enabled = true;
            lastBackupDir = rep.BackupDir;

            lv.BeginUpdate();
            foreach (ListViewItem li in lv.Items.Cast<ListViewItem>().ToList())
                if (((LeftoverItem)li.Tag).Deleted) lv.Items.Remove(li);
            foreach (ListViewGroup g in lv.Groups.Cast<ListViewGroup>().ToList())
                if (g.Items.Count == 0) lv.Groups.Remove(g);
                else g.Header = g.Name + "  (" + g.Items.Count + ")";
            lv.EndUpdate();
            SetListButtons(lv.Items.Count > 0);
            Log("Limpeza concluída: " + rep.Deleted + " excluído(s), " + rep.Errors.Count + " falha(s). Backup: " + rep.BackupDir);

            if (rep.Errors.Count == 0)
            {
                stepper.Current = 3;
                Finish("Limpeza concluída.");
                if (lv.Items.Count == 0)
                    ShowOverlay(Glyphs.Check, Theme.SuccessLight, Theme.Success, "Limpeza concluída!",
                        rep.Deleted + " item(ns) excluído(s)." + (rep.Kept > 0 ? " " + rep.Kept + " pasta(s)/chave(s) do editor mantida(s) porque não estavam vazias." : "") +
                        "\nO backup do registro foi salvo e pode ser restaurado com dois cliques nos arquivos .reg.", true);
                else
                {
                    MessageBox.Show(this, rep.Deleted + " item(ns) excluído(s).\n\nBackup do registro salvo em:\n" + rep.BackupDir, "Resultado",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            else
            {
                Finish("Limpeza concluída com " + rep.Errors.Count + " falha(s).");
                var msg = new StringBuilder();
                msg.AppendLine(rep.Deleted + " item(ns) excluído(s).");
                msg.AppendLine();
                msg.AppendLine(rep.Errors.Count + " item(ns) não puderam ser excluídos:");
                foreach (string err in rep.Errors.Take(8)) msg.AppendLine("   • " + err);
                if (rep.Errors.Count > 8) msg.AppendLine("   ... (veja as atividades)");
                msg.AppendLine();
                msg.AppendLine("Backup do registro salvo em:");
                msg.Append(rep.BackupDir);
                MessageBox.Show(this, msg.ToString(), "Resultado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                ToggleLog(true);
            }
            listHeader.Invalidate();

            if (rep.Remaining.Count > 0)
            {
                if (MessageBox.Show(this,
                        rep.Remaining.Count + " arquivo(s)/pasta(s) estão em uso e não puderam ser excluídos agora.\n\n" +
                        "Deseja agendar a exclusão para a próxima reinicialização do Windows?\n(Atenção: a exclusão na reinicialização é definitiva, sem Lixeira.)",
                        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                {
                    List<string> remaining = rep.Remaining.ToList();
                    await Task.Run(() => Cleaner.ScheduleOnReboot(remaining, LogFromThread));
                    MessageBox.Show(this, "Exclusão agendada. Os itens serão removidos quando o computador for reiniciado.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        // ------------------------------------------------------------------ abrir local

        private void OpenSelectedLocation()
        {
            if (lv.SelectedItems.Count == 0) return;
            var it = (LeftoverItem)lv.SelectedItems[0].Tag;
            try
            {
                switch (it.Type)
                {
                    case LeftoverType.Folder:
                        if (Directory.Exists(it.Path)) Process.Start("explorer.exe", "\"" + it.Path + "\"");
                        break;
                    case LeftoverType.File:
                        Process.Start("explorer.exe", "/select,\"" + it.Path + "\"");
                        break;
                    case LeftoverType.RegistryKey:
                    case LeftoverType.RegistryValue:
                    case LeftoverType.PathEntry:
                        OpenRegedit(it.Hive, it.Path);
                        break;
                    case LeftoverType.Service:
                        Process.Start("services.msc");
                        break;
                    case LeftoverType.ScheduledTask:
                        Process.Start("taskschd.msc");
                        break;
                }
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private static void OpenRegedit(RegistryHive hive, string subKey)
        {
            // O Editor do Registro abre na última chave visitada; definimos essa chave antes de abri-lo.
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Applets\Regedit"))
                k.SetValue("LastKey", RegUtil.LongHive(hive) + "\\" + subKey);
            Process.Start("regedit.exe", "/m");
        }

        // ------------------------------------------------------------------ fechamento

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (deleting)
            {
                MessageBox.Show(this, "Aguarde o término da exclusão.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                e.Cancel = true;
                return;
            }
            if (runningUninstaller)
            {
                if (MessageBox.Show(this, "O desinstalador ainda está em execução. Fechar esta janela não o interrompe, mas a varredura de resquícios não será feita.\n\nDeseja fechar mesmo assim?",
                        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                abandoned = true;
                if (waitCts != null) waitCts.Cancel();
                return;
            }
            if (working || scanning)
            {
                e.Cancel = true;
                MessageBox.Show(this, "Aguarde o término da etapa atual.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}
