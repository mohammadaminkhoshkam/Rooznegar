using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Rooznegar
{
    /// <summary>Colour tokens. Every surface reads these at paint time, so switching theme is a repaint.</summary>
    public static class Palette
    {
        public static bool Dark;
        public static Color Bg, Surface, Surface2, Sunken, Sidebar, Ink, Ink2, Ink3, Line, LineStrong;
        public static Color Accent, AccentHover, AccentSoft, AccentInk, Danger, DangerSoft, Warning, WarningSoft, Success;
        public static readonly string[] ProjectColors = {
            "#0F766E", "#3B6FD8", "#6457D6", "#9A55B4", "#D2557F", "#D9534F",
            "#E07B39", "#D99A21", "#7A9A2E", "#2F9D63", "#1A9BB5", "#5D6B82" };

        public static void Apply(bool dark)
        {
            Dark = dark;
            if (!dark)
            {
                Bg = Hex("#F4F7F6"); Surface = Color.White; Surface2 = Hex("#F0F5F3"); Sunken = Hex("#E9F0EE"); Sidebar = Hex("#FBFCFC");
                Ink = Hex("#152B28"); Ink2 = Hex("#536966"); Ink3 = Hex("#8A9C98"); Line = Hex("#E2EAE7"); LineStrong = Hex("#CBD8D4");
                Accent = Hex("#0F766E"); AccentHover = Hex("#0B5E57"); AccentSoft = Hex("#E0F2EE"); AccentInk = Color.White;
                Danger = Hex("#D64545"); DangerSoft = Hex("#FCE9E9"); Warning = Hex("#C98410"); WarningSoft = Hex("#FFF4DE"); Success = Hex("#2F9D63");
            }
            else
            {
                Bg = Hex("#0E1413"); Surface = Hex("#151D1C"); Surface2 = Hex("#1B2624"); Sunken = Hex("#111918"); Sidebar = Hex("#111817");
                Ink = Hex("#E7EFED"); Ink2 = Hex("#A3B5B1"); Ink3 = Hex("#6E827E"); Line = Hex("#24312F"); LineStrong = Hex("#34443F");
                Accent = Hex("#2BB5A4"); AccentHover = Hex("#45C9B8"); AccentSoft = Hex("#16332F"); AccentInk = Hex("#04201C");
                Danger = Hex("#F06A6A"); DangerSoft = Hex("#3A1D1F"); Warning = Hex("#F0B44C"); WarningSoft = Hex("#3A2E15"); Success = Hex("#4CC08A");
            }
        }
        public static Color Hex(string html) { try { return ColorTranslator.FromHtml(html); } catch { return Color.Teal; } }
        public static Color Project(string html)
        {
            var c = Hex(html);
            return Dark ? Mix(c, Color.White, 0.18) : c;
        }
        public static Color Mix(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }
        public static Color Alpha(Color c, int alpha) { return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), c); }
        /// <summary>Follows Windows' "app mode" (light/dark) when the theme setting is «system».</summary>
        public static bool SystemPrefersDark()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = key == null ? null : key.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 0;
                }
            }
            catch { return false; }
        }
    }

    public enum Weight { Regular, Medium, Bold, Black }

    /// <summary>Records unexpected errors to errors.log next to the data instead of crashing the app.</summary>
    public static class ErrorLog
    {
        public static string Directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rooznegar");
        public static int Count;
        public static string Last = "";
        public static void Write(string where, Exception e)
        {
            Count++; Last = where + ": " + e;
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                string path = Path.Combine(Directory, "errors.log");
                if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024) File.Delete(path);
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + "  [" + where + "]  " + e + Environment.NewLine + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }
    }

    /// <summary>Loads the embedded Vazirmatn family once and hands out cached Font objects.</summary>
    public static class Fonts
    {
        [DllImport("gdi32.dll")] private static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, ref uint pcFonts);
        private static readonly PrivateFontCollection collection = new PrivateFontCollection();
        private static readonly Dictionary<string, Font> cache = new Dictionary<string, Font>();
        private static FontFamily regular, medium, black;
        public static string Status = "";
        public static bool Loaded { get { return regular != null; } }

        public static void Init()
        {
            var asm = typeof(Fonts).Assembly;
            foreach (string file in new[] { "Vazirmatn-Regular.ttf", "Vazirmatn-Bold.ttf", "Vazirmatn-Medium.ttf", "Vazirmatn-ExtraBold.ttf" })
            {
                try
                {
                    byte[] data = null;
                    using (var stream = asm.GetManifestResourceStream("Rooznegar.Fonts." + file))
                        if (stream != null) { data = new byte[stream.Length]; int read = 0; while (read < data.Length) { int n = stream.Read(data, read, data.Length - read); if (n <= 0) break; read += n; } }
                    if (data == null)
                    {
                        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fonts", file);
                        if (File.Exists(path)) data = File.ReadAllBytes(path);
                    }
                    if (data == null) continue;
                    // The memory must outlive the collection, so it is intentionally never freed.
                    IntPtr mem = Marshal.AllocCoTaskMem(data.Length);
                    Marshal.Copy(data, 0, mem, data.Length);
                    collection.AddMemoryFont(mem, data.Length);
                    if (!Ui.IsMono) { uint count = 0; AddFontMemResourceEx(mem, (uint)data.Length, IntPtr.Zero, ref count); }
                }
                catch (Exception e) { Status += file + ": " + e.Message + "\n"; }
            }
            foreach (var f in collection.Families)
            {
                if (f.Name == "Vazirmatn") regular = f;
                else if (f.Name == "Vazirmatn Medium") medium = f;
                else if (f.Name == "Vazirmatn ExtraBold") black = f;
            }
        }
        public static Font Get(float size, Weight weight = Weight.Regular)
        {
            size = (float)Math.Round(size * Ui.FontFactor * 4) / 4f;
            string key = size.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + weight;
            Font f;
            if (cache.TryGetValue(key, out f)) return f;
            try
            {
                if (regular == null) f = new Font(FallbackName(), size, weight == Weight.Regular ? FontStyle.Regular : FontStyle.Bold);
                else if (weight == Weight.Medium && medium != null) f = new Font(medium, size, FontStyle.Regular);
                else if (weight == Weight.Black && black != null) f = new Font(black, size, FontStyle.Regular);
                else if (weight == Weight.Regular) f = new Font(regular, size, FontStyle.Regular);
                else f = new Font(regular, size, regular.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular);
            }
            catch { f = new Font(FontFamily.GenericSansSerif, size, weight == Weight.Regular ? FontStyle.Regular : FontStyle.Bold); }
            cache[key] = f; return f;
        }
        private static string FallbackName()
        {
            using (var installed = new InstalledFontCollection())
                foreach (string name in new[] { "Vazirmatn", "Segoe UI", "Tahoma" })
                    if (installed.Families.Any(x => x.Name == name)) return name;
            return FontFamily.GenericSansSerif.Name;
        }
    }

    public enum Align { Right, Left, Center }

    /// <summary>Drawing helpers shared by every custom-painted surface.</summary>
    public static class Ui
    {
        public static readonly bool IsMono = Type.GetType("Mono.Runtime") != null;
        public static float Scale = 1f;
        public static int S(double v) { return (int)Math.Round(v * Scale); }
        /// <summary>Font sizes are in points (already DPI-scaled by Windows); this shrinks them when the layout is capped.</summary>
        public static float FontFactor = 1f;
        public static void InitScale()
        {
            float dpi = 1f;
            try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) dpi = Math.Max(1f, g.DpiX / 96f); } catch { }
            Scale = dpi;
            // Small screens with high scaling (e.g. 1366×768 at 125%) cannot fit the 1080×700 design;
            // cap the layout scale so the window still fits, and shrink fonts by the same ratio.
            try
            {
                var area = Screen.PrimaryScreen.WorkingArea;
                float fit = Math.Min(area.Width / 1100f, area.Height / 720f);
                if (fit < Scale) Scale = Math.Max(1f, fit);
            }
            catch { }
            FontFactor = Scale / dpi;
        }
        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 1) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure(); return path;
        }
        public static void Fill(Graphics g, Rectangle r, Color c, int radius)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (var b = new SolidBrush(c))
            {
                if (radius <= 0) { g.FillRectangle(b, r); return; }
                using (var p = Round(r, radius)) g.FillPath(b, p);
            }
        }
        public static void Stroke(Graphics g, Rectangle r, Color c, int radius, float width = 1f)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (var pen = new Pen(c, width))
            using (var p = Round(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), radius)) g.DrawPath(pen, p);
        }
        /// <summary>Card: rounded fill with a hairline border.</summary>
        public static void Card(Graphics g, Rectangle r, Color? fill = null, Color? border = null, int radius = -1)
        {
            if (radius < 0) radius = S(14);
            Fill(g, r, fill ?? Palette.Surface, radius);
            Stroke(g, r, border ?? Palette.Line, radius);
        }
        public static void Circle(Graphics g, RectangleF r, Color c)
        {
            using (var b = new SolidBrush(c)) g.FillEllipse(b, r);
        }

        private static TextFormatFlags Flags(Align align, bool multiline, bool rtl)
        {
            // GDI text ignores GDI+ transforms/clips unless asked; scrolled surfaces rely on the transform.
            var f = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.PreserveGraphicsClipping;
            f |= multiline ? TextFormatFlags.WordBreak | TextFormatFlags.Top : TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            if (align == Align.Center) f |= TextFormatFlags.HorizontalCenter;
            else if (align == Align.Right) f |= TextFormatFlags.Right;
            if (rtl && !IsMono) f |= TextFormatFlags.RightToLeft;
            return f;
        }
        public static void Text(Graphics g, string text, Font font, Color color, Rectangle r, Align align = Align.Right, bool multiline = false, bool rtl = true)
        {
            if (string.IsNullOrEmpty(text) || r.Width <= 0) return;
            if (IsMono) text = rtl ? Shaper.Visual(text) : text;
            TextRenderer.DrawText(g, text, font, r, color, Flags(align, multiline, rtl));
        }
        public static Size Measure(Graphics g, string text, Font font, bool rtl = true)
        {
            if (string.IsNullOrEmpty(text)) return Size.Empty;
            if (IsMono && rtl) text = Shaper.Visual(text);
            return TextRenderer.MeasureText(g, text, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }
        public static int MeasureHeight(Graphics g, string text, Font font, int width)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            if (IsMono) text = Shaper.Visual(text);
            return TextRenderer.MeasureText(g, text, font, new Size(width, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.WordBreak).Height;
        }
        public static void HighQuality(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }
        public static Color Readable(Color background)
        {
            return background.GetBrightness() > 0.62 || (background.R * 0.299 + background.G * 0.587 + background.B * 0.114) > 170 ? Color.FromArgb(20, 32, 30) : Color.White;
        }
    }

    /// <summary>
    /// Under Mono (used only for headless previews) libgdiplus has no Arabic shaping or bidi, so text is
    /// converted to presentation forms and visual order there. On Windows, GDI/Uniscribe does this natively.
    /// </summary>
    public static class Shaper
    {
        private static readonly Dictionary<char, int[]> forms = new Dictionary<char, int[]>();
        private static void F(char c, int iso, int count)
        {
            // count 2 = right-joining (iso, fin); 4 = dual-joining (iso, fin, ini, med)
            forms[c] = count == 4 ? new[] { iso, iso + 1, iso + 2, iso + 3 } : count == 2 ? new[] { iso, iso + 1, -1, -1 } : new[] { iso, -1, -1, -1 };
        }
        static Shaper()
        {
            F('ء', 0xFE80, 1); F('آ', 0xFE81, 2); F('أ', 0xFE83, 2); F('ؤ', 0xFE85, 2); F('إ', 0xFE87, 2); F('ئ', 0xFE89, 4);
            F('ا', 0xFE8D, 2); F('ب', 0xFE8F, 4); F('ة', 0xFE93, 2); F('ت', 0xFE95, 4); F('ث', 0xFE99, 4); F('ج', 0xFE9D, 4);
            F('ح', 0xFEA1, 4); F('خ', 0xFEA5, 4); F('د', 0xFEA9, 2); F('ذ', 0xFEAB, 2); F('ر', 0xFEAD, 2); F('ز', 0xFEAF, 2);
            F('س', 0xFEB1, 4); F('ش', 0xFEB5, 4); F('ص', 0xFEB9, 4); F('ض', 0xFEBD, 4); F('ط', 0xFEC1, 4); F('ظ', 0xFEC5, 4);
            F('ع', 0xFEC9, 4); F('غ', 0xFECD, 4); F('ف', 0xFED1, 4); F('ق', 0xFED5, 4); F('ك', 0xFED9, 4); F('ل', 0xFEDD, 4);
            F('م', 0xFEE1, 4); F('ن', 0xFEE5, 4); F('ه', 0xFEE9, 4); F('و', 0xFEED, 2); F('ى', 0xFEEF, 2); F('ي', 0xFEF1, 4);
            F('پ', 0xFB56, 4); F('چ', 0xFB7A, 4); F('ژ', 0xFB8A, 2); F('ک', 0xFB8E, 4); F('گ', 0xFB92, 4); F('ی', 0xFBFC, 4);
        }
        private static bool Dual(char c) { int[] f; return forms.TryGetValue(c, out f) && f[2] >= 0; }
        private static bool Joins(char c) { int[] f; return forms.TryGetValue(c, out f) && f[1] >= 0; }
        private static bool Mark(char c) { return c >= 'ً' && c <= 'ْ'; }
        private static string Shape(string s)
        {
            var b = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i]; int[] f;
                if (!forms.TryGetValue(c, out f)) { b.Append(c); continue; }
                int p = i - 1; while (p >= 0 && Mark(s[p])) p--;
                int n = i + 1; while (n < s.Length && Mark(s[n])) n++;
                bool prev = p >= 0 && Dual(s[p]);
                if (c == 'ل' && n < s.Length && (s[n] == 'ا' || s[n] == 'آ'))
                {
                    b.Append((char)(s[n] == 'ا' ? (prev ? 0xFEFC : 0xFEFB) : (prev ? 0xFEF6 : 0xFEF5))); i = n; continue;
                }
                bool next = n < s.Length && Joins(s[n]) && f[2] >= 0;
                int form = prev && next ? 3 : prev ? 1 : next ? 2 : 0;
                if (f[form] < 0) form = prev && f[1] >= 0 ? 1 : 0;
                b.Append((char)f[form]);
            }
            return b.ToString();
        }
        private static bool Ltr(char c)
        {
            return (c >= '0' && c <= '9') || (c >= '۰' && c <= '۹') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
        }
        private static char Mirror(char c) { return c == '(' ? ')' : c == ')' ? '(' : c == '«' ? '»' : c == '»' ? '«' : c == '[' ? ']' : c == ']' ? '[' : c; }
        public static string Visual(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (text.IndexOf('\n') >= 0) return string.Join("\n", text.Split('\n').Select(Visual).ToArray());
            bool hasRtl = text.Any(c => c >= '؀' && c <= 'ۿ' && !(c >= '۰' && c <= '۹'));
            if (!hasRtl) return text;
            string shaped = Shape(text.Replace("‌", "​"));
            // Split into LTR runs (letters/digits plus separators between them) and RTL/neutral runs.
            var runs = new List<KeyValuePair<bool, string>>();
            int i = 0;
            while (i < shaped.Length)
            {
                if (Ltr(shaped[i]))
                {
                    int j = i + 1;
                    while (j < shaped.Length && (Ltr(shaped[j]) || ((":/.,٫%٪-_".IndexOf(shaped[j]) >= 0 || shaped[j] == ' ' && j + 1 < shaped.Length && (shaped[j + 1] >= 'A' && shaped[j + 1] <= 'z')) && j + 1 < shaped.Length && Ltr(shaped[j + 1])))) j++;
                    runs.Add(new KeyValuePair<bool, string>(true, shaped.Substring(i, j - i))); i = j;
                }
                else
                {
                    int j = i + 1; while (j < shaped.Length && !Ltr(shaped[j])) j++;
                    runs.Add(new KeyValuePair<bool, string>(false, shaped.Substring(i, j - i))); i = j;
                }
            }
            var b = new StringBuilder();
            for (int r = runs.Count - 1; r >= 0; r--)
            {
                if (runs[r].Key) b.Append(runs[r].Value);
                else { var chars = runs[r].Value.ToCharArray(); Array.Reverse(chars); foreach (char c in chars) b.Append(Mirror(c)); }
            }
            return b.ToString();
        }
    }

    public enum Glyph
    {
        Dashboard, Timesheet, Reports, Settings, Play, Stop, Plus, Edit, Trash, Search, Calendar, ChevronRight, ChevronLeft,
        Download, Sun, Moon, Clock, Flame, Note, Close, Check, Archive, Shield, Folder, Keyboard, Coffee, Target, Widget, Undo, Upload, Logo, Bolt, Power
    }

    /// <summary>Line icons drawn as vector paths on a 24×24 grid, so they stay crisp at any DPI.</summary>
    public static class Icons
    {
        public static void Draw(Graphics g, Glyph glyph, RectangleF r, Color color, float stroke = 1.8f)
        {
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float k = Math.Min(r.Width, r.Height) / 24f;
            g.TranslateTransform(r.X + (r.Width - 24 * k) / 2, r.Y + (r.Height - 24 * k) / 2);
            g.ScaleTransform(k, k);
            using (var pen = new Pen(color, stroke * Ui.Scale / Math.Max(0.2f, k)))
            using (var brush = new SolidBrush(color))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                switch (glyph)
                {
                    case Glyph.Dashboard:
                        R(g, pen, 3.5f, 3.5f, 7, 7, 2); R(g, pen, 13.5f, 3.5f, 7, 7, 2); R(g, pen, 3.5f, 13.5f, 7, 7, 2); R(g, pen, 13.5f, 13.5f, 7, 7, 2); break;
                    case Glyph.Timesheet:
                        R(g, pen, 3.5f, 4.5f, 17, 16, 3); g.DrawLine(pen, 3.5f, 9.5f, 20.5f, 9.5f); g.DrawLine(pen, 8, 2.5f, 8, 6); g.DrawLine(pen, 16, 2.5f, 16, 6);
                        g.DrawLine(pen, 7.5f, 13.5f, 12, 13.5f); g.DrawLine(pen, 7.5f, 16.8f, 15.5f, 16.8f); break;
                    case Glyph.Calendar:
                        R(g, pen, 3.5f, 4.5f, 17, 16, 3); g.DrawLine(pen, 3.5f, 9.5f, 20.5f, 9.5f); g.DrawLine(pen, 8, 2.5f, 8, 6); g.DrawLine(pen, 16, 2.5f, 16, 6);
                        g.FillEllipse(brush, 7, 12.5f, 2.4f, 2.4f); g.FillEllipse(brush, 11, 12.5f, 2.4f, 2.4f); g.FillEllipse(brush, 15, 12.5f, 2.4f, 2.4f); g.FillEllipse(brush, 7, 16, 2.4f, 2.4f); break;
                    case Glyph.Reports:
                        g.DrawLine(pen, 4, 20, 20, 20); R(g, pen, 5.5f, 11, 3.2f, 6.5f, 1); R(g, pen, 10.4f, 5, 3.2f, 12.5f, 1); R(g, pen, 15.3f, 8.5f, 3.2f, 9, 1); break;
                    case Glyph.Settings:
                        for (int i = 0; i < 8; i++)
                        {
                            double a = i * Math.PI / 4;
                            g.DrawLine(pen, 12 + (float)Math.Cos(a) * 7.2f, 12 + (float)Math.Sin(a) * 7.2f, 12 + (float)Math.Cos(a) * 9.4f, 12 + (float)Math.Sin(a) * 9.4f);
                        }
                        g.DrawEllipse(pen, 5.2f, 5.2f, 13.6f, 13.6f); g.DrawEllipse(pen, 9, 9, 6, 6); break;
                    case Glyph.Play:
                        using (var p = new GraphicsPath()) { p.AddPolygon(new[] { new PointF(8, 5.2f), new PointF(19, 12), new PointF(8, 18.8f) }); g.FillPath(brush, p); g.DrawPath(pen, p); }
                        break;
                    case Glyph.Stop:
                        using (var p = Ui.Round(new RectangleF(6.5f, 6.5f, 11, 11), 2.2f)) g.FillPath(brush, p); break;
                    case Glyph.Plus: g.DrawLine(pen, 12, 5, 12, 19); g.DrawLine(pen, 5, 12, 19, 12); break;
                    case Glyph.Close: g.DrawLine(pen, 6.5f, 6.5f, 17.5f, 17.5f); g.DrawLine(pen, 17.5f, 6.5f, 6.5f, 17.5f); break;
                    case Glyph.Check: g.DrawLines(pen, new[] { new PointF(5, 12.5f), new PointF(10, 17.2f), new PointF(19, 7) }); break;
                    case Glyph.Edit:
                        g.DrawLines(pen, new[] { new PointF(4.5f, 19.5f), new PointF(5.2f, 15.6f), new PointF(15.6f, 5.2f), new PointF(18.8f, 8.4f), new PointF(8.4f, 18.8f), new PointF(4.5f, 19.5f) });
                        g.DrawLine(pen, 13.4f, 7.4f, 16.6f, 10.6f); break;
                    case Glyph.Trash:
                        g.DrawLine(pen, 4.5f, 6.5f, 19.5f, 6.5f); g.DrawLines(pen, new[] { new PointF(9.5f, 6.5f), new PointF(9.5f, 4), new PointF(14.5f, 4), new PointF(14.5f, 6.5f) });
                        g.DrawLines(pen, new[] { new PointF(6.5f, 6.5f), new PointF(7.5f, 20), new PointF(16.5f, 20), new PointF(17.5f, 6.5f) });
                        g.DrawLine(pen, 10.3f, 10.5f, 10.3f, 16.5f); g.DrawLine(pen, 13.7f, 10.5f, 13.7f, 16.5f); break;
                    case Glyph.Search: g.DrawEllipse(pen, 4.5f, 4.5f, 11.5f, 11.5f); g.DrawLine(pen, 14.5f, 14.5f, 19.5f, 19.5f); break;
                    case Glyph.ChevronRight: g.DrawLines(pen, new[] { new PointF(9.5f, 5.5f), new PointF(16, 12), new PointF(9.5f, 18.5f) }); break;
                    case Glyph.ChevronLeft: g.DrawLines(pen, new[] { new PointF(14.5f, 5.5f), new PointF(8, 12), new PointF(14.5f, 18.5f) }); break;
                    case Glyph.Download:
                        g.DrawLine(pen, 12, 4, 12, 15); g.DrawLines(pen, new[] { new PointF(7.5f, 10.8f), new PointF(12, 15.2f), new PointF(16.5f, 10.8f) });
                        g.DrawLines(pen, new[] { new PointF(4.5f, 15.5f), new PointF(4.5f, 19.5f), new PointF(19.5f, 19.5f), new PointF(19.5f, 15.5f) }); break;
                    case Glyph.Upload:
                        g.DrawLine(pen, 12, 15, 12, 4.5f); g.DrawLines(pen, new[] { new PointF(7.5f, 8.8f), new PointF(12, 4.4f), new PointF(16.5f, 8.8f) });
                        g.DrawLines(pen, new[] { new PointF(4.5f, 15.5f), new PointF(4.5f, 19.5f), new PointF(19.5f, 19.5f), new PointF(19.5f, 15.5f) }); break;
                    case Glyph.Sun:
                        g.DrawEllipse(pen, 8, 8, 8, 8);
                        for (int i = 0; i < 8; i++) { double a = i * Math.PI / 4; g.DrawLine(pen, 12 + (float)Math.Cos(a) * 6.8f, 12 + (float)Math.Sin(a) * 6.8f, 12 + (float)Math.Cos(a) * 9f, 12 + (float)Math.Sin(a) * 9f); }
                        break;
                    case Glyph.Moon:
                        using (var outer = new GraphicsPath()) using (var inner = new GraphicsPath())
                        {
                            outer.AddEllipse(4, 4, 16, 16); inner.AddEllipse(9.5f, 1.5f, 14, 14);
                            using (var region = new Region(outer)) { region.Exclude(inner); g.FillRegion(brush, region); }
                        }
                        break;
                    case Glyph.Clock: g.DrawEllipse(pen, 3.5f, 3.5f, 17, 17); g.DrawLines(pen, new[] { new PointF(12, 7.5f), new PointF(12, 12), new PointF(15.2f, 14) }); break;
                    case Glyph.Flame:
                        using (var p = new GraphicsPath())
                        {
                            p.AddBezier(12, 21, 6.5f, 21, 4.8f, 16.5f, 6.4f, 12.6f); p.AddBezier(6.4f, 12.6f, 7.4f, 10.4f, 9.4f, 9.2f, 9.6f, 6.5f);
                            p.AddBezier(9.6f, 6.5f, 9.6f, 5.2f, 9.2f, 4, 9, 3.2f); p.AddBezier(9, 3.2f, 13.5f, 4.8f, 15.5f, 8.5f, 15.2f, 11.4f);
                            p.AddBezier(15.2f, 11.4f, 16.3f, 10.6f, 16.8f, 9.6f, 17, 8.6f); p.AddBezier(17, 8.6f, 19.6f, 12.4f, 19.6f, 21, 12, 21);
                            g.DrawPath(pen, p);
                        }
                        break;
                    case Glyph.Note:
                        R(g, pen, 5, 3.5f, 14, 17, 2.5f); g.DrawLine(pen, 8.5f, 8.5f, 15.5f, 8.5f); g.DrawLine(pen, 8.5f, 12, 15.5f, 12); g.DrawLine(pen, 8.5f, 15.5f, 12.5f, 15.5f); break;
                    case Glyph.Archive:
                        R(g, pen, 3.5f, 4.5f, 17, 4.5f, 1.2f); g.DrawLines(pen, new[] { new PointF(5, 9), new PointF(5, 19.5f), new PointF(19, 19.5f), new PointF(19, 9) }); g.DrawLine(pen, 10, 13, 14, 13); break;
                    case Glyph.Shield:
                        g.DrawLines(pen, new[] { new PointF(12, 3.5f), new PointF(19, 6.2f), new PointF(19, 11.5f) }); g.DrawBezier(pen, 19, 11.5f, 19, 16, 15.8f, 19, 12, 20.5f);
                        g.DrawBezier(pen, 12, 20.5f, 8.2f, 19, 5, 16, 5, 11.5f); g.DrawLines(pen, new[] { new PointF(5, 11.5f), new PointF(5, 6.2f), new PointF(12, 3.5f) });
                        g.DrawLines(pen, new[] { new PointF(9, 12), new PointF(11.3f, 14.3f), new PointF(15.3f, 10) }); break;
                    case Glyph.Folder:
                        g.DrawLines(pen, new[] { new PointF(3.5f, 18.5f), new PointF(3.5f, 5.5f), new PointF(9.5f, 5.5f), new PointF(11.5f, 8), new PointF(20.5f, 8), new PointF(20.5f, 18.5f), new PointF(3.5f, 18.5f) }); break;
                    case Glyph.Keyboard:
                        R(g, pen, 2.5f, 6, 19, 12, 2.5f);
                        for (int i = 0; i < 5; i++) g.FillEllipse(brush, 5.2f + i * 3.1f, 8.8f, 1.8f, 1.8f);
                        g.DrawLine(pen, 7.5f, 14.5f, 16.5f, 14.5f); break;
                    case Glyph.Coffee:
                        g.DrawLines(pen, new[] { new PointF(4.5f, 9), new PointF(5.5f, 18), new PointF(14.5f, 18), new PointF(15.5f, 9), new PointF(4.5f, 9) });
                        g.DrawArc(pen, 13.8f, 10, 6, 5.5f, -90, 180); g.DrawLine(pen, 8, 3.5f, 8, 6); g.DrawLine(pen, 12, 3.5f, 12, 6); break;
                    case Glyph.Target: g.DrawEllipse(pen, 3.5f, 3.5f, 17, 17); g.DrawEllipse(pen, 7.5f, 7.5f, 9, 9); g.FillEllipse(brush, 10.6f, 10.6f, 2.8f, 2.8f); break;
                    case Glyph.Widget: R(g, pen, 3, 5, 18, 14, 2.5f); using (var p = Ui.Round(new RectangleF(5.5f, 7.5f, 7.5f, 5), 1.2f)) g.FillPath(brush, p); break;
                    case Glyph.Undo:
                        g.DrawLines(pen, new[] { new PointF(8.5f, 5), new PointF(4.5f, 9), new PointF(8.5f, 13) });
                        g.DrawBezier(pen, 4.8f, 9, 12, 9, 19.5f, 9, 19.5f, 14.5f); g.DrawBezier(pen, 19.5f, 14.5f, 19.5f, 18, 16.5f, 19.5f, 13, 19.5f); break;
                    case Glyph.Bolt:
                        using (var p = new GraphicsPath()) { p.AddPolygon(new[] { new PointF(13.5f, 3), new PointF(5.5f, 13.5f), new PointF(11.5f, 13.5f), new PointF(10.5f, 21), new PointF(18.5f, 10.5f), new PointF(12.5f, 10.5f) }); g.DrawPath(pen, p); }
                        break;
                    case Glyph.Power:
                        g.DrawArc(pen, 4.5f, 5, 15, 15, -55, 290); g.DrawLine(pen, 12, 3, 12, 11.5f); break;
                    case Glyph.Logo:
                        // A clock face whose hand doubles as a check mark: "time, well spent".
                        g.DrawEllipse(pen, 3, 3, 18, 18); g.DrawLines(pen, new[] { new PointF(8.2f, 12.4f), new PointF(11, 15.2f), new PointF(16.2f, 9) }); break;
                }
            }
            g.Restore(state);
        }
        private static void R(Graphics g, Pen pen, float x, float y, float w, float h, float radius)
        {
            using (var p = Ui.Round(new RectangleF(x, y, w, h), radius)) g.DrawPath(pen, p);
        }
    }
}
