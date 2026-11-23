using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DesinstaladorCompleto
{
    /// <summary>Preferência de tema escolhida pelo usuário.</summary>
    internal enum ThemeMode { Sistema, Claro, Escuro }

    /// <summary>Conjunto de cores de um tema (claro ou escuro).</summary>
    internal sealed class Palette
    {
        public bool IsDark;

        public Color Accent, AccentHover, AccentPressed, AccentLight, AccentSoft, AccentText, Indigo;
        public Color HeaderStart, HeaderEnd, HeaderSubtext;
        public Color Danger, DangerHover, DangerPressed, DangerLight;
        public Color Warning, WarningDark, WarningLight, WarningPressed;
        public Color Success, SuccessDark, SuccessLight;
        public Color Background, Surface, SurfaceAlt, Border, BorderStrong, Neutral, NeutralHover;
        public Color Text, TextMuted, TextSubtle, RowHover, RowDivider;
        public Color PurpleLight, PurpleText, TealLight, TealText;

        public static readonly Palette Light = new Palette
        {
            IsDark = false,
            Accent = Color.FromArgb(37, 99, 235), AccentHover = Color.FromArgb(29, 78, 216), AccentPressed = Color.FromArgb(30, 64, 175),
            AccentLight = Color.FromArgb(219, 234, 254), AccentSoft = Color.FromArgb(239, 246, 255), AccentText = Color.FromArgb(30, 64, 175),
            Indigo = Color.FromArgb(79, 70, 229),
            HeaderStart = Color.FromArgb(37, 99, 235), HeaderEnd = Color.FromArgb(79, 70, 229), HeaderSubtext = Color.FromArgb(225, 233, 255),
            Danger = Color.FromArgb(220, 38, 38), DangerHover = Color.FromArgb(185, 28, 28), DangerPressed = Color.FromArgb(153, 27, 27),
            DangerLight = Color.FromArgb(254, 226, 226),
            Warning = Color.FromArgb(217, 119, 6), WarningDark = Color.FromArgb(180, 83, 9), WarningLight = Color.FromArgb(254, 243, 199),
            WarningPressed = Color.FromArgb(253, 230, 138),
            Success = Color.FromArgb(22, 163, 74), SuccessDark = Color.FromArgb(21, 128, 61), SuccessLight = Color.FromArgb(220, 252, 231),
            Background = Color.FromArgb(243, 244, 246), Surface = Color.White, SurfaceAlt = Color.FromArgb(249, 250, 251),
            Border = Color.FromArgb(229, 231, 235), BorderStrong = Color.FromArgb(209, 213, 219),
            Neutral = Color.FromArgb(243, 244, 246), NeutralHover = Color.FromArgb(238, 240, 243),
            Text = Color.FromArgb(17, 24, 39), TextMuted = Color.FromArgb(107, 114, 128), TextSubtle = Color.FromArgb(156, 163, 175),
            RowHover = Color.FromArgb(246, 248, 251), RowDivider = Color.FromArgb(241, 243, 245),
            PurpleLight = Color.FromArgb(237, 233, 254), PurpleText = Color.FromArgb(91, 33, 182),
            TealLight = Color.FromArgb(204, 251, 241), TealText = Color.FromArgb(17, 94, 89)
        };

        public static readonly Palette Dark = new Palette
        {
            IsDark = true,
            Accent = Color.FromArgb(59, 130, 246), AccentHover = Color.FromArgb(37, 99, 235), AccentPressed = Color.FromArgb(29, 78, 216),
            AccentLight = Color.FromArgb(30, 58, 98), AccentSoft = Color.FromArgb(29, 41, 61), AccentText = Color.FromArgb(147, 197, 253),
            Indigo = Color.FromArgb(129, 140, 248),
            HeaderStart = Color.FromArgb(30, 64, 175), HeaderEnd = Color.FromArgb(67, 56, 202), HeaderSubtext = Color.FromArgb(205, 215, 245),
            Danger = Color.FromArgb(239, 68, 68), DangerHover = Color.FromArgb(220, 38, 38), DangerPressed = Color.FromArgb(185, 28, 28),
            DangerLight = Color.FromArgb(72, 30, 30),
            Warning = Color.FromArgb(245, 158, 11), WarningDark = Color.FromArgb(252, 211, 77), WarningLight = Color.FromArgb(66, 48, 16),
            WarningPressed = Color.FromArgb(92, 66, 18),
            Success = Color.FromArgb(34, 197, 94), SuccessDark = Color.FromArgb(134, 239, 172), SuccessLight = Color.FromArgb(22, 60, 38),
            Background = Color.FromArgb(28, 28, 30), Surface = Color.FromArgb(40, 40, 43), SurfaceAlt = Color.FromArgb(46, 46, 50),
            Border = Color.FromArgb(58, 58, 63), BorderStrong = Color.FromArgb(82, 82, 89),
            Neutral = Color.FromArgb(56, 56, 61), NeutralHover = Color.FromArgb(54, 54, 59),
            Text = Color.FromArgb(240, 240, 243), TextMuted = Color.FromArgb(166, 166, 175), TextSubtle = Color.FromArgb(118, 118, 128),
            RowHover = Color.FromArgb(48, 48, 53), RowDivider = Color.FromArgb(50, 50, 55),
            PurpleLight = Color.FromArgb(56, 38, 92), PurpleText = Color.FromArgb(216, 180, 254),
            TealLight = Color.FromArgb(19, 70, 66), TealText = Color.FromArgb(153, 246, 228)
        };

        // Cores usadas como fundo (BackColor) e como texto/ícone (ForeColor). Servem para trocar o tema
        // das janelas já abertas: cada cor do tema antigo vira a cor equivalente do tema novo.
        private static readonly string[] BackKeys =
        {
            "Background", "Surface", "SurfaceAlt", "AccentLight", "AccentSoft", "WarningLight", "SuccessLight", "DangerLight", "Neutral", "Accent"
        };
        private static readonly string[] ForeKeys =
        {
            "Text", "TextMuted", "TextSubtle", "Accent", "AccentText", "Danger", "Warning", "WarningDark", "Success", "SuccessDark", "Indigo"
        };

        /// <summary>Converte uma cor deste tema para a cor equivalente de outro tema (ou devolve a mesma cor).</summary>
        public Color Translate(Color c, Palette to, bool foreground)
        {
            if (c.A == 0 || to == this) return c;
            int argb = c.ToArgb();
            foreach (string key in foreground ? ForeKeys : BackKeys)
            {
                FieldInfo f = typeof(Palette).GetField(key);
                if (((Color)f.GetValue(this)).ToArgb() == argb) return (Color)f.GetValue(to);
            }
            return c;
        }
    }

    /// <summary>Controle que guarda cores próprias e precisa convertê-las quando o tema muda.</summary>
    internal interface IThemeRetint
    {
        void Retint(Palette from, Palette to);
    }

    /// <summary>Paleta, fontes e utilitários de desenho da interface.</summary>
    internal static class Theme
    {
        private static Palette p = Palette.Light;

        public static Palette Current { get { return p; } }
        public static bool IsDark { get { return p.IsDark; } }

        public static Color Accent { get { return p.Accent; } }
        public static Color AccentHover { get { return p.AccentHover; } }
        public static Color AccentPressed { get { return p.AccentPressed; } }
        public static Color AccentLight { get { return p.AccentLight; } }
        public static Color AccentSoft { get { return p.AccentSoft; } }
        /// <summary>Azul para texto sobre fundos claros/escuros (botões de contorno, segmentos, etiquetas).</summary>
        public static Color AccentText { get { return p.AccentText; } }
        public static Color Indigo { get { return p.Indigo; } }
        public static Color HeaderStart { get { return p.HeaderStart; } }
        public static Color HeaderEnd { get { return p.HeaderEnd; } }
        public static Color HeaderSubtext { get { return p.HeaderSubtext; } }

        public static Color Danger { get { return p.Danger; } }
        public static Color DangerHover { get { return p.DangerHover; } }
        public static Color DangerPressed { get { return p.DangerPressed; } }
        public static Color DangerLight { get { return p.DangerLight; } }

        public static Color Warning { get { return p.Warning; } }
        public static Color WarningDark { get { return p.WarningDark; } }
        public static Color WarningLight { get { return p.WarningLight; } }
        public static Color WarningPressed { get { return p.WarningPressed; } }

        public static Color Success { get { return p.Success; } }
        public static Color SuccessDark { get { return p.SuccessDark; } }
        public static Color SuccessLight { get { return p.SuccessLight; } }

        public static Color Background { get { return p.Background; } }
        public static Color Surface { get { return p.Surface; } }
        public static Color SurfaceAlt { get { return p.SurfaceAlt; } }
        public static Color Border { get { return p.Border; } }
        public static Color BorderStrong { get { return p.BorderStrong; } }
        /// <summary>Cinza neutro para etiquetas e botões desabilitados.</summary>
        public static Color Neutral { get { return p.Neutral; } }
        public static Color NeutralHover { get { return p.NeutralHover; } }
        public static Color Text { get { return p.Text; } }
        public static Color TextMuted { get { return p.TextMuted; } }
        public static Color TextSubtle { get { return p.TextSubtle; } }
        public static Color RowHover { get { return p.RowHover; } }
        public static Color RowDivider { get { return p.RowDivider; } }
        public static Color PurpleLight { get { return p.PurpleLight; } }
        public static Color PurpleText { get { return p.PurpleText; } }
        public static Color TealLight { get { return p.TealLight; } }
        public static Color TealText { get { return p.TealText; } }

        // ------------------------------------------------------------------ escolha do tema

        private const string PrefKey = @"Software\DesinstaladorCompleto";
        private const string PrefValue = "Tema";

        private static ThemeMode mode = ThemeMode.Sistema;
        private static bool initialized;

        /// <summary>Disparado (na thread da interface) depois que o tema efetivo muda.</summary>
        public static event EventHandler Changed;

        public static ThemeMode Mode { get { return mode; } }

        /// <summary>Lê a preferência salva e passa a acompanhar o tema do Windows. Chamar uma vez, no início.</summary>
        public static void Initialize()
        {
            if (initialized) return;
            initialized = true;
            mode = LoadPreference();
            p = Resolve(mode);
            Native.SetAppDarkMode(p.IsDark);
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }

        /// <summary>Troca o tema e aplica em todas as janelas abertas.</summary>
        public static void SetMode(ThemeMode newMode, bool save = true)
        {
            mode = newMode;
            if (save) SavePreference(newMode);
            ApplyPalette(Resolve(newMode));
        }

        /// <summary>Verdadeiro quando o Windows está configurado com o modo escuro para aplicativos.</summary>
        public static bool SystemPrefersDark()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k != null ? k.GetValue("AppsUseLightTheme") : null;
                    return v is int && (int)v == 0;
                }
            }
            catch { return false; }
        }

        private static Palette Resolve(ThemeMode m)
        {
            if (m == ThemeMode.Claro) return Palette.Light;
            if (m == ThemeMode.Escuro) return Palette.Dark;
            return SystemPrefersDark() ? Palette.Dark : Palette.Light;
        }

        private static ThemeMode LoadPreference()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(PrefKey))
                {
                    string v = k != null ? k.GetValue(PrefValue) as string : null;
                    if (v == "Claro") return ThemeMode.Claro;
                    if (v == "Escuro") return ThemeMode.Escuro;
                }
            }
            catch { }
            return ThemeMode.Sistema;
        }

        private static void SavePreference(ThemeMode m)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(PrefKey))
                    k.SetValue(PrefValue, m.ToString(), RegistryValueKind.String);
            }
            catch { }
        }

        private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (mode != ThemeMode.Sistema) return;
            if (e.Category != UserPreferenceCategory.General && e.Category != UserPreferenceCategory.VisualStyle) return;
            Form owner = Application.OpenForms.Count > 0 ? Application.OpenForms[0] : null;
            if (owner == null) return;
            MethodInvoker apply = () => ApplyPalette(Resolve(ThemeMode.Sistema));
            if (owner.InvokeRequired) owner.BeginInvoke(apply);
            else apply();
        }

        private static void ApplyPalette(Palette next)
        {
            if (next == p) return;
            Palette old = p;
            p = next;
            Native.SetAppDarkMode(p.IsDark);
            var forms = new List<Form>();
            foreach (Form f in Application.OpenForms) forms.Add(f);
            foreach (Form f in forms)
            {
                f.SuspendLayout();
                Retint(f, old, next);
                f.ResumeLayout();
                Native.ApplyWindowTheme(f, p.IsDark);
                f.Invalidate(true);
            }
            if (Changed != null) Changed(null, EventArgs.Empty);
        }

        private static void Retint(Control c, Palette from, Palette to)
        {
            Color back = from.Translate(c.BackColor, to, false);
            if (back != c.BackColor) c.BackColor = back;
            Color fore = from.Translate(c.ForeColor, to, true);
            if (fore != c.ForeColor) c.ForeColor = fore;
            var r = c as IThemeRetint;
            if (r != null) r.Retint(from, to);
            if (c.ContextMenuStrip != null) RetintMenu(c.ContextMenuStrip);
            ApplyNativeTheme(c);
            foreach (Control child in c.Controls) Retint(child, from, to);
        }

        /// <summary>
        /// Prepara uma janela para o tema: barra de título escura/clara e barras de rolagem das listas e caixas de texto.
        /// Chamar no fim do construtor, depois de montar todos os controles.
        /// </summary>
        public static void Attach(Form form)
        {
            form.HandleCreated += (s, e) => Native.ApplyWindowTheme((Form)s, p.IsDark);
            AttachTree(form);
        }

        private static void AttachTree(Control c)
        {
            if (c is ListView || (c is TextBox && ((TextBox)c).Multiline))
            {
                if (c.IsHandleCreated) ApplyNativeTheme(c);
                c.HandleCreated += (s, e) => ApplyNativeTheme((Control)s);
            }
            if (c.ContextMenuStrip != null) RetintMenu(c.ContextMenuStrip);
            foreach (Control child in c.Controls) AttachTree(child);
        }

        private static void ApplyNativeTheme(Control c)
        {
            if (!c.IsHandleCreated) return;
            if (c is ListView || (c is TextBox && ((TextBox)c).Multiline)) Native.ApplyControlTheme(c.Handle, p.IsDark);
        }

        /// <summary>Aplica as cores do tema a um menu (de contexto ou suspenso).</summary>
        public static void RetintMenu(ToolStrip menu)
        {
            if (!(menu.Renderer is ThemedMenuRenderer)) menu.Renderer = new ThemedMenuRenderer();
            menu.BackColor = Surface;
            menu.ForeColor = Text;
            menu.Invalidate();
        }

        public static readonly float Scale;
        public static readonly string GlyphFontName;

        public static readonly Font Body = new Font("Segoe UI", 9.75F);
        public static readonly Font BodyBold = new Font("Segoe UI Semibold", 9.75F);
        public static readonly Font Small = new Font("Segoe UI", 8.75F);
        public static readonly Font SmallBold = new Font("Segoe UI Semibold", 8.25F);
        public static readonly Font Title = new Font("Segoe UI Semibold", 15F);
        public static readonly Font Heading = new Font("Segoe UI Semibold", 12F);
        public static readonly Font Subtitle = new Font("Segoe UI", 10F);

        private static readonly Dictionary<float, Font> glyphFonts = new Dictionary<float, Font>();

        static Theme()
        {
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) Scale = Math.Max(1f, g.DpiX / 96f);
            }
            catch { Scale = 1f; }

            GlyphFontName = "Segoe UI Symbol";
            using (var fonts = new InstalledFontCollection())
            {
                foreach (FontFamily f in fonts.Families)
                {
                    if (f.Name == "Segoe Fluent Icons") { GlyphFontName = f.Name; break; }
                    if (f.Name == "Segoe MDL2 Assets") GlyphFontName = f.Name;
                }
            }
        }

        /// <summary>Converte pixels lógicos (96 DPI) para pixels reais.</summary>
        public static int S(int px)
        {
            return (int)Math.Round(px * Scale);
        }

        public static Font Glyph(float sizePt)
        {
            Font f;
            if (!glyphFonts.TryGetValue(sizePt, out f))
            {
                f = new Font(GlyphFontName, sizePt);
                glyphFonts[sizePt] = f;
            }
            return f;
        }

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRound(Graphics g, Rectangle r, int radius, Color fill)
        {
            using (GraphicsPath p = RoundRect(r, radius))
            using (var b = new SolidBrush(fill))
                g.FillPath(b, p);
        }

        public static void DrawRound(Graphics g, Rectangle r, int radius, Color border, float width = 1f)
        {
            using (GraphicsPath p = RoundRect(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), radius))
            using (var pen = new Pen(border, width))
                g.DrawPath(pen, p);
        }

        public static void DrawGlyph(Graphics g, string glyph, float sizePt, Color color, Rectangle bounds)
        {
            TextRenderer.DrawText(g, glyph, Glyph(sizePt), bounds, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }

        /// <summary>Etiqueta arredondada ("pílula") com texto.</summary>
        public static void DrawPill(Graphics g, Rectangle area, string text, Color bg, Color fg)
        {
            Size ts = TextRenderer.MeasureText(g, text, SmallBold, Size.Empty, TextFormatFlags.NoPadding);
            int w = ts.Width + S(16), h = ts.Height + S(6);
            var r = new Rectangle(area.X, area.Y + (area.Height - h) / 2, Math.Min(w, area.Width), h);
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            FillRound(g, r, h / 2, bg);
            g.SmoothingMode = old;
            TextRenderer.DrawText(g, text, SmallBold, r, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        /// <summary>Gera um bitmap com um ícone da fonte de símbolos do Windows.</summary>
        public static Bitmap GlyphBitmap(string glyph, int size, Color color)
        {
            var bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using (var f = new Font(GlyphFontName, size * 0.62f, GraphicsUnit.Pixel))
                using (var b = new SolidBrush(color))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(glyph, f, b, new RectangleF(0, 0, size, size), sf);
            }
            return bmp;
        }

        /// <summary>
        /// Pinta o fundo do controle pai por baixo do controle (substitui a "transparência" do WinForms,
        /// que deixa restos de outros controles quando há buffer duplo).
        /// </summary>
        public static void PaintParentBackground(Control c, Graphics g)
        {
            Control p = c.Parent;
            if (p is GradientHeader && p.Width > 0 && p.Height > 0)
            {
                using (var b = new LinearGradientBrush(new Rectangle(-c.Left, -c.Top, p.Width, p.Height), HeaderStart, HeaderEnd, LinearGradientMode.Horizontal))
                    g.FillRectangle(b, new Rectangle(0, 0, c.Width, c.Height));
            }
            else g.Clear(p != null ? p.BackColor : Background);
        }

        /// <summary>Funções nativas do Windows para o modo escuro (barra de título e barras de rolagem).</summary>
        private static class Native
        {
            [DllImport("dwmapi.dll")]
            private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

            [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
            private static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

            // Funções sem nome exportado do uxtheme.dll (Windows 10 1903 ou superior), usadas pelo próprio Explorer.
            [DllImport("uxtheme.dll", EntryPoint = "#133")]
            private static extern bool AllowDarkModeForWindow(IntPtr hwnd, bool allow);

            [DllImport("uxtheme.dll", EntryPoint = "#135")]
            private static extern int SetPreferredAppMode(int mode);

            [DllImport("uxtheme.dll", EntryPoint = "#136")]
            private static extern void FlushMenuThemes();

            [DllImport("user32.dll")]
            private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

            private static readonly int build = GetBuild();

            private static int GetBuild()
            {
                try
                {
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                    {
                        int b;
                        if (k != null && int.TryParse(k.GetValue("CurrentBuildNumber") as string, out b)) return b;
                    }
                }
                catch { }
                return 0;
            }

            private static bool UndocumentedOk { get { return build >= 18362; } }

            public static void SetAppDarkMode(bool dark)
            {
                if (!UndocumentedOk) return;
                try
                {
                    SetPreferredAppMode(dark ? 2 : 3);   // 2 = sempre escuro, 3 = sempre claro
                    FlushMenuThemes();
                }
                catch { }
            }

            /// <summary>Barra de título escura ou clara.</summary>
            public static void ApplyWindowTheme(Form f, bool dark)
            {
                if (!f.IsHandleCreated || build < 17763) return;
                try
                {
                    int v = dark ? 1 : 0;
                    int attr = build >= 18985 ? 20 : 19;   // DWMWA_USE_IMMERSIVE_DARK_MODE
                    DwmSetWindowAttribute(f.Handle, attr, ref v, sizeof(int));
                    // Força o Windows a redesenhar a moldura com a nova cor
                    SetWindowPos(f.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020);
                }
                catch { }
            }

            /// <summary>Barras de rolagem escuras ou claras em listas e caixas de texto.</summary>
            public static void ApplyControlTheme(IntPtr hwnd, bool dark)
            {
                try
                {
                    if (UndocumentedOk) AllowDarkModeForWindow(hwnd, dark);
                    // Claro: volta ao tema padrão do controle. Escuro: tema escuro do Explorer (barras de rolagem escuras).
                    if (dark) SetWindowTheme(hwnd, "DarkMode_Explorer", null);
                    else SetWindowTheme(hwnd, null, null);
                }
                catch { }
            }
        }

        public static Color Blend(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }
    }

    /// <summary>Desenha menus (de contexto e suspensos) com as cores do tema atual.</summary>
    internal class ThemedMenuRenderer : ToolStripProfessionalRenderer
    {
        public ThemedMenuRenderer() : base(new ThemedColorTable()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.TextSubtle;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle r = e.ImageRectangle;
            if (r.Width <= 0) r = new Rectangle(Theme.S(4), 0, Theme.S(20), e.Item.Height);
            Theme.DrawGlyph(e.Graphics, Glyphs.Check, 9f, Theme.Accent, new Rectangle(r.X, 0, r.Width, e.Item.Height));
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.TextMuted;
            base.OnRenderArrow(e);
        }

        private class ThemedColorTable : ProfessionalColorTable
        {
            public ThemedColorTable() { UseSystemColors = false; }

            public override Color ToolStripDropDownBackground { get { return Theme.Surface; } }
            public override Color ImageMarginGradientBegin { get { return Theme.Surface; } }
            public override Color ImageMarginGradientMiddle { get { return Theme.Surface; } }
            public override Color ImageMarginGradientEnd { get { return Theme.Surface; } }
            public override Color MenuBorder { get { return Theme.BorderStrong; } }
            public override Color MenuItemBorder { get { return Theme.RowHover; } }
            public override Color MenuItemSelected { get { return Theme.RowHover; } }
            public override Color MenuItemSelectedGradientBegin { get { return Theme.RowHover; } }
            public override Color MenuItemSelectedGradientEnd { get { return Theme.RowHover; } }
            public override Color MenuItemPressedGradientBegin { get { return Theme.AccentLight; } }
            public override Color MenuItemPressedGradientEnd { get { return Theme.AccentLight; } }
            public override Color SeparatorDark { get { return Theme.Border; } }
            public override Color SeparatorLight { get { return Theme.Border; } }
            public override Color CheckBackground { get { return Theme.Surface; } }
            public override Color CheckSelectedBackground { get { return Theme.RowHover; } }
            public override Color CheckPressedBackground { get { return Theme.AccentLight; } }
        }
    }

    /// <summary>Códigos dos ícones da fonte "Segoe MDL2 Assets" / "Segoe Fluent Icons".</summary>
    internal static class Glyphs
    {
        public const string ColorTheme = "";
        public const string Delete = "";
        public const string Search = "";
        public const string Refresh = "";
        public const string FolderOpen = "";
        public const string Folder = "";
        public const string Info = "";
        public const string Warning = "";
        public const string Check = "";
        public const string Cancel = "";
        public const string Document = "";
        public const string Link = "";
        public const string Settings = "";
        public const string Calendar = "";
        public const string Shield = "";
        public const string Code = "";
        public const string History = "";
        public const string Store = "";
        public const string Broom = "";
        public const string Stop = "";
        public const string Repair = "";
        public const string ChevronDown = "";
        public const string ChevronUp = "";
        public const string SelectAll = "";
        public const string ClearSelection = "";
        public const string Accept = "";
        public const string List = "";
        public const string Globe = "";
        public const string Clock = "";
        public const string Tag = "";
        public const string App = "";
        public const string Filter = "";
        public const string Recycle = "";
    }
}
