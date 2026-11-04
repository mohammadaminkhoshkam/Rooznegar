using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Rooznegar
{
    public enum Kind { Primary, Secondary, Ghost, Danger, Outline, Soft }

    /// <summary>
    /// Base for custom-painted UI. Subclasses paint in <see cref="Draw"/> and register clickable
    /// regions with <see cref="AddHit"/>; hover, cursor, tooltips and optional vertical scrolling are handled here.
    /// </summary>
    public class Surface : Control
    {
        protected sealed class Hit
        {
            public Rectangle R; public string Id; public Action Click; public Action DoubleClick; public string Tip;
        }
        private readonly List<Hit> hits = new List<Hit>();
        protected string HoverId;
        private string pressedId;
        protected Point Mouse = new Point(-1, -1);
        public bool Scrollable;
        public int ScrollY;
        public bool CanScroll { get { return Scrollable && ContentHeight > Height; } }
        protected int ContentHeight;
        private bool draggingBar;
        private int dragOffset;

        public Surface()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            RightToLeft = RightToLeft.Yes;
        }
        protected virtual Color Background { get { return Palette.Bg; } }
        protected virtual void Draw(Graphics g) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; hits.Clear();
            Ui.HighQuality(g); g.Clear(Background);
            // A painting bug must never leave a red cross or crash the app: log it and keep going.
            try { PaintBody(g); }
            catch (Exception error) { g.ResetTransform(); ErrorLog.Write(GetType().Name + ".Paint", error); }
        }
        private void PaintBody(Graphics g)
        {
            if (Scrollable)
            {
                ClampScroll();
                g.TranslateTransform(0, -ScrollY);
                Draw(g);
                g.ResetTransform();
                if (ContentHeight > Height)
                {
                    var bar = ScrollThumb();
                    bool hot = draggingBar || (Mouse.X >= 0 && Mouse.X < Ui.S(14));
                    Ui.Fill(g, new Rectangle(bar.X + (hot ? 0 : Ui.S(2)), bar.Y, hot ? bar.Width : bar.Width - Ui.S(3), bar.Height), Palette.Alpha(Palette.Ink3, hot ? 150 : 90), Ui.S(4));
                }
            }
            else Draw(g);
            DrawTip(g);
        }
        private Rectangle ScrollThumb()
        {
            int track = Height - Ui.S(8);
            int h = Math.Max(Ui.S(36), (int)(track * (double)Height / Math.Max(1, ContentHeight)));
            int y = Ui.S(4) + (int)((track - h) * (double)ScrollY / Math.Max(1, ContentHeight - Height));
            return new Rectangle(Ui.S(3), y, Ui.S(7), h);
        }
        protected void ClampScroll()
        {
            ScrollY = Math.Max(0, Math.Min(ScrollY, Math.Max(0, ContentHeight - Height)));
        }
        private void DrawTip(Graphics g)
        {
            var hit = Find(Mouse);
            if (hit == null || string.IsNullOrEmpty(hit.Tip)) return;
            var font = Fonts.Get(8.5f);
            var size = Ui.Measure(g, hit.Tip, font);
            int w = size.Width + Ui.S(20), h = size.Height + Ui.S(12);
            var r = hit.R; if (Scrollable) r.Offset(0, -ScrollY);
            int x = r.X + r.Width / 2 - w / 2, y = r.Y - h - Ui.S(6);
            if (y < Ui.S(2)) y = r.Bottom + Ui.S(6);
            x = Math.Max(Ui.S(4), Math.Min(Width - w - Ui.S(4), x));
            var box = new Rectangle(x, y, w, h);
            Ui.Fill(g, box, Palette.Dark ? Palette.Hex("#E7EFED") : Palette.Hex("#1B2B29"), Ui.S(7));
            Ui.Text(g, hit.Tip, font, Palette.Dark ? Palette.Hex("#152B28") : Color.White, box, Align.Center);
        }
        public void AddHit(Rectangle r, string id, Action click, string tip = null, Action doubleClick = null)
        {
            hits.Add(new Hit { R = r, Id = id, Click = click, Tip = tip, DoubleClick = doubleClick });
        }
        private Hit Find(Point p)
        {
            if (p.X < 0) return null;
            var q = Scrollable ? new Point(p.X, p.Y + ScrollY) : p;
            for (int i = hits.Count - 1; i >= 0; i--) if (hits[i].R.Contains(q)) return hits[i];
            return null;
        }
        public bool Hot(string id) { return HoverId == id; }
        public bool Down(string id) { return pressedId == id && HoverId == id; }
        protected Point ContentMouse { get { return Scrollable ? new Point(Mouse.X, Mouse.Y + ScrollY) : Mouse; } }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (draggingBar)
            {
                int track = Height - Ui.S(8), h = ScrollThumb().Height;
                ScrollY = (int)((e.Y - dragOffset - Ui.S(4)) * (double)(ContentHeight - Height) / Math.Max(1, track - h));
                ClampScroll(); Invalidate(); return;
            }
            Mouse = e.Location;
            var hit = Find(e.Location);
            string id = hit == null ? null : hit.Id;
            Cursor = hit != null && (hit.Click != null || hit.DoubleClick != null) ? Cursors.Hand : Cursors.Default;
            if (id != HoverId || (hit != null && hit.Tip != null) || WantsMouseRepaint) { HoverId = id; Invalidate(); }
        }
        /// <summary>Set by surfaces that draw cursor-following overlays (chart tooltips).</summary>
        protected virtual bool WantsMouseRepaint { get { return Scrollable && Mouse.X < Ui.S(16); } }
        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            Mouse = new Point(-1, -1); HoverId = null; pressedId = null; Invalidate();
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (Scrollable && ContentHeight > Height && e.X < Ui.S(14))
            {
                var thumb = ScrollThumb();
                if (thumb.Contains(e.Location)) { draggingBar = true; dragOffset = e.Y - thumb.Y; Capture = true; return; }
                ScrollY += e.Y < thumb.Y ? -Height + Ui.S(40) : Height - Ui.S(40); ClampScroll(); Invalidate(); return;
            }
            var hit = Find(e.Location); pressedId = hit == null ? null : hit.Id; Invalidate();
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (draggingBar) { draggingBar = false; Capture = false; Invalidate(); return; }
            if (e.Button != MouseButtons.Left) return;
            var hit = Find(e.Location);
            string pressed = pressedId; pressedId = null;
            if (hit != null && hit.Id == pressed && hit.Click != null) { Invalidate(); Run(hit.Click); return; }
            Invalidate();
        }
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            var hit = Find(e.Location);
            if (hit != null && hit.DoubleClick != null) Run(hit.DoubleClick);
        }
        /// <summary>
        /// Runs a click after the current mouse message finishes, so an action that replaces or closes this
        /// surface (navigation, theme switch, dialogs) never runs while the surface is still handling input.
        /// </summary>
        private void Run(Action action)
        {
            Action safe = () => { try { action(); } catch (Exception error) { ErrorLog.Write(GetType().Name + ".Click", error); Dialogs.Error(FindForm(), error); } };
            if (IsHandleCreated && !IsDisposed) BeginInvoke(safe); else safe();
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!Scrollable) return;
            ScrollY -= e.Delta / 120 * Ui.S(56); ClampScroll(); Invalidate();
        }
        /// <summary>Simulates the mouse at a point (used by the automated UI check).</summary>
        public void Probe(Point p) { if (p.X < 0) OnMouseLeave(EventArgs.Empty); else OnMouseMove(new MouseEventArgs(MouseButtons.None, 0, p.X, p.Y, 0)); }
        public void Wheel(int delta) { OnMouseWheel(new MouseEventArgs(MouseButtons.None, 0, Mouse.X, Mouse.Y, delta)); }

        // ---------- shared widgets ----------
        public void Button(Graphics g, Rectangle r, string text, Glyph? icon, Kind kind, string id, Action click, string tip = null, bool enabled = true)
        {
            bool hot = enabled && Hot(id), down = enabled && Down(id);
            Color fill, ink, border = Color.Empty;
            switch (kind)
            {
                case Kind.Primary: fill = hot ? Palette.AccentHover : Palette.Accent; ink = Palette.AccentInk; break;
                case Kind.Danger: fill = hot ? Palette.Mix(Palette.Danger, Color.Black, 0.12) : Palette.Danger; ink = Color.White; break;
                case Kind.Soft: fill = hot ? Palette.Mix(Palette.AccentSoft, Palette.Accent, 0.12) : Palette.AccentSoft; ink = Palette.Dark ? Palette.AccentHover : Palette.Accent; break;
                case Kind.Ghost: fill = hot ? Palette.Surface2 : Color.Transparent; ink = Palette.Ink2; break;
                case Kind.Outline: fill = hot ? Palette.Surface2 : Palette.Surface; ink = Palette.Ink; border = Palette.LineStrong; break;
                default: fill = hot ? Palette.Sunken : Palette.Surface2; ink = Palette.Ink; break;
            }
            if (!enabled) { fill = kind == Kind.Ghost ? Color.Transparent : Palette.Surface2; ink = Palette.Ink3; border = Color.Empty; }
            if (down) r.Offset(0, 1);
            int radius = Math.Min(Ui.S(10), r.Height / 2);
            if (fill.A > 0) Ui.Fill(g, r, fill, radius);
            if (!border.IsEmpty) Ui.Stroke(g, r, border, radius);
            var font = Fonts.Get(9.5f, Weight.Medium);
            if (string.IsNullOrEmpty(text) && icon.HasValue)
            {
                int s = Math.Min(Ui.S(20), r.Height - Ui.S(12));
                Icons.Draw(g, icon.Value, new RectangleF(r.X + (r.Width - s) / 2f, r.Y + (r.Height - s) / 2f, s, s), ink);
            }
            else
            {
                int tw = Ui.Measure(g, text, font).Width, iw = icon.HasValue ? Ui.S(18) + Ui.S(7) : 0;
                int start = r.X + (r.Width + tw + iw) / 2; // RTL: icon sits to the right of the text
                if (icon.HasValue) Icons.Draw(g, icon.Value, new RectangleF(start - Ui.S(18), r.Y + (r.Height - Ui.S(18)) / 2f, Ui.S(18), Ui.S(18)), ink);
                Ui.Text(g, text, font, ink, new Rectangle(start - iw - tw - Ui.S(2), r.Y, tw + Ui.S(4), r.Height), Align.Center);
            }
            AddHit(r, id, enabled ? click : null, tip);
        }
        public int ButtonWidth(Graphics g, string text, bool icon)
        {
            return Ui.Measure(g, text, Fonts.Get(9.5f, Weight.Medium)).Width + Ui.S(icon ? 50 : 32);
        }
        public void IconButton(Graphics g, Rectangle r, Glyph icon, string id, Action click, string tip, Color? color = null, bool round = false)
        {
            bool hot = Hot(id);
            if (hot) Ui.Fill(g, r, Palette.Surface2, round ? r.Height / 2 : Ui.S(8));
            int s = Math.Min(Ui.S(18), r.Height - Ui.S(10));
            Icons.Draw(g, icon, new RectangleF(r.X + (r.Width - s) / 2f, r.Y + (r.Height - s) / 2f, s, s), color ?? (hot ? Palette.Ink : Palette.Ink2));
            AddHit(r, id, click, tip);
        }
        public void Toggle(Graphics g, Rectangle r, bool on, string id, Action click)
        {
            var track = new Rectangle(r.X, r.Y + (r.Height - Ui.S(22)) / 2, Ui.S(40), Ui.S(22));
            Ui.Fill(g, track, on ? Palette.Accent : (Hot(id) ? Palette.Ink3 : Palette.LineStrong), track.Height / 2);
            int d = track.Height - Ui.S(6);
            // RTL switch: the knob sits on the left when on.
            int kx = on ? track.X + Ui.S(3) : track.Right - Ui.S(3) - d;
            Ui.Circle(g, new RectangleF(kx, track.Y + Ui.S(3), d, d), Color.White);
            AddHit(track, id, click);
        }
        public void Segmented(Graphics g, Rectangle r, string[] items, int selected, string id, Action<int> pick)
        {
            Ui.Fill(g, r, Palette.Sunken, Ui.S(10));
            var font = Fonts.Get(9f, Weight.Medium);
            int w = r.Width / items.Length;
            for (int i = 0; i < items.Length; i++)
            {
                // First item is right-most (RTL).
                var cell = new Rectangle(r.Right - (i + 1) * w, r.Y, w, r.Height);
                string hid = id + i; int index = i;
                var inner = Rectangle.Inflate(cell, -Ui.S(3), -Ui.S(3));
                if (i == selected) { Ui.Fill(g, inner, Palette.Surface, Ui.S(8)); if (!Palette.Dark) Ui.Stroke(g, inner, Palette.Line, Ui.S(8)); }
                else if (Hot(hid)) Ui.Fill(g, inner, Palette.Alpha(Palette.Surface, 120), Ui.S(8));
                Ui.Text(g, items[i], font, i == selected ? Palette.Ink : Palette.Ink2, cell, Align.Center);
                AddHit(cell, hid, () => pick(index));
            }
        }
        public void Stepper(Graphics g, Rectangle r, string value, string id, Action minus, Action plus)
        {
            Ui.Fill(g, r, Palette.Sunken, Ui.S(10));
            int b = r.Height;
            var right = new Rectangle(r.Right - b, r.Y, b, b); var left = new Rectangle(r.X, r.Y, b, b);
            IconButton(g, Rectangle.Inflate(right, -Ui.S(4), -Ui.S(4)), Glyph.Plus, id + "+", plus, null);
            var minusRect = Rectangle.Inflate(left, -Ui.S(4), -Ui.S(4));
            if (Hot(id + "-")) Ui.Fill(g, minusRect, Palette.Surface2, Ui.S(8));
            AddHit(minusRect, id + "-", minus);
            using (var pen = new Pen(Hot(id + "-") ? Palette.Ink : Palette.Ink2, 1.8f * Ui.Scale) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(pen, left.X + b / 2 - Ui.S(6), left.Y + b / 2, left.X + b / 2 + Ui.S(6), left.Y + b / 2);
            Ui.Text(g, value, Fonts.Get(10f, Weight.Medium), Palette.Ink, new Rectangle(r.X + b, r.Y, r.Width - 2 * b, r.Height), Align.Center);
        }
        public void Badge(Graphics g, Rectangle r, string text, Color fill, Color ink)
        {
            Ui.Fill(g, r, fill, r.Height / 2);
            Ui.Text(g, text, Fonts.Get(8f, Weight.Medium), ink, r, Align.Center);
        }
        public int BadgeWidth(Graphics g, string text) { return Ui.Measure(g, text, Fonts.Get(8f, Weight.Medium)).Width + Ui.S(18); }
        public void ProgressBar(Graphics g, Rectangle r, double value, Color color)
        {
            Ui.Fill(g, r, Palette.Sunken, r.Height / 2);
            int w = (int)(r.Width * Math.Max(0, Math.Min(1, value)));
            if (w > 0) Ui.Fill(g, new Rectangle(r.Right - Math.Max(w, r.Height), r.Y, Math.Max(w, r.Height), r.Height), color, r.Height / 2);
        }
        public void Ring(Graphics g, Rectangle r, double value, Color color, float thickness)
        {
            using (var track = new Pen(Palette.Sunken, thickness))
                g.DrawEllipse(track, r.X + thickness / 2, r.Y + thickness / 2, r.Width - thickness, r.Height - thickness);
            double v = Math.Max(0, Math.Min(1, value));
            if (v > 0)
                using (var pen = new Pen(color, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(pen, r.X + thickness / 2, r.Y + thickness / 2, r.Width - thickness, r.Height - thickness, -90, -(float)(359.9 * v));
        }
        public void Swatch(Graphics g, Rectangle r, string color)
        {
            Ui.Fill(g, r, Palette.Project(color), Math.Min(r.Width, r.Height) / 2);
        }
    }

    /// <summary>A surface whose content is painted by a delegate; handy for small one-off widgets in dialogs.</summary>
    public sealed class Canvas : Surface
    {
        public Action<Canvas, Graphics> Painter;
        public Func<Color> Back;
        protected override Color Background { get { return Back != null ? Back() : Palette.Surface; } }
        protected override void Draw(Graphics g) { if (Painter != null) Painter(this, g); }
        public void SetContentHeight(int h) { ContentHeight = h; }
    }

    /// <summary>A real, focusable button (Enter/Esc work via AcceptButton/CancelButton) painted like the rest of the UI.</summary>
    public sealed class Btn : Control, IButtonControl
    {
        public Kind Kind;
        public Glyph? Icon;
        /// <summary>Colour behind the rounded corners (defaults to the parent's background).</summary>
        public Color? Under;
        /// <summary>Optional text/icon colour, e.g. red for a destructive ghost button.</summary>
        public Func<Color> Ink;
        private bool hover, down, isDefault;
        public DialogResult DialogResult { get; set; }
        public Btn(string text, Kind kind = Kind.Secondary, Glyph? icon = null)
        {
            Text = text; Kind = kind; Icon = icon; Cursor = Cursors.Hand; TabStop = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Size = new Size(Ui.S(120), Ui.S(40)); RightToLeft = RightToLeft.Yes;
        }
        public void NotifyDefault(bool value) { isDefault = value; Invalidate(); }
        public void PerformClick() { if (Enabled && Visible) OnClick(EventArgs.Empty); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            var form = FindForm();
            if (form != null && DialogResult != DialogResult.None) form.DialogResult = DialogResult;
        }
        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Space) PerformClick();
        }
        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Space || base.IsInputKey(keyData); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Ui.HighQuality(g);
            g.Clear(Under ?? (Parent != null ? Parent.BackColor : Palette.Bg));
            Color fill, ink, border = Color.Empty;
            switch (Kind)
            {
                case Kind.Primary: fill = hover ? Palette.AccentHover : Palette.Accent; ink = Palette.AccentInk; break;
                case Kind.Danger: fill = hover ? Palette.Mix(Palette.Danger, Color.Black, 0.12) : Palette.Danger; ink = Color.White; break;
                case Kind.Ghost: fill = hover ? Palette.Surface2 : Color.Transparent; ink = Palette.Ink2; break;
                case Kind.Outline: fill = hover ? Palette.Surface2 : Palette.Surface; ink = Palette.Ink; border = Palette.LineStrong; break;
                default: fill = hover ? Palette.Sunken : Palette.Surface2; ink = Palette.Ink; break;
            }
            if (Ink != null) ink = Ink();
            if (!Enabled) { fill = Palette.Surface2; ink = Palette.Ink3; }
            var r = new Rectangle(0, down ? 1 : 0, Width - 1, Height - 1);
            int radius = Math.Min(Ui.S(10), Height / 2);
            if (fill.A > 0) Ui.Fill(g, r, fill, radius);
            if (!border.IsEmpty) Ui.Stroke(g, r, border, radius);
            if (Focused && ShowFocusCues) Ui.Stroke(g, Rectangle.Inflate(r, -Ui.S(2), -Ui.S(2)), Palette.Alpha(Kind == Kind.Primary || Kind == Kind.Danger ? Color.White : Palette.Accent, 200), radius, 1.6f * Ui.Scale);
            var font = Fonts.Get(9.5f, Weight.Medium);
            int tw = Ui.Measure(g, Text, font).Width, iw = Icon.HasValue ? Ui.S(25) : 0;
            int start = (Width + tw + iw) / 2;
            if (Icon.HasValue) Icons.Draw(g, Icon.Value, new RectangleF(start - Ui.S(18), (Height - Ui.S(18)) / 2f + r.Y, Ui.S(18), Ui.S(18)), ink);
            Ui.Text(g, Text, font, ink, new Rectangle(start - iw - tw - Ui.S(2), r.Y, tw + Ui.S(4), Height), Align.Center);
        }
        public Btn Fit(int min = 0)
        {
            using (var g = CreateGraphics()) Width = Math.Max(min, Ui.Measure(g, Text, Fonts.Get(9.5f, Weight.Medium)).Width + Ui.S(Icon.HasValue ? 54 : 36));
            return this;
        }
    }

    /// <summary>A borderless TextBox inside a rounded, theme-aware frame with a placeholder.</summary>
    public sealed class InputBox : Control
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
        public readonly TextBox Box;
        public Glyph? Icon;
        private string placeholder = "";
        public InputBox(bool multiline = false)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Box = new TextBox { BorderStyle = BorderStyle.None, Multiline = multiline, RightToLeft = RightToLeft.Yes, Font = Fonts.Get(10.5f), BackColor = Palette.Surface, ForeColor = Palette.Ink };
            if (multiline) { Box.ScrollBars = ScrollBars.None; Box.AcceptsReturn = true; }
            Controls.Add(Box);
            Box.GotFocus += (s, e) => Invalidate(); Box.LostFocus += (s, e) => Invalidate();
            Box.HandleCreated += (s, e) => ApplyPlaceholder();
            Size = new Size(Ui.S(240), Ui.S(42)); RightToLeft = RightToLeft.Yes; Cursor = Cursors.IBeam;
        }
        public string Placeholder { get { return placeholder; } set { placeholder = value ?? ""; ApplyPlaceholder(); } }
        private void ApplyPlaceholder()
        {
            if (!Ui.IsMono && Box.IsHandleCreated && !Box.Multiline) try { SendMessage(Box.Handle, 0x1501, (IntPtr)1, placeholder); } catch { }
        }
        public override string Text { get { return Box.Text; } set { Box.Text = value; } }
        protected override void OnClick(EventArgs e) { base.OnClick(e); Box.Focus(); }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int pad = Ui.S(12), iconW = Icon.HasValue ? Ui.S(28) : 0;
            if (Box.Multiline) Box.Bounds = new Rectangle(pad, Ui.S(9), Width - pad * 2, Height - Ui.S(18));
            else
            {
                int h = Box.PreferredHeight;
                Box.Bounds = new Rectangle(pad, (Height - h) / 2, Width - pad * 2 - iconW, h);
            }
        }
        public void Retheme() { Box.BackColor = Palette.Surface; Box.ForeColor = Palette.Ink; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Ui.HighQuality(g);
            g.Clear(Parent != null ? Parent.BackColor : Palette.Bg);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Ui.Fill(g, r, Palette.Surface, Ui.S(10));
            bool focus = Box.Focused;
            Ui.Stroke(g, r, focus ? Palette.Accent : Palette.LineStrong, Ui.S(10), focus ? 1.6f * Ui.Scale : 1f);
            if (Icon.HasValue) Icons.Draw(g, Icon.Value, new RectangleF(Width - Ui.S(34), (Height - Ui.S(18)) / 2f, Ui.S(18), Ui.S(18)), Palette.Ink3);
            if (Ui.IsMono && Box.Text.Length == 0 && !focus && placeholder.Length > 0)
                Ui.Text(g, placeholder, Fonts.Get(10f), Palette.Ink3, new Rectangle(Ui.S(12), 0, Width - Ui.S(24) - (Icon.HasValue ? Ui.S(28) : 0), Height));
        }
    }

    /// <summary>Transient message at the bottom of the window, optionally with an action such as «بازگردانی».</summary>
    public sealed class Toast : Surface
    {
        private string message = "", action;
        private Action onAction;
        private readonly Timer timer = new Timer();
        public Toast()
        {
            Visible = false; timer.Tick += (s, e) => { timer.Stop(); Visible = false; };
        }
        protected override Color Background { get { return Parent != null ? Parent.BackColor : Palette.Bg; } }
        public void Show(string text, string actionText = null, Action act = null, int seconds = 5)
        {
            message = text; action = actionText; onAction = act;
            using (var g = CreateGraphics())
            {
                int w = Ui.Measure(g, message, Fonts.Get(9.5f)).Width + Ui.S(64) + (action == null ? 0 : Ui.Measure(g, action, Fonts.Get(9.5f, Weight.Bold)).Width + Ui.S(36));
                Size = new Size(Math.Min(Parent == null ? 600 : Parent.Width - Ui.S(40), w), Ui.S(48));
            }
            if (Parent != null) Location = new Point((Parent.ClientSize.Width - Width) / 2, Parent.ClientSize.Height - Height - Ui.S(22));
            Visible = true; BringToFront(); Invalidate();
            timer.Stop(); timer.Interval = seconds * 1000; timer.Start();
        }
        protected override void Draw(Graphics g)
        {
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Ui.Fill(g, r, Palette.Dark ? Palette.Hex("#E7EFED") : Palette.Hex("#1B2B29"), Ui.S(12));
            Color ink = Palette.Dark ? Palette.Hex("#152B28") : Color.White;
            Icons.Draw(g, Glyph.Check, new RectangleF(Width - Ui.S(38), (Height - Ui.S(18)) / 2f, Ui.S(18), Ui.S(18)), Palette.Dark ? Palette.Accent : Palette.Hex("#5FD3C2"));
            int actionW = 0;
            if (action != null)
            {
                actionW = Ui.Measure(g, action, Fonts.Get(9.5f, Weight.Bold)).Width + Ui.S(24);
                var ar = new Rectangle(Ui.S(8), Ui.S(8), actionW, Height - Ui.S(16));
                if (Hot("act")) Ui.Fill(g, ar, Palette.Alpha(ink, 30), Ui.S(8));
                Ui.Text(g, action, Fonts.Get(9.5f, Weight.Bold), Palette.Dark ? Palette.Accent : Palette.Hex("#5FD3C2"), ar, Align.Center);
                AddHit(ar, "act", () => { Visible = false; timer.Stop(); if (onAction != null) onAction(); });
            }
            Ui.Text(g, message, Fonts.Get(9.5f), ink, new Rectangle(Ui.S(16) + actionW, 0, Width - Ui.S(60) - actionW, Height));
        }
        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }
}
