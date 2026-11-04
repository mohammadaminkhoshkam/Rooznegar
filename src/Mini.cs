using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Rooznegar
{
    /// <summary>Small always-on-top timer that can be dragged anywhere; double-click opens the main window.</summary>
    public sealed class MiniWidget : Form
    {
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        private readonly MainForm app;
        private readonly Canvas canvas;
        private Point dragFrom; private bool dragging;
        public MiniWidget(MainForm owner)
        {
            app = owner;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None; Size = new Size(Ui.S(300), Ui.S(58)); BackColor = Palette.Bg;
            canvas = new Canvas { Dock = DockStyle.Fill, Back = () => Palette.Dark ? Palette.Bg : Palette.Bg };
            canvas.Painter = PaintWidget;
            canvas.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left || e.X < Ui.S(52)) return;
                if (!Ui.IsMono) { ReleaseCapture(); SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); app.SaveMiniPosition(Location); }
                else { dragging = true; dragFrom = e.Location; }
            };
            canvas.MouseMove += (s, e) => { if (dragging) Location = new Point(Location.X + e.X - dragFrom.X, Location.Y + e.Y - dragFrom.Y); };
            canvas.MouseUp += (s, e) => { if (dragging) { dragging = false; app.SaveMiniPosition(Location); } };
            canvas.DoubleClick += (s, e) => app.ShowFromTray();
            Controls.Add(canvas);
            Region = new Region(Ui.Round(new RectangleF(0, 0, Width, Height), Ui.S(14)));
        }
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80 | 0x08000000; return cp; } // tool window, no activate
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        public void Place()
        {
            var s = app.State.Settings; var area = Screen.PrimaryScreen.WorkingArea;
            var p = new Point(s.MiniX, s.MiniY);
            bool visible = Screen.AllScreens.Any(sc => sc.WorkingArea.Contains(new Rectangle(p, new Size(Ui.S(40), Ui.S(30)))));
            Location = s.MiniX >= 0 && visible ? p : new Point(area.Right - Width - Ui.S(24), area.Bottom - Height - Ui.S(24));
        }
        private void PaintWidget(Canvas c, Graphics g)
        {
            var st = app.State; var a = st.Active;
            var p = a == null ? null : st.Projects.FirstOrDefault(x => x.Id == a.ProjectId);
            Color pc = p == null ? Palette.Ink3 : Palette.Project(p.Color);
            var r = new Rectangle(0, 0, c.Width - 1, c.Height - 1);
            Ui.Fill(g, r, Palette.Surface, Ui.S(14));
            Ui.Stroke(g, r, p == null ? Palette.LineStrong : Palette.Alpha(pc, 160), Ui.S(14), 1.5f * Ui.Scale);
            Ui.Fill(g, new Rectangle(c.Width - Ui.S(8), Ui.S(12), Ui.S(4), c.Height - Ui.S(24)), pc, Ui.S(2));
            var btn = new Rectangle(Ui.S(10), (c.Height - Ui.S(38)) / 2, Ui.S(38), Ui.S(38));
            bool hot = c.Hot("b");
            Ui.Fill(g, btn, a != null ? (hot ? Palette.Mix(Palette.Danger, Color.Black, 0.1) : Palette.Danger) : (hot ? Palette.AccentHover : Palette.Accent), btn.Height / 2);
            Icons.Draw(g, a != null ? Glyph.Stop : Glyph.Play, Rectangle.Inflate(btn, -Ui.S(10), -Ui.S(10)), a != null ? Color.White : Palette.AccentInk);
            c.AddHit(btn, "b", app.ToggleTimer, a != null ? "توقف" : "ادامهٔ آخرین پروژه");
            string name = p != null ? p.Name : "تایمر خاموش است";
            Ui.Text(g, name, Fonts.Get(9f, Weight.Medium), p != null ? Palette.Ink2 : Palette.Ink3, new Rectangle(btn.Right + Ui.S(110), 0, c.Width - btn.Right - Ui.S(130), c.Height));
            Ui.Text(g, a != null ? Dates.Duration(app.Snap.Now - a.StartUtc) : Dates.Hm(app.Snap.TodayTotal), Fonts.Get(13f, Weight.Bold), a != null ? pc : Palette.Ink2, new Rectangle(btn.Right + Ui.S(8), 0, Ui.S(104), c.Height), Align.Left, false, true);
        }
    }
}
