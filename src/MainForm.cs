using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DesinstaladorCompleto
{
    /// <summary>Janela principal: lista de programas + painel de detalhes com as ações.</summary>
    internal class MainForm : Form
    {
        private enum Filter { Todos, Programas, Store }

        private static int S(int px) { return Theme.S(px); }

        // cabeçalho / barra de ferramentas
        private readonly ModernButton btnRefresh, btnTheme;
        private readonly ContextMenuStrip themeMenu;
        private readonly Label lblSub;
        private readonly SearchBox search;
        private readonly ModernButton segAll, segPrograms, segStore;
        private readonly ToggleSwitch tglSystem;
        private readonly Label lblCount;
        private readonly Panel toolbar;

        // lista
        private readonly ModernListView lv;
        private readonly Label lblListEmpty;

        // painel de detalhes
        private readonly CardPanel details;
        private readonly Panel emptyPanel, infoPanel;
        private readonly PictureBox picIcon;
        private readonly Label lblName, lblPublisher;
        private readonly Label[] infoValues = new Label[6];
        private readonly Label[] infoLabels = new Label[6];
        private readonly ModernButton btnUninstall, btnForce, btnOpenFolder, btnDetails;
        private readonly Label lblSafety, shield;
        private int separatorY;
        private readonly ToolTip tips = new ToolTip();

        private readonly Timer searchTimer = new Timer { Interval = 200 };
        private readonly Dictionary<string, Bitmap> icons = new Dictionary<string, Bitmap>();
        private readonly Dictionary<string, Bitmap> bigIcons = new Dictionary<string, Bitmap>();
        private Bitmap defaultIcon, storeIcon;

        /// <summary>Usado só na compilação de pré-visualização para abrir com um programa selecionado.</summary>
        public string PreviewSelect;

        private List<InstalledProgram> programs = new List<InstalledProgram>();
        private Filter filter = Filter.Todos;
        private bool loading;

        public MainForm()
        {
            Text = "Desinstalador Completo";
            AutoScaleMode = AutoScaleMode.None;
            Font = Theme.Body;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(S(1260), S(800));
            MinimumSize = new Size(S(980), S(620));
            KeyPreview = true;
            if (Program.AppIcon != null) Icon = Program.AppIcon;

            defaultIcon = Theme.GlyphBitmap(Glyphs.App, S(28), Theme.TextSubtle);
            storeIcon = Theme.GlyphBitmap(Glyphs.Store, S(28), Theme.Accent);

            // ================================================== cabeçalho
            var header = new GradientHeader { Dock = DockStyle.Top, Height = S(78) };
            var logo = new PictureBox { BackColor = Color.Transparent, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(S(42), S(42)), Location = new Point(S(22), S(18)) };
            if (Program.AppIcon != null) logo.Image = new Icon(Program.AppIcon, S(48), S(48)).ToBitmap();
            var lblTitle = new Label { Text = "Desinstalador Completo", Font = Theme.Title, ForeColor = Color.White, BackColor = Color.Transparent, AutoSize = true, Location = new Point(S(74), S(13)) };
            lblSub = new Label
            {
                Text = "Remova programas e todos os resquícios com segurança  •  nada é excluído sem a sua confirmação",
                Font = Theme.Small, ForeColor = Theme.HeaderSubtext, BackColor = Color.Transparent, AutoSize = true, Location = new Point(S(76), S(46))
            };
            btnRefresh = new ModernButton { Kind = ButtonKind.OnAccent, Glyph = Glyphs.Refresh, Text = "Atualizar lista", AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnTheme = new ModernButton { Kind = ButtonKind.OnAccent, Glyph = Glyphs.ColorTheme, Text = "Tema", AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            header.Controls.AddRange(new Control[] { logo, lblTitle, lblSub, btnTheme, btnRefresh });
            header.Resize += (s, e) =>
            {
                btnRefresh.Location = new Point(header.Width - btnRefresh.Width - S(22), (header.Height - btnRefresh.Height) / 2);
                btnTheme.Location = new Point(btnRefresh.Left - btnTheme.Width - S(8), btnRefresh.Top);
            };
            tips.SetToolTip(btnRefresh, "Recarregar a lista de programas (F5)");
            tips.SetToolTip(btnTheme, "Escolher o tema: o mesmo do Windows, claro ou escuro");
            themeMenu = BuildThemeMenu();
            btnTheme.Click += (s, e) => themeMenu.Show(btnTheme, new Point(btnTheme.Width - themeMenu.GetPreferredSize(Size.Empty).Width, btnTheme.Height + S(4)));

            // ================================================== barra de ferramentas
            toolbar = new Panel { Dock = DockStyle.Top, Height = S(66), BackColor = Theme.Background };
            search = new SearchBox("Pesquisar por nome ou editor...  (Ctrl+F)") { Width = S(380) };
            segAll = new ModernButton { Kind = ButtonKind.Segment, Text = "Todos", AutoSize = true, Checked = true };
            segPrograms = new ModernButton { Kind = ButtonKind.Segment, Glyph = Glyphs.App, Text = "Programas", AutoSize = true };
            segStore = new ModernButton { Kind = ButtonKind.Segment, Glyph = Glyphs.Store, Text = "Microsoft Store", AutoSize = true };
            foreach (var b in new[] { segAll, segPrograms, segStore }) b.PreferredHeight = S(38);
            tglSystem = new ToggleSwitch { Text = "Componentes do sistema" };
            tips.SetToolTip(tglSystem, "Mostra também componentes ocultos, atualizações e partes do Windows");
            lblCount = new Label { AutoSize = true, ForeColor = Theme.TextMuted, Font = Theme.Small, BackColor = Theme.Background };
            toolbar.Controls.AddRange(new Control[] { search, segAll, segPrograms, segStore, tglSystem, lblCount });
            toolbar.Resize += (s, e) => LayoutToolbar();

            // ================================================== conteúdo
            var content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Padding = new Padding(S(20), 0, S(20), S(10)) };
            var listCard = new CardPanel { Dock = DockStyle.Fill };
            lv = new ModernListView { Dock = DockStyle.Fill, MultiSelect = false };
            lv.SetRowHeight(S(52));
            lv.Columns.Add("Programa", S(380));
            lv.Columns.Add("Versão", S(130));
            lv.Columns.Add("Tamanho", S(95), HorizontalAlignment.Right);
            lv.Columns.Add("Instalado em", S(115));
            lv.Columns.Add("Tipo", S(120));
            lv.SortColumn = 0;
            lblListEmpty = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.TextMuted, Font = Theme.Subtitle,
                BackColor = Theme.Surface, Text = "Carregando programas instalados...", Visible = true
            };
            listCard.Controls.Add(lv);
            listCard.Controls.Add(lblListEmpty);
            lblListEmpty.BringToFront();

            details = new CardPanel { Dock = DockStyle.Right, Width = S(340), Padding = new Padding(S(22)) };
            var spacer = new Panel { Dock = DockStyle.Right, Width = S(16), BackColor = Theme.Background };

            // ---- painel vazio
            emptyPanel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
            var emptyBadge = new GlyphBadge { Size = new Size(S(64), S(64)) };
            emptyBadge.Set(Glyphs.App, Theme.Background, Theme.TextSubtle);
            var emptyTitle = new Label { Text = "Nenhum programa selecionado", Font = Theme.Heading, ForeColor = Theme.Text, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter };
            var emptyText = new Label
            {
                Text = "Clique em um programa da lista para ver os detalhes e as opções de remoção.\n\nDica: comece a digitar para pesquisar.",
                ForeColor = Theme.TextMuted, AutoSize = false, TextAlign = ContentAlignment.TopCenter
            };
            emptyPanel.Controls.AddRange(new Control[] { emptyBadge, emptyTitle, emptyText });
            emptyPanel.Resize += (s, e) =>
            {
                int w = emptyPanel.ClientSize.Width;
                int y = Math.Max(S(20), emptyPanel.ClientSize.Height / 2 - S(110));
                emptyBadge.Location = new Point((w - emptyBadge.Width) / 2, y);
                emptyTitle.SetBounds(0, emptyBadge.Bottom + S(16), w, S(30));
                emptyText.SetBounds(S(10), emptyTitle.Bottom + S(6), w - S(20), S(100));
            };

            // ---- painel com o programa selecionado
            infoPanel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Visible = false };
            picIcon = new PictureBox { Size = new Size(S(52), S(52)), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Surface };
            lblName = new Label { Font = Theme.Heading, ForeColor = Theme.Text, AutoSize = false, AutoEllipsis = true, UseMnemonic = false };
            lblPublisher = new Label { ForeColor = Theme.TextMuted, AutoSize = false, AutoEllipsis = true, UseMnemonic = false };
            infoPanel.Controls.AddRange(new Control[] { picIcon, lblName, lblPublisher });
            string[] captions = { "Versão", "Tamanho", "Instalado em", "Tipo", "Origem", "Local" };
            for (int i = 0; i < captions.Length; i++)
            {
                infoLabels[i] = new Label { Text = captions[i], ForeColor = Theme.TextMuted, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
                infoValues[i] = new Label { ForeColor = Theme.Text, AutoSize = false, AutoEllipsis = true, UseMnemonic = false, TextAlign = ContentAlignment.MiddleLeft };
                infoPanel.Controls.Add(infoLabels[i]);
                infoPanel.Controls.Add(infoValues[i]);
            }
            btnUninstall = new ModernButton { Kind = ButtonKind.Danger, Glyph = Glyphs.Delete, Text = "Desinstalar", PreferredHeight = S(46) };
            btnForce = new ModernButton { Kind = ButtonKind.Warning, Glyph = Glyphs.Broom, Text = "Remoção forçada", PreferredHeight = S(40) };
            btnOpenFolder = new ModernButton { Kind = ButtonKind.Secondary, Glyph = Glyphs.FolderOpen, Text = "Abrir pasta de instalação", PreferredHeight = S(40) };
            btnDetails = new ModernButton { Kind = ButtonKind.Ghost, Glyph = Glyphs.Info, Text = "Detalhes técnicos", PreferredHeight = S(36) };
            lblSafety = new Label
            {
                Text = "Nada é excluído sem você revisar. O registro tem backup automático.",
                ForeColor = Theme.TextMuted, Font = Theme.Small, AutoSize = false
            };
            shield = new Label { Text = Glyphs.Shield, Font = Theme.Glyph(12f), ForeColor = Theme.Success, AutoSize = false, TextAlign = ContentAlignment.TopCenter };
            infoPanel.Controls.AddRange(new Control[] { btnUninstall, btnForce, btnOpenFolder, btnDetails, lblSafety, shield });
            tips.SetToolTip(btnUninstall, "Executa o desinstalador do programa e depois procura resquícios (Enter)");
            tips.SetToolTip(btnForce, "Para programas com desinstalador quebrado: pula o desinstalador e só procura resquícios");
            infoPanel.Resize += (s, e) => LayoutDetails();
            infoPanel.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, 0, separatorY, infoPanel.Width, separatorY);
            };

            details.Controls.Add(infoPanel);
            details.Controls.Add(emptyPanel);

            content.Controls.Add(listCard);
            content.Controls.Add(spacer);
            content.Controls.Add(details);

            // ---- rodapé com atalhos
            var footer = new Label
            {
                Dock = DockStyle.Bottom, Height = S(26), ForeColor = Theme.TextSubtle, Font = Theme.Small, BackColor = Theme.Background,
                Padding = new Padding(S(22), 0, 0, S(6)), TextAlign = ContentAlignment.MiddleLeft,
                Text = "Atalhos:   Enter ou duplo clique = desinstalar     Ctrl+F = pesquisar     F5 = atualizar     Clique com o botão direito = mais opções"
            };

            Controls.Add(content);
            Controls.Add(footer);
            Controls.Add(toolbar);
            Controls.Add(header);

            BuildContextMenu();

            // ================================================== eventos
            searchTimer.Tick += (s, e) => { searchTimer.Stop(); ApplyFilter(); };
            search.QueryChanged += (s, e) => { searchTimer.Stop(); searchTimer.Start(); };
            search.Input.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Down && lv.Items.Count > 0) { lv.Focus(); SelectIndex(0); e.Handled = true; }
                else if (e.KeyCode == Keys.Enter && lv.Items.Count > 0) { SelectIndex(0); lv.Focus(); e.SuppressKeyPress = true; }
            };
            segAll.Click += (s, e) => SetFilter(Filter.Todos);
            segPrograms.Click += (s, e) => SetFilter(Filter.Programas);
            segStore.Click += (s, e) => SetFilter(Filter.Store);
            tglSystem.CheckedChanged += (s, e) => ApplyFilter();
            btnRefresh.Click += (s, e) => LoadPrograms();
            btnUninstall.Click += (s, e) => StartUninstall(false);
            btnForce.Click += (s, e) => StartUninstall(true);
            btnOpenFolder.Click += (s, e) => OpenInstallFolder();
            btnDetails.Click += (s, e) => ShowDetails();
            lv.DrawSubItem += OnDrawCell;
            lv.DoubleClick += (s, e) => StartUninstall(false);
            lv.ColumnClick += OnColumnClick;
            lv.SelectedIndexChanged += (s, e) => UpdateDetails();
            lv.Resize += (s, e) => lv.FillFirstColumn();
            lv.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Delete) { StartUninstall(false); e.Handled = true; }
            };
            lv.KeyPress += (s, e) =>
            {
                // Digitar com a lista em foco vai direto para a pesquisa
                if (!char.IsControl(e.KeyChar))
                {
                    search.Input.Focus();
                    search.Input.Text += e.KeyChar;
                    search.Input.SelectionStart = search.Input.Text.Length;
                    e.Handled = true;
                }
            };
            Shown += (s, e) => { LayoutToolbar(); lv.FillFirstColumn(); LoadPrograms(); };
            UpdateDetails();

            Theme.Changed += OnThemeChanged;
            FormClosed += (s, e) => Theme.Changed -= OnThemeChanged;
            Theme.Attach(this);
        }

        // ------------------------------------------------------------------ tema

        private ContextMenuStrip BuildThemeMenu()
        {
            var menu = new ContextMenuStrip { Font = Theme.Body, ShowImageMargin = false, ShowCheckMargin = true };
            var items = new[]
            {
                new ToolStripMenuItem("Usar o tema do Windows") { Tag = ThemeMode.Sistema },
                new ToolStripMenuItem("Claro") { Tag = ThemeMode.Claro },
                new ToolStripMenuItem("Escuro") { Tag = ThemeMode.Escuro }
            };
            foreach (ToolStripMenuItem it in items)
            {
                it.Padding = new Padding(0, S(3), 0, S(3));
                it.Click += (s, e) => Theme.SetMode((ThemeMode)((ToolStripItem)s).Tag);
                menu.Items.Add(it);
            }
            menu.Opening += (s, e) =>
            {
                foreach (ToolStripMenuItem it in items) it.Checked = (ThemeMode)it.Tag == Theme.Mode;
            };
            Theme.RetintMenu(menu);
            return menu;
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            // Ícones e menus gerados com as cores do tema anterior precisam ser refeitos
            Bitmap oldDefault = defaultIcon, oldStore = storeIcon;
            defaultIcon = Theme.GlyphBitmap(Glyphs.App, S(28), Theme.TextSubtle);
            storeIcon = Theme.GlyphBitmap(Glyphs.Store, S(28), Theme.Accent);
            oldDefault.Dispose();
            oldStore.Dispose();
            bigIcons.Clear();
            ContextMenuStrip oldMenu = lv.ContextMenuStrip;
            BuildContextMenu();
            if (oldMenu != null) oldMenu.Dispose();
            Theme.RetintMenu(themeMenu);
            lblSub.ForeColor = Theme.HeaderSubtext;
            UpdateDetails();
            lv.Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.F) { search.FocusSearch(); e.Handled = true; }
            else if (e.KeyCode == Keys.F5) { LoadPrograms(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        // ------------------------------------------------------------------ layout

        private void LayoutToolbar()
        {
            int x = S(20), h = S(38);
            int y = (toolbar.Height - h) / 2 + S(2);
            search.SetBounds(x, y, Math.Min(S(380), Math.Max(S(220), toolbar.Width - S(760))), h);
            x = search.Right + S(14);
            foreach (ModernButton b in new[] { segAll, segPrograms, segStore })
            {
                b.Size = b.GetPreferredSize(Size.Empty);
                b.Location = new Point(x, y);
                x = b.Right + S(6);
            }
            tglSystem.Size = tglSystem.GetPreferredSize(Size.Empty);
            tglSystem.Location = new Point(x + S(14), y + (h - tglSystem.Height) / 2);
            lblCount.Location = new Point(toolbar.Width - lblCount.Width - S(22), y + (h - lblCount.Height) / 2);
            lblCount.Visible = lblCount.Left > tglSystem.Right + S(10);
        }

        private void LayoutDetails()
        {
            int w = infoPanel.ClientSize.Width;
            int h = infoPanel.ClientSize.Height;
            if (w <= 0) return;
            picIcon.Location = new Point(0, 0);
            int nameH = TextRenderer.MeasureText(lblName.Text ?? "", lblName.Font, new Size(w, S(200)), TextFormatFlags.WordBreak).Height;
            lblName.SetBounds(0, picIcon.Bottom + S(10), w, Math.Min(S(56), Math.Max(S(26), nameH + S(2))));
            lblPublisher.SetBounds(0, lblName.Bottom + S(2), w, S(20));
            separatorY = lblPublisher.Bottom + S(12);
            int y = separatorY + S(8);
            for (int i = 0; i < infoLabels.Length; i++)
            {
                infoLabels[i].SetBounds(0, y, S(96), S(25));
                infoValues[i].SetBounds(S(100), y, w - S(100), S(25));
                y += S(26);
            }
            infoPanel.Invalidate();

            int by = h;
            lblSafety.SetBounds(S(26), by - S(38), w - S(26), S(38));
            shield.SetBounds(0, by - S(38), S(22), S(24));
            by -= S(48);
            foreach (ModernButton b in new[] { btnDetails, btnOpenFolder, btnForce, btnUninstall })
            {
                b.SetBounds(0, by - b.PreferredHeight, w, b.PreferredHeight);
                by = b.Top - S(8);
            }
            // Se faltar espaço, esconde as últimas linhas de informação
            for (int i = 0; i < infoLabels.Length; i++)
            {
                bool fits = infoValues[i].Bottom < by - S(4);
                infoLabels[i].Visible = fits;
                infoValues[i].Visible = fits;
            }
        }

        private void BuildContextMenu()
        {
            var menu = new ContextMenuStrip { Font = Theme.Body, ShowImageMargin = true, ImageScalingSize = new Size(S(16), S(16)) };
            Func<string, Color, Image> img = (g, c) => Theme.GlyphBitmap(g, S(16), c);
            menu.Items.Add("Desinstalar...", img(Glyphs.Delete, Theme.Danger), (s, e) => StartUninstall(false));
            menu.Items.Add("Remoção forçada...", img(Glyphs.Broom, Theme.Warning), (s, e) => StartUninstall(true));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Abrir pasta de instalação", img(Glyphs.FolderOpen, Theme.Accent), (s, e) => OpenInstallFolder());
            menu.Items.Add("Detalhes técnicos", img(Glyphs.Info, Theme.Accent), (s, e) => ShowDetails());
            menu.Items.Add("Copiar nome", img(Glyphs.Document, Theme.TextMuted), (s, e) => { var p = Selected(); if (p != null) Clipboard.SetText(p.DisplayName); });
            menu.Opening += (s, e) => { if (Selected() == null) e.Cancel = true; };
            Theme.RetintMenu(menu);
            lv.ContextMenuStrip = menu;
        }

        // ------------------------------------------------------------------ carregamento

        private async void LoadPrograms()
        {
            if (loading) return;
            loading = true;
            btnRefresh.Enabled = false;
            UseWaitCursor = true;
            try
            {
                SetCount("Carregando programas...");
                if (programs.Count == 0) ShowListMessage("Carregando programas instalados...");
                List<InstalledProgram> win = await Task.Run(() => ProgramScanner.GetWin32Programs());
                programs = win;
                ApplyFilter();

                SetCount("Carregando aplicativos da Microsoft Store...");
                List<InstalledProgram> store = await Task.Run(() => ProgramScanner.GetStorePrograms());
                programs = win.Concat(store).ToList();
                ApplyFilter();

                SetCount("Carregando ícones...");
                List<InstalledProgram> snapshot = programs;
                int size = S(28);
                Dictionary<string, Bitmap> loaded = await Task.Run(() =>
                {
                    var dict = new Dictionary<string, Bitmap>();
                    foreach (InstalledProgram p in snapshot)
                    {
                        if (dict.ContainsKey(p.Id)) continue;
                        Bitmap b = LoadIcon(p, size);
                        if (b != null) dict[p.Id] = b;
                    }
                    return dict;
                });
                foreach (var kv in loaded) icons[kv.Key] = kv.Value;
                lv.Invalidate();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Erro ao carregar a lista de programas:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                loading = false;
                btnRefresh.Enabled = true;
                UseWaitCursor = false;
                UpdateCounts();
            }
        }

        private static Bitmap LoadIcon(InstalledProgram p, int size)
        {
            Bitmap b = IconUtil.GetIcon(p.Kind == ProgramKind.StoreApp ? p.LogoPath : p.DisplayIcon, size);
            if (b == null && p.Kind != ProgramKind.StoreApp)
            {
                string exe = UninstallRunner.UninstallerExe(p);
                if (exe != null) b = IconUtil.GetIcon(exe, size);
            }
            return b;
        }

        private Bitmap SmallIcon(InstalledProgram p)
        {
            Bitmap b;
            if (icons.TryGetValue(p.Id, out b)) return b;
            return p.Kind == ProgramKind.StoreApp ? storeIcon : defaultIcon;
        }

        private Bitmap BigIcon(InstalledProgram p)
        {
            Bitmap b;
            if (!bigIcons.TryGetValue(p.Id, out b))
            {
                b = LoadIcon(p, S(56)) ?? Theme.GlyphBitmap(p.Kind == ProgramKind.StoreApp ? Glyphs.Store : Glyphs.App, S(56), Theme.TextSubtle);
                bigIcons[p.Id] = b;
            }
            return b;
        }

        // ------------------------------------------------------------------ filtro e lista

        private void SetFilter(Filter f)
        {
            filter = f;
            segAll.Checked = f == Filter.Todos;
            segPrograms.Checked = f == Filter.Programas;
            segStore.Checked = f == Filter.Store;
            ApplyFilter();
        }

        private IEnumerable<InstalledProgram> BaseView()
        {
            IEnumerable<InstalledProgram> v = programs;
            if (!tglSystem.Checked) v = v.Where(p => !p.IsSystemComponent && !p.IsUpdate);
            return v;
        }

        private void ApplyFilter()
        {
            string q = search.Query.Trim();
            string selectedId = Selected() != null ? Selected().Id : null;
            IEnumerable<InstalledProgram> view = BaseView();
            if (filter == Filter.Programas) view = view.Where(p => p.Kind != ProgramKind.StoreApp);
            else if (filter == Filter.Store) view = view.Where(p => p.Kind == ProgramKind.StoreApp);
            if (q.Length > 0) view = view.Where(p => Contains(p.DisplayName, q) || Contains(p.Publisher, q));

            lv.BeginUpdate();
            lv.ListViewItemSorter = null;
            lv.Items.Clear();
            foreach (InstalledProgram p in view)
            {
                var it = new ListViewItem(p.DisplayName) { Tag = p };
                it.SubItems.Add(p.DisplayVersion ?? "");
                it.SubItems.Add(p.EstimatedSizeKB > 0 ? FsUtil.FormatSize(p.EstimatedSizeKB * 1024) : "");
                it.SubItems.Add(FormatDate(p.InstallDate));
                it.SubItems.Add(p.KindText);
                lv.Items.Add(it);
                if (p.Id == selectedId) it.Selected = true;
            }
            lv.ListViewItemSorter = new ItemComparer(lv.SortColumn, lv.SortAscending);
            lv.EndUpdate();
            if (PreviewSelect != null && lv.SelectedItems.Count == 0)
                foreach (ListViewItem it in lv.Items)
                    if (it.Text.IndexOf(PreviewSelect, StringComparison.OrdinalIgnoreCase) >= 0) { it.Selected = true; break; }
            if (lv.SelectedItems.Count > 0) lv.SelectedItems[0].EnsureVisible();

            if (lv.Items.Count == 0)
                ShowListMessage(loading && programs.Count == 0 ? "Carregando programas instalados..." :
                    q.Length > 0 ? "Nenhum programa encontrado para \"" + q + "\"." : "Nenhum programa nesta categoria.");
            else lblListEmpty.Visible = false;
            UpdateCounts();
            UpdateDetails();
        }

        private void ShowListMessage(string text)
        {
            lblListEmpty.Text = text;
            lblListEmpty.Visible = true;
        }

        private void SetCount(string text)
        {
            lblCount.Text = text;
            LayoutToolbar();
        }

        private void UpdateCounts()
        {
            var bv = BaseView().ToList();
            segAll.Text = "Todos  " + bv.Count;
            segPrograms.Text = "Programas  " + bv.Count(p => p.Kind != ProgramKind.StoreApp);
            segStore.Text = "Microsoft Store  " + bv.Count(p => p.Kind == ProgramKind.StoreApp);
            if (!loading) lblCount.Text = lv.Items.Count + " exibido(s)";
            LayoutToolbar();
        }

        private void SelectIndex(int i)
        {
            if (i < 0 || i >= lv.Items.Count) return;
            lv.Items[i].Selected = true;
            lv.Items[i].Focused = true;
            lv.Items[i].EnsureVisible();
        }

        private static bool Contains(string s, string q)
        {
            return s != null && s.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private static string FormatDate(string d)
        {
            DateTime dt;
            if (!string.IsNullOrEmpty(d) && DateTime.TryParseExact(d.Trim(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                return dt.ToString("dd/MM/yyyy");
            return d ?? "";
        }

        private InstalledProgram Selected()
        {
            return lv.SelectedItems.Count > 0 ? (InstalledProgram)lv.SelectedItems[0].Tag : null;
        }

        private void OnDrawCell(object sender, DrawListViewSubItemEventArgs e)
        {
            var p = (InstalledProgram)e.Item.Tag;
            Rectangle b = lv.CellBounds(e);
            Graphics g = e.Graphics;
            bool dim = p.IsSystemComponent || p.IsUpdate;
            const TextFormatFlags common = TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            switch (e.ColumnIndex)
            {
                case 0:
                {
                    int ic = S(30);
                    var ir = new Rectangle(b.X + S(16), b.Y + (b.Height - ic) / 2, ic, ic);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(SmallIcon(p), ir);
                    int tx = ir.Right + S(14);
                    int mid = b.Y + b.Height / 2;
                    TextRenderer.DrawText(g, p.DisplayName, Theme.BodyBold, new Rectangle(tx, b.Y, b.Right - tx - S(8), mid - b.Y), dim ? Theme.TextMuted : Theme.Text,
                        common | TextFormatFlags.Bottom | TextFormatFlags.Left);
                    string pub = string.IsNullOrWhiteSpace(p.Publisher) ? "Editor desconhecido" : p.Publisher;
                    if (dim) pub += p.IsUpdate ? "  •  atualização" : "  •  componente do sistema";
                    TextRenderer.DrawText(g, pub, Theme.Small, new Rectangle(tx, mid + S(2), b.Right - tx - S(8), b.Bottom - mid - S(2)), Theme.TextMuted,
                        common | TextFormatFlags.Top | TextFormatFlags.Left);
                    break;
                }
                case 4:
                {
                    Color bg, fg;
                    switch (p.Kind)
                    {
                        case ProgramKind.Msi: bg = Theme.PurpleLight; fg = Theme.PurpleText; break;
                        case ProgramKind.StoreApp: bg = Theme.TealLight; fg = Theme.TealText; break;
                        default: bg = Theme.AccentSoft; fg = Theme.AccentText; break;
                    }
                    Theme.DrawPill(g, new Rectangle(b.X + S(12), b.Y, b.Width - S(16), b.Height), p.KindText, bg, fg);
                    break;
                }
                default:
                {
                    var r = new Rectangle(b.X + S(12), b.Y, b.Width - S(24), b.Height);
                    var flags = common | TextFormatFlags.VerticalCenter | (lv.Columns[e.ColumnIndex].TextAlign == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left);
                    TextRenderer.DrawText(g, e.SubItem.Text, Theme.Body, r, e.ColumnIndex == 1 ? Theme.Text : Theme.TextMuted, flags);
                    break;
                }
            }
        }

        private void OnColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (e.Column == lv.SortColumn) lv.SortAscending = !lv.SortAscending;
            else { lv.SortColumn = e.Column; lv.SortAscending = true; }
            lv.ListViewItemSorter = new ItemComparer(lv.SortColumn, lv.SortAscending);
            lv.Invalidate();
        }

        private class ItemComparer : IComparer
        {
            private readonly int col;
            private readonly bool asc;

            public ItemComparer(int col, bool asc)
            {
                this.col = col;
                this.asc = asc;
            }

            public int Compare(object x, object y)
            {
                var a = (ListViewItem)x;
                var b = (ListViewItem)y;
                var pa = (InstalledProgram)a.Tag;
                var pb = (InstalledProgram)b.Tag;
                int r;
                switch (col)
                {
                    case 2: r = pa.EstimatedSizeKB.CompareTo(pb.EstimatedSizeKB); break;
                    case 3: r = string.CompareOrdinal(pa.InstallDate ?? "", pb.InstallDate ?? ""); break;
                    default: r = string.Compare(a.SubItems[Math.Max(0, col)].Text, b.SubItems[Math.Max(0, col)].Text, StringComparison.CurrentCultureIgnoreCase); break;
                }
                return asc ? r : -r;
            }
        }

        // ------------------------------------------------------------------ painel de detalhes

        private void UpdateDetails()
        {
            InstalledProgram p = Selected();
            emptyPanel.Visible = p == null;
            infoPanel.Visible = p != null;
            if (p == null) return;

            picIcon.Image = BigIcon(p);
            lblName.Text = p.DisplayName;
            lblPublisher.Text = string.IsNullOrWhiteSpace(p.Publisher) ? "Editor desconhecido" : p.Publisher;
            string loc = PathUtil.Normalize(p.InstallLocation);
            string[] values =
            {
                string.IsNullOrEmpty(p.DisplayVersion) ? "—" : p.DisplayVersion,
                p.EstimatedSizeKB > 0 ? FsUtil.FormatSize(p.EstimatedSizeKB * 1024) : "—",
                string.IsNullOrEmpty(p.InstallDate) ? "—" : FormatDate(p.InstallDate),
                p.KindText + (p.IsSystemComponent ? " (componente do sistema)" : p.IsUpdate ? " (atualização)" : ""),
                p.SourceLabel ?? "—",
                loc ?? "Não informado pelo programa"
            };
            for (int i = 0; i < values.Length; i++) infoValues[i].Text = values[i];
            tips.SetToolTip(infoValues[5], loc ?? "");

            bool canNormal = !(p.Kind == ProgramKind.StoreApp && p.NoRemove);
            btnUninstall.Enabled = canNormal;
            btnUninstall.Text = p.Kind == ProgramKind.StoreApp ? "Remover aplicativo" : "Desinstalar";
            btnOpenFolder.Enabled = loc != null && Directory.Exists(loc);
            LayoutDetails();
        }

        // ------------------------------------------------------------------ ações

        private void StartUninstall(bool forced)
        {
            InstalledProgram p = Selected();
            if (p == null)
            {
                MessageBox.Show(this, "Selecione um programa na lista.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!forced)
            {
                if (p.Kind == ProgramKind.StoreApp && p.NoRemove)
                {
                    MessageBox.Show(this, "\"" + p.DisplayName + "\" é um componente essencial do Windows e não pode ser removido.",
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (p.Kind != ProgramKind.StoreApp)
                {
                    bool isMsi = UninstallRunner.FindMsiGuid(p) != null;
                    if (!isMsi && string.IsNullOrWhiteSpace(p.UninstallString))
                    {
                        if (Ask("\"" + p.DisplayName + "\" não possui um desinstalador registrado.\n\n" +
                                "Deseja fazer a remoção forçada (procurar e remover os resquícios do programa)?")) forced = true;
                        else return;
                    }
                    else
                    {
                        string exe = UninstallRunner.UninstallerExe(p);
                        if (exe != null && !File.Exists(exe))
                        {
                            if (Ask("O desinstalador do programa não foi encontrado:\n" + exe + "\n\n" +
                                    "Deseja fazer a remoção forçada (procurar e remover os resquícios do programa)?")) forced = true;
                            else return;
                        }
                    }
                    if (!forced && p.NoRemove &&
                        !Ask("O próprio programa indica que não deve ser removido pelo Painel de Controle.\n\nDeseja continuar mesmo assim?"))
                        return;
                }
            }

            UninstallOptions options;
            using (var dlg = new ConfirmDialog(p, forced, BigIcon(p)))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                options = dlg.Options;
            }
            using (var f = new UninstallForm(p, forced, options, BigIcon(p))) f.ShowDialog(this);
            LoadPrograms();
        }

        private bool Ask(string msg)
        {
            return MessageBox.Show(this, msg, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        private void OpenInstallFolder()
        {
            InstalledProgram p = Selected();
            if (p == null) return;
            string dir = PathUtil.Normalize(p.InstallLocation);
            if (dir != null && Directory.Exists(dir)) Process.Start("explorer.exe", "\"" + dir + "\"");
            else MessageBox.Show(this, "A pasta de instalação não foi informada pelo programa ou não existe.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowDetails()
        {
            InstalledProgram p = Selected();
            if (p == null) return;
            var sb = new StringBuilder();
            sb.AppendLine("Nome: " + p.DisplayName);
            sb.AppendLine("Versão: " + p.DisplayVersion);
            sb.AppendLine("Editor: " + p.Publisher);
            sb.AppendLine("Tipo: " + p.KindText + "    Origem: " + p.SourceLabel);
            sb.AppendLine("Pasta de instalação: " + (p.InstallLocation ?? "(não informada)"));
            if (p.Kind == ProgramKind.StoreApp)
            {
                sb.AppendLine("Pacote: " + p.PackageFullName);
                sb.AppendLine("Família: " + p.PackageFamilyName);
            }
            else
            {
                sb.AppendLine("Desinstalador: " + p.UninstallString);
                if (!string.IsNullOrEmpty(p.QuietUninstallString)) sb.AppendLine("Desinstalador silencioso: " + p.QuietUninstallString);
                sb.AppendLine("Registro: " + RegUtil.ShortHive(p.Hive) + "\\" + p.RegistrySubKey);
                if (p.IsSystemComponent) sb.AppendLine("Componente do sistema (oculto no Painel de Controle)");
                if (p.IsUpdate) sb.AppendLine("Atualização");
            }
            MessageBox.Show(this, sb.ToString(), "Detalhes técnicos", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
