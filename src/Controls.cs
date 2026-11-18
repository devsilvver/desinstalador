using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DesinstaladorCompleto
{
    internal enum ButtonKind { Primary, Danger, Secondary, Ghost, Outline, Warning, OnAccent, Segment }

    /// <summary>Botão moderno desenhado à mão: cantos arredondados, ícone + texto, estados de foco/hover/pressionado.</summary>
    internal class ModernButton : Button
    {
        private bool hover, pressed, isChecked;
        private ButtonKind kind = ButtonKind.Secondary;
        private string glyph;
        private int preferredHeight = Theme.S(36);

        public ModernButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
                        UseVisualStyleBackColor = false;
            Font = Theme.BodyBold;
            Cursor = Cursors.Hand;
            Height = preferredHeight;
            Margin = new Padding(0, 0, Theme.S(8), 0);
        }

        [DefaultValue(ButtonKind.Secondary)]
        public ButtonKind Kind { get { return kind; } set { kind = value; Invalidate(); } }

        public string Glyph { get { return glyph; } set { glyph = value; Invalidate(); } }

        public bool Checked { get { return isChecked; } set { isChecked = value; Invalidate(); } }

        public int PreferredHeight { get { return preferredHeight; } set { preferredHeight = value; Height = value; } }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); if (AutoSize) { Size = GetPreferredSize(Size.Empty); } Invalidate(); }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size t = TextRenderer.MeasureText(Text ?? "", Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            bool hasText = !string.IsNullOrEmpty(Text);
            bool hasGlyph = !string.IsNullOrEmpty(glyph);
            int w = (hasText ? t.Width : 0) + (hasGlyph ? Theme.S(16) : 0) + (hasText && hasGlyph ? Theme.S(8) : 0);
            w += hasText ? Theme.S(32) : Theme.S(20);
            return new Size(w, preferredHeight);
        }

        private void GetColors(out Color bg, out Color fg, out Color border, out Color glyphColor)
        {
            border = Color.Empty;
            if (!Enabled)
            {
                bg = kind == ButtonKind.Ghost || kind == ButtonKind.OnAccent ? Color.Transparent : Color.FromArgb(243, 244, 246);
                fg = kind == ButtonKind.OnAccent ? Color.FromArgb(150, 255, 255, 255) : Theme.TextSubtle;
                glyphColor = fg;
                if (kind != ButtonKind.Ghost && kind != ButtonKind.OnAccent) border = Theme.Border;
                return;
            }
            switch (kind)
            {
                case ButtonKind.Primary:
                    bg = pressed ? Theme.AccentPressed : hover ? Theme.AccentHover : Theme.Accent;
                    fg = Color.White;
                    break;
                case ButtonKind.Danger:
                    bg = pressed ? Theme.DangerPressed : hover ? Theme.DangerHover : Theme.Danger;
                    fg = Color.White;
                    break;
                case ButtonKind.Ghost:
                    bg = pressed ? Theme.Border : hover ? Color.FromArgb(238, 240, 243) : Color.Transparent;
                    fg = Theme.Text;
                    break;
                case ButtonKind.Outline:
                    bg = pressed ? Theme.AccentLight : hover ? Theme.AccentSoft : Theme.Surface;
                    fg = Theme.AccentPressed;
                    border = Theme.Accent;
                    break;
                case ButtonKind.Warning:
                    bg = pressed ? Color.FromArgb(253, 230, 138) : hover ? Theme.WarningLight : Theme.Surface;
                    fg = Theme.WarningDark;
                    border = Theme.Warning;
                    break;
                case ButtonKind.OnAccent:
                    bg = Color.FromArgb(pressed ? 90 : hover ? 65 : 38, 255, 255, 255);
                    fg = Color.White;
                    border = Color.FromArgb(90, 255, 255, 255);
                    break;
                case ButtonKind.Segment:
                    if (isChecked)
                    {
                        bg = Theme.AccentLight;
                        fg = Theme.AccentPressed;
                        border = Theme.Accent;
                    }
                    else
                    {
                        bg = pressed ? Theme.Border : hover ? Theme.SurfaceAlt : Theme.Surface;
                        fg = Theme.TextMuted;
                        border = Theme.BorderStrong;
                    }
                    break;
                default:
                    bg = pressed ? Theme.Border : hover ? Theme.SurfaceAlt : Theme.Surface;
                    fg = Theme.Text;
                    border = hover ? Theme.TextSubtle : Theme.BorderStrong;
                    break;
            }
            glyphColor = fg;
            if (kind == ButtonKind.Ghost || kind == ButtonKind.Secondary) glyphColor = Theme.Accent;
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.PaintParentBackground(this, g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color bg, fg, border, glyphColor;
            GetColors(out bg, out fg, out border, out glyphColor);
            int radius = Theme.S(7);
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            if (bg.A > 0) Theme.FillRound(g, new Rectangle(0, 0, Width, Height), radius, bg);
            if (!border.IsEmpty) Theme.DrawRound(g, new Rectangle(0, 0, Width, Height), radius, border);

            bool hasText = !string.IsNullOrEmpty(Text);
            bool hasGlyph = !string.IsNullOrEmpty(glyph);
            Size t = hasText ? TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine) : Size.Empty;
            int gw = hasGlyph ? Theme.S(16) : 0;
            int gap = hasText && hasGlyph ? Theme.S(8) : 0;
            int total = gw + gap + t.Width;
            int x = Math.Max(Theme.S(8), (Width - total) / 2);
            if (hasGlyph)
                Theme.DrawGlyph(g, glyph, 11f, glyphColor, new Rectangle(x, 0, gw, Height));
            if (hasText)
            {
                var tr = new Rectangle(x + gw + gap, 0, Width - (x + gw + gap) - Theme.S(6), Height);
                TextRenderer.DrawText(g, Text, Font, tr, fg,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }

            if (Focused && ShowFocusCues)
            {
                var fr = new Rectangle(Theme.S(2), Theme.S(2), Width - Theme.S(4), Height - Theme.S(4));
                Theme.DrawRound(g, fr, radius - 1, kind == ButtonKind.Primary || kind == ButtonKind.Danger ? Color.FromArgb(200, 255, 255, 255) : Theme.Accent, 1.5f);
            }
        }
    }

    /// <summary>Chave liga/desliga (substitui a caixa de seleção).</summary>
    internal class ToggleSwitch : CheckBox
    {
        public ToggleSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
                        Font = Theme.Body;
            Cursor = Cursors.Hand;
            AutoSize = true;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size t = TextRenderer.MeasureText(Text ?? "", Font, Size.Empty, TextFormatFlags.NoPadding);
            return new Size(Theme.S(40) + Theme.S(10) + t.Width + Theme.S(4), Math.Max(Theme.S(26), t.Height + Theme.S(4)));
        }

        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.PaintParentBackground(this, g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int tw = Theme.S(40), th = Theme.S(22);
            var track = new Rectangle(1, (Height - th) / 2, tw, th);
            Color trackColor = !Enabled ? Theme.Border : Checked ? Theme.Accent : Theme.BorderStrong;
            Theme.FillRound(g, track, th / 2, trackColor);
            int k = th - Theme.S(6);
            int kx = Checked ? track.Right - k - Theme.S(3) : track.Left + Theme.S(3);
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, kx, track.Top + Theme.S(3), k, k);
            var tr = new Rectangle(track.Right + Theme.S(10), 0, Width - track.Right - Theme.S(10), Height);
            TextRenderer.DrawText(g, Text, Font, tr, Enabled ? Theme.Text : Theme.TextSubtle,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues) Theme.DrawRound(g, new Rectangle(track.X - 1, track.Y - 1, track.Width + 2, track.Height + 2), th / 2 + 1, Theme.AccentHover, 1.5f);
        }
    }

    /// <summary>Caixa de pesquisa arredondada com ícone de lupa, texto de exemplo e botão de limpar.</summary>
    internal class SearchBox : Panel
    {
        private readonly TextBox input;
        private readonly ModernButton clear;
        private readonly string placeholder;

        public event EventHandler QueryChanged;

        public SearchBox(string placeholder)
        {
            this.placeholder = placeholder;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
            Height = Theme.S(38);
            Cursor = Cursors.IBeam;
            input = new TextBox { BorderStyle = BorderStyle.None, Font = Theme.Body, BackColor = Theme.Surface, ForeColor = Theme.Text };
            clear = new ModernButton { Kind = ButtonKind.Ghost, Glyph = Glyphs.Cancel, Text = "", PreferredHeight = Theme.S(28), Width = Theme.S(28), Visible = false, TabStop = false };
            clear.Click += (s, e) => { input.Clear(); input.Focus(); };
            input.TextChanged += (s, e) =>
            {
                clear.Visible = input.Text.Length > 0;
                if (QueryChanged != null) QueryChanged(this, EventArgs.Empty);
            };
            input.GotFocus += (s, e) => Invalidate();
            input.LostFocus += (s, e) => Invalidate();
            input.HandleCreated += (s, e) => NativeMethods.SendMessage(input.Handle, 0x1501, new IntPtr(1), this.placeholder);
            input.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { input.Clear(); e.SuppressKeyPress = true; } };
            Controls.Add(input);
            Controls.Add(clear);
            Click += (s, e) => input.Focus();
        }

        public string Query { get { return input.Text; } }

        public TextBox Input { get { return input; } }

        public void FocusSearch()
        {
            input.Focus();
            input.SelectAll();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (input == null || clear == null) return;   // ainda no construtor
            int left = Theme.S(38);
            clear.Location = new Point(Width - clear.Width - Theme.S(5), (Height - clear.Height) / 2);
            input.Location = new Point(left, (Height - input.Height) / 2);
            input.Width = Math.Max(10, Width - left - clear.Width - Theme.S(10));
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.PaintParentBackground(this, g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color parentBg = Parent != null ? Parent.BackColor : Theme.Background;
            g.Clear(parentBg);
            var r = new Rectangle(0, 0, Width, Height);
            Theme.FillRound(g, r, Theme.S(8), Theme.Surface);
            bool focused = input.Focused;
            Theme.DrawRound(g, r, Theme.S(8), focused ? Theme.Accent : Theme.BorderStrong, focused ? 2f : 1f);
            Theme.DrawGlyph(g, Glyphs.Search, 11f, focused ? Theme.Accent : Theme.TextMuted, new Rectangle(Theme.S(10), 0, Theme.S(20), Height));
        }
    }

    /// <summary>Painel branco com cantos arredondados e borda sutil.</summary>
    internal class CardPanel : Panel
    {
        public CardPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
            Padding = new Padding(Theme.S(6));
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Background);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width, Height);
            Theme.FillRound(g, r, Theme.S(10), Theme.Surface);
            Theme.DrawRound(g, r, Theme.S(10), Theme.Border);
        }
    }

    /// <summary>Faixa superior em degradê azul.</summary>
    internal class GradientHeader : Panel
    {
        public GradientHeader()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Accent;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (Width <= 0 || Height <= 0) return;
            using (var b = new LinearGradientBrush(ClientRectangle, Theme.Accent, Theme.Indigo, LinearGradientMode.Horizontal))
                e.Graphics.FillRectangle(b, ClientRectangle);
        }
    }

    /// <summary>Indicador de etapas: ① Desinstalar → ② Procurar resquícios → ③ Revisar e excluir.</summary>
    internal class Stepper : Control
    {
        private string[] steps = new string[0];
        private int current;
        private bool failed;

        public Stepper()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
                        Font = Theme.SmallBold;
            Height = Theme.S(32);
        }

        public string[] Steps { get { return steps; } set { steps = value ?? new string[0]; Width = GetPreferredSize(Size.Empty).Width; Invalidate(); } }

        /// <summary>Índice da etapa atual; igual a Steps.Length quando tudo terminou.</summary>
        public int Current { get { return current; } set { current = value; failed = false; Invalidate(); } }

        public void MarkFailed() { failed = true; Invalidate(); }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int w = 0;
            for (int i = 0; i < steps.Length; i++)
            {
                w += Theme.S(26) + Theme.S(8) + TextRenderer.MeasureText(steps[i], Font).Width;
                if (i < steps.Length - 1) w += Theme.S(36);
            }
            return new Size(w + Theme.S(4), Theme.S(32));
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.PaintParentBackground(this, g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int x = 0, d = Theme.S(26), cy = Height / 2;
            for (int i = 0; i < steps.Length; i++)
            {
                bool done = i < current;
                bool active = i == current;
                Color fill = done ? Theme.Success : active ? (failed ? Theme.Danger : Theme.Accent) : Theme.Surface;
                var circle = new Rectangle(x, cy - d / 2, d, d);
                using (var b = new SolidBrush(fill)) g.FillEllipse(b, circle);
                if (!done && !active) using (var p = new Pen(Theme.BorderStrong, 1.5f)) g.DrawEllipse(p, circle);
                if (done) Theme.DrawGlyph(g, Glyphs.Check, 9f, Color.White, circle);
                else if (active && failed) Theme.DrawGlyph(g, Glyphs.Cancel, 8f, Color.White, circle);
                else TextRenderer.DrawText(g, (i + 1).ToString(), Font, circle, active ? Color.White : Theme.TextMuted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                x += d + Theme.S(8);
                Size ts = TextRenderer.MeasureText(steps[i], Font);
                TextRenderer.DrawText(g, steps[i], Font, new Rectangle(x, 0, ts.Width, Height), active || done ? Theme.Text : Theme.TextMuted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                x += ts.Width;
                if (i < steps.Length - 1)
                {
                    using (var p = new Pen(done ? Theme.Success : Theme.BorderStrong, 2f))
                        g.DrawLine(p, x + Theme.S(8), cy, x + Theme.S(28), cy);
                    x += Theme.S(36);
                }
            }
        }
    }

    /// <summary>Barra de progresso fina e animada (indeterminada).</summary>
    internal class ThinProgress : Control
    {
        private readonly Timer timer = new Timer { Interval = 16 };
        private float pos;
        private bool running;

        public ThinProgress()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = Theme.S(3);
            BackColor = Theme.Surface;
            timer.Tick += (s, e) => { pos += 0.012f; if (pos > 1.4f) pos = -0.4f; Invalidate(); };
        }

        public bool Running
        {
            get { return running; }
            set { running = value; timer.Enabled = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(running ? Theme.AccentLight : BackColor);
            if (!running) return;
            int w = Width / 3;
            int x = (int)(pos * Width) - w / 2;
            using (var b = new SolidBrush(Theme.Accent)) e.Graphics.FillRectangle(b, x, 0, w, Height);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Ícone dentro de um círculo colorido.</summary>
    internal class GlyphBadge : Control
    {
        protected override void OnPaintBackground(PaintEventArgs e) { }

        private string glyph = Glyphs.Info;
        private Color circle = Theme.AccentLight, glyphColor = Theme.Accent;

        public GlyphBadge()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
                        Size = new Size(Theme.S(48), Theme.S(48));
        }

        public void Set(string g, Color circleColor, Color fg)
        {
            glyph = g;
            circle = circleColor;
            glyphColor = fg;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.PaintParentBackground(this, e.Graphics);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(circle)) e.Graphics.FillEllipse(b, 0, 0, Width - 1, Height - 1);
            Theme.DrawGlyph(e.Graphics, glyph, Width / (Theme.Scale * 3.2f), glyphColor, new Rectangle(0, 0, Width, Height));
        }
    }

    /// <summary>ListView com linhas desenhadas à mão (altura maior, destaque ao passar o mouse, cabeçalho plano).</summary>
    internal class ModernListView : ListView
    {
        private int hotIndex = -1;
        public int SortColumn = -1;
        public bool SortAscending = true;

        public ModernListView()
        {
            OwnerDraw = true;
            View = View.Details;
            FullRowSelect = true;
            HideSelection = false;
            BorderStyle = BorderStyle.None;
            Font = Theme.Body;
            BackColor = Theme.Surface;
            DoubleBuffered = true;
        }

        public int HotIndex { get { return hotIndex; } }

        /// <summary>Define a altura das linhas (o ListView usa a altura da lista de imagens).</summary>
        public void SetRowHeight(int height)
        {
            var il = new ImageList { ImageSize = new Size(1, height) };
            SmallImageList = il;
        }

        protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
        {
            Graphics g = e.Graphics;
            using (var b = new SolidBrush(Theme.SurfaceAlt)) g.FillRectangle(b, e.Bounds);
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            flags |= e.Header.TextAlign == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left;
            var r = new Rectangle(e.Bounds.X + Theme.S(12), e.Bounds.Y, e.Bounds.Width - Theme.S(24), e.Bounds.Height);
            bool sorted = e.ColumnIndex == SortColumn;
            TextRenderer.DrawText(g, e.Header.Text.ToUpperInvariant(), Theme.SmallBold, r, sorted ? Theme.Accent : Theme.TextMuted, flags);
            if (sorted)
            {
                Size ts = TextRenderer.MeasureText(e.Header.Text.ToUpperInvariant(), Theme.SmallBold);
                int gx = e.Header.TextAlign == HorizontalAlignment.Right ? r.Right - ts.Width - Theme.S(16) : r.Left + ts.Width + Theme.S(2);
                Theme.DrawGlyph(g, SortAscending ? Glyphs.ChevronUp : Glyphs.ChevronDown, 7f, Theme.Accent, new Rectangle(gx, e.Bounds.Y, Theme.S(14), e.Bounds.Height));
            }
        }

        protected override void OnDrawItem(DrawListViewItemEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle r = e.Bounds;
            r.Width = Math.Max(r.Width, ClientSize.Width);
            Color bg = e.Item.Selected ? Theme.AccentLight : e.ItemIndex == hotIndex ? Theme.RowHover : Theme.Surface;
            using (var b = new SolidBrush(bg)) g.FillRectangle(b, r);
            using (var p = new Pen(Color.FromArgb(241, 243, 245))) g.DrawLine(p, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1);
            if (e.Item.Selected) using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, r.Left, r.Top, Theme.S(3), r.Height);
        }

        /// <summary>Limites reais de uma célula (no ListView, a coluna 0 recebe a linha inteira).</summary>
        public Rectangle CellBounds(DrawListViewSubItemEventArgs e)
        {
            Rectangle b = e.Bounds;
            if (e.ColumnIndex == 0) b.Width = Columns[0].Width;
            return b;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            ListViewItem it = GetItemAt(e.X, e.Y);
            int idx = it != null ? it.Index : -1;
            if (idx != hotIndex)
            {
                InvalidateRow(hotIndex);
                hotIndex = idx;
                InvalidateRow(hotIndex);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            InvalidateRow(hotIndex);
            hotIndex = -1;
        }

        public void InvalidateRow(int index)
        {
            if (index < 0 || index >= Items.Count) return;
            Rectangle r = Items[index].Bounds;
            r.Width = ClientSize.Width;
            Invalidate(r);
        }

        /// <summary>Faz a primeira coluna ocupar o espaço livre.</summary>
        public void FillFirstColumn()
        {
            if (Columns.Count == 0) return;
            int others = 0;
            for (int i = 1; i < Columns.Count; i++) others += Columns[i].Width;
            Columns[0].Width = Math.Max(Theme.S(200), ClientSize.Width - others - 1);
        }
    }
}
