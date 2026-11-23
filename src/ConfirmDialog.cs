using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DesinstaladorCompleto
{
    /// <summary>Primeira confirmação: explica o que vai acontecer e oferece as opções.</summary>
    internal class ConfirmDialog : Form
    {
        private static int S(int px) { return Theme.S(px); }

        private readonly ToggleSwitch tglRestore, tglSilent;

        public UninstallOptions Options { get; private set; }

        public ConfirmDialog(InstalledProgram p, bool forced, Bitmap icon)
        {
            Text = forced ? "Remoção forçada" : "Confirmar desinstalação";
            AutoScaleMode = AutoScaleMode.None;
            Font = Theme.Body;
            BackColor = Theme.Surface;
            ForeColor = Theme.Text;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(S(580), S(470));
            if (Program.AppIcon != null) Icon = Program.AppIcon;

            int pad = S(28);
            int w = ClientSize.Width - pad * 2;

            // ---- cabeçalho com o programa
            var pic = new PictureBox { Image = icon ?? Theme.GlyphBitmap(Glyphs.App, S(52), Theme.TextSubtle), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(S(52), S(52)), Location = new Point(pad, pad) };
            var kicker = new Label
            {
                Text = forced ? "REMOÇÃO FORÇADA" : "DESINSTALAR PROGRAMA", Font = Theme.SmallBold,
                ForeColor = forced ? Theme.WarningDark : Theme.Danger, AutoSize = true, Location = new Point(pad + S(68), pad - S(2))
            };
            var name = new Label
            {
                Text = p.DisplayName, Font = Theme.Heading, ForeColor = Theme.Text, AutoSize = false, AutoEllipsis = true, UseMnemonic = false,
                Location = new Point(pad + S(68), pad + S(16)), Size = new Size(w - S(68), S(28))
            };
            string meta = "";
            if (!string.IsNullOrEmpty(p.DisplayVersion)) meta += "Versão " + p.DisplayVersion;
            if (!string.IsNullOrEmpty(p.Publisher)) meta += (meta.Length > 0 ? "  •  " : "") + p.Publisher;
            var lblMeta = new Label
            {
                Text = meta, ForeColor = Theme.TextMuted, Font = Theme.Small, AutoSize = false, AutoEllipsis = true, UseMnemonic = false,
                Location = new Point(pad + S(68), pad + S(44)), Size = new Size(w - S(68), S(20))
            };

            // ---- caixa explicando o que vai acontecer
            string body = forced
                ? "O desinstalador do programa NÃO será executado.\n\nSerá feita apenas a busca por resquícios (pastas, registro, atalhos, serviços, tarefas...). Você verá a lista e escolherá o que excluir."
                : "1.  O desinstalador do próprio programa será executado.\n2.  Depois, procuramos resquícios (pastas, registro, atalhos, serviços...).\n3.  Você revisa a lista e escolhe o que excluir. Nada é apagado sem confirmação.";
            var info = new InfoBox(forced ? Glyphs.Warning : Glyphs.Info, body,
                forced ? Theme.WarningLight : Theme.AccentSoft, forced ? Theme.WarningDark : Theme.Accent)
            {
                Location = new Point(pad, pad + S(80)), Size = new Size(w, S(118))
            };

            // ---- opções
            int oy = info.Bottom + S(20);
            tglRestore = new ToggleSwitch { Text = "Criar ponto de restauração antes", Checked = true, Location = new Point(pad, oy) };
            var restoreDesc = new Label
            {
                Text = "Permite desfazer as alterações pelo Windows se algo der errado (recomendado).",
                ForeColor = Theme.TextMuted, Font = Theme.Small, AutoSize = false, Location = new Point(pad + S(50), oy + S(26)), Size = new Size(w - S(50), S(20))
            };
            bool silentAvailable = !forced && UninstallRunner.SupportsSilent(p);
            tglSilent = new ToggleSwitch { Text = "Desinstalação silenciosa", Location = new Point(pad, oy + S(58)), Enabled = silentAvailable, Visible = !forced };
            var silentDesc = new Label
            {
                Text = silentAvailable ? "Remove sem abrir a janela do desinstalador do programa." : "Não disponível para este programa: a janela do desinstalador será exibida.",
                ForeColor = Theme.TextMuted, Font = Theme.Small, AutoSize = false, Visible = !forced,
                Location = new Point(pad + S(50), oy + S(84)), Size = new Size(w - S(50), S(20))
            };

            // ---- rodapé
            var footer = new Panel { Dock = DockStyle.Bottom, Height = S(74), BackColor = Theme.SurfaceAlt };
            footer.Paint += (s, e) => { using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
            var btnOk = new ModernButton
            {
                Kind = forced ? ButtonKind.Primary : ButtonKind.Danger, Glyph = forced ? Glyphs.Search : Glyphs.Delete,
                Text = forced ? "Procurar resquícios" : (p.Kind == ProgramKind.StoreApp ? "Remover aplicativo" : "Desinstalar"),
                AutoSize = true, PreferredHeight = S(42), DialogResult = DialogResult.OK
            };
            var btnCancel = new ModernButton { Kind = ButtonKind.Secondary, Text = "Cancelar", AutoSize = true, PreferredHeight = S(42), DialogResult = DialogResult.Cancel };
            footer.Controls.AddRange(new Control[] { btnOk, btnCancel });
            footer.Resize += (s, e) =>
            {
                btnOk.Size = btnOk.GetPreferredSize(Size.Empty);
                btnCancel.Size = btnCancel.GetPreferredSize(Size.Empty);
                btnOk.Location = new Point(footer.Width - pad - btnOk.Width, (footer.Height - btnOk.Height) / 2);
                btnCancel.Location = new Point(btnOk.Left - S(10) - btnCancel.Width, btnOk.Top);
            };
            AcceptButton = btnOk;
            CancelButton = btnCancel;

            Controls.AddRange(new Control[] { pic, kicker, name, lblMeta, info, tglRestore, restoreDesc, tglSilent, silentDesc, footer });
            if (forced) ClientSize = new Size(ClientSize.Width, ClientSize.Height - S(58));
            Shown += (s, e) => btnOk.Focus();

            FormClosing += (s, e) =>
            {
                Options = new UninstallOptions { CreateRestorePoint = tglRestore.Checked, Silent = tglSilent.Enabled && tglSilent.Checked };
            };
            Theme.Attach(this);
        }

        /// <summary>Caixa colorida com ícone e texto explicativo.</summary>
        private class InfoBox : Control, IThemeRetint
        {
            private readonly string glyph, text;
            private Color bg, fg;

            public void Retint(Palette from, Palette to)
            {
                bg = from.Translate(bg, to, false);
                fg = from.Translate(fg, to, true);
            }

            public InfoBox(string glyph, string text, Color bg, Color fg)
            {
                this.glyph = glyph;
                this.text = text;
                this.bg = bg;
                this.fg = fg;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                BackColor = Theme.Surface;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(BackColor);
                Theme.FillRound(g, new Rectangle(0, 0, Width, Height), S(10), bg);
                Theme.DrawGlyph(g, glyph, 14f, fg, new Rectangle(S(14), S(14), S(26), S(26)));
                TextRenderer.DrawText(g, text, Theme.Body, new Rectangle(S(52), S(16), Width - S(66), Height - S(24)), Theme.Text,
                    TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix);
            }
        }
    }
}
