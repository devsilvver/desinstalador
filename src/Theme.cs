using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace DesinstaladorCompleto
{
    /// <summary>Paleta, fontes e utilitários de desenho da interface.</summary>
    internal static class Theme
    {
        public static readonly Color Accent = Color.FromArgb(37, 99, 235);
        public static readonly Color AccentHover = Color.FromArgb(29, 78, 216);
        public static readonly Color AccentPressed = Color.FromArgb(30, 64, 175);
        public static readonly Color AccentLight = Color.FromArgb(219, 234, 254);
        public static readonly Color AccentSoft = Color.FromArgb(239, 246, 255);
        public static readonly Color Indigo = Color.FromArgb(79, 70, 229);

        public static readonly Color Danger = Color.FromArgb(220, 38, 38);
        public static readonly Color DangerHover = Color.FromArgb(185, 28, 28);
        public static readonly Color DangerPressed = Color.FromArgb(153, 27, 27);
        public static readonly Color DangerLight = Color.FromArgb(254, 226, 226);

        public static readonly Color Warning = Color.FromArgb(217, 119, 6);
        public static readonly Color WarningDark = Color.FromArgb(180, 83, 9);
        public static readonly Color WarningLight = Color.FromArgb(254, 243, 199);

        public static readonly Color Success = Color.FromArgb(22, 163, 74);
        public static readonly Color SuccessDark = Color.FromArgb(21, 128, 61);
        public static readonly Color SuccessLight = Color.FromArgb(220, 252, 231);

        public static readonly Color Background = Color.FromArgb(243, 244, 246);
        public static readonly Color Surface = Color.White;
        public static readonly Color SurfaceAlt = Color.FromArgb(249, 250, 251);
        public static readonly Color Border = Color.FromArgb(229, 231, 235);
        public static readonly Color BorderStrong = Color.FromArgb(209, 213, 219);
        public static readonly Color Text = Color.FromArgb(17, 24, 39);
        public static readonly Color TextMuted = Color.FromArgb(107, 114, 128);
        public static readonly Color TextSubtle = Color.FromArgb(156, 163, 175);
        public static readonly Color RowHover = Color.FromArgb(246, 248, 251);

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
                using (var b = new LinearGradientBrush(new Rectangle(-c.Left, -c.Top, p.Width, p.Height), Accent, Indigo, LinearGradientMode.Horizontal))
                    g.FillRectangle(b, new Rectangle(0, 0, c.Width, c.Height));
            }
            else g.Clear(p != null ? p.BackColor : Background);
        }

        public static Color Blend(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }
    }

    /// <summary>Códigos dos ícones da fonte "Segoe MDL2 Assets" / "Segoe Fluent Icons".</summary>
    internal static class Glyphs
    {
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
