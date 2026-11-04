using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace Rooznegar
{
    /// <summary>Everything a page needs to paint one frame, computed once per tick.</summary>
    public sealed class Snapshot
    {
        public long Now;
        public DateTime Today, WeekStart, MonthStart;
        public List<TimeSlice> Slices;
        public Dictionary<DateTime, long> Days;
        public readonly Dictionary<string, long> TodayBy = new Dictionary<string, long>(), WeekBy = new Dictionary<string, long>(), TotalBy = new Dictionary<string, long>();
        public long TodayTotal, WeekTotal, MonthTotal, Total, Yesterday;
        public int Streak, MonthActiveDays;
        public static long Get(Dictionary<string, long> d, string id) { long v; return id != null && d.TryGetValue(id, out v) ? v : 0; }
        public static Snapshot Build(AppState s, long now, TimeZoneInfo zone)
        {
            var snap = new Snapshot { Now = now, Today = Dates.Local(now, zone).Date };
            snap.WeekStart = Dates.WeekStart(snap.Today); snap.MonthStart = Dates.MonthStart(snap.Today);
            snap.Slices = Engine.Slices(s, now, zone).ToList();
            snap.Days = Engine.DayTotals(snap.Slices);
            foreach (var t in snap.Slices)
            {
                long d = t.Duration;
                Add(snap.TotalBy, t.ProjectId, d); snap.Total += d;
                if (t.LocalDay == snap.Today) { Add(snap.TodayBy, t.ProjectId, d); snap.TodayTotal += d; }
                if (t.LocalDay >= snap.WeekStart && t.LocalDay <= snap.Today) { Add(snap.WeekBy, t.ProjectId, d); snap.WeekTotal += d; }
                if (t.LocalDay >= snap.MonthStart && t.LocalDay <= snap.Today) snap.MonthTotal += d;
                if (t.LocalDay == snap.Today.AddDays(-1)) snap.Yesterday += d;
            }
            snap.MonthActiveDays = snap.Days.Count(kv => kv.Key >= snap.MonthStart && kv.Key <= snap.Today && kv.Value > 0);
            snap.Streak = Engine.Streak(snap.Days, s.Settings.DailyGoalMinutes * TimeSpan.TicksPerMinute, snap.Today);
            return snap;
        }
        private static void Add(Dictionary<string, long> d, string k, long v) { long o; d.TryGetValue(k, out o); d[k] = o + v; }
        public long Day(DateTime day) { long v; return Days.TryGetValue(day.Date, out v) ? v : 0; }
    }

    /// <summary>Common page chrome: title, subtitle and the 28px gutter.</summary>
    public abstract class Page : Surface
    {
        protected readonly MainForm App;
        protected Page(MainForm app) { App = app; Dock = DockStyle.Fill; }
        protected int Pad { get { return Ui.S(28); } }
        protected Snapshot Snap { get { return App.Snap; } }
        protected AppState State { get { return App.State; } }
        protected void Header(Graphics g, string title, string subtitle)
        {
            Ui.Text(g, title, Fonts.Get(18f, Weight.Bold), Palette.Ink, new Rectangle(Width / 2, Ui.S(18), Width / 2 - Pad, Ui.S(38)));
            Ui.Text(g, subtitle, Fonts.Get(9.5f), Palette.Ink2, new Rectangle(Width / 3, Ui.S(56), Width * 2 / 3 - Pad, Ui.S(24)));
        }
        protected Project ProjectOf(string id) { return State.Projects.FirstOrDefault(p => p.Id == id); }
        protected void Tile(Graphics g, Rectangle r, Glyph icon, string label, string value, string note, Color tint)
        {
            Ui.Card(g, r);
            int ic = Ui.S(38);
            var circle = new Rectangle(r.Right - Ui.S(16) - ic, r.Y + (r.Height - ic) / 2, ic, ic);
            Ui.Fill(g, circle, Palette.Alpha(tint, Palette.Dark ? 46 : 28), ic / 2);
            Icons.Draw(g, icon, Rectangle.Inflate(circle, -Ui.S(9), -Ui.S(9)), tint);
            int tx = r.X + Ui.S(14), tw = circle.X - Ui.S(12) - tx;
            Ui.Text(g, label, Fonts.Get(8.5f), Palette.Ink2, new Rectangle(tx, r.Y + Ui.S(12), tw, Ui.S(20)));
            Ui.Text(g, value, Fonts.Get(15f, Weight.Bold), Palette.Ink, new Rectangle(tx, r.Y + Ui.S(31), tw, Ui.S(30)), Align.Right, false, true);
            Ui.Text(g, note, Fonts.Get(8f), Palette.Ink3, new Rectangle(tx, r.Bottom - Ui.S(26), tw, Ui.S(18)));
        }
        protected void Empty(Graphics g, Rectangle r, Glyph icon, string title, string text)
        {
            int s = Ui.S(52);
            var c = new Rectangle(r.X + (r.Width - s) / 2, r.Y + r.Height / 2 - Ui.S(64), s, s);
            Ui.Fill(g, c, Palette.Surface2, s / 2);
            Icons.Draw(g, icon, Rectangle.Inflate(c, -Ui.S(14), -Ui.S(14)), Palette.Ink3);
            Ui.Text(g, title, Fonts.Get(11f, Weight.Medium), Palette.Ink, new Rectangle(r.X, c.Bottom + Ui.S(10), r.Width, Ui.S(26)), Align.Center);
            Ui.Text(g, text, Fonts.Get(9f), Palette.Ink3, new Rectangle(r.X, c.Bottom + Ui.S(36), r.Width, Ui.S(22)), Align.Center);
        }
        /// <summary>Repaints the page and its custom surfaces, but not native inputs (avoids caret flicker).</summary>
        public virtual void Tick()
        {
            Invalidate(false);
            foreach (Control c in Controls) if (c is Surface) c.Invalidate();
        }
    }

    // ===================================================================== Sidebar
    public sealed class Sidebar : Surface
    {
        private readonly MainForm app;
        public Sidebar(MainForm owner) { app = owner; Dock = DockStyle.Right; Width = Ui.S(236); }
        protected override Color Background { get { return Palette.Sidebar; } }
        private static readonly string[] keys = { "dashboard", "timesheet", "reports", "settings" };
        private static readonly string[] names = { "نمای کلی", "تایم‌شیت", "گزارش‌ها", "تنظیمات" };
        private static readonly Glyph[] glyphs = { Glyph.Dashboard, Glyph.Timesheet, Glyph.Reports, Glyph.Settings };
        protected override void Draw(Graphics g)
        {
            using (var p = new Pen(Palette.Line)) g.DrawLine(p, 0, 0, 0, Height);
            int m = Ui.S(18);
            var logo = new Rectangle(Width - m - Ui.S(42), Ui.S(26), Ui.S(42), Ui.S(42));
            using (var brush = new LinearGradientBrush(logo, Palette.Mix(Palette.Accent, Color.White, 0.12), Palette.Mix(Palette.Accent, Color.Black, 0.18), 45f))
            using (var path = Ui.Round(logo, Ui.S(12))) g.FillPath(brush, path);
            Icons.Draw(g, Glyph.Logo, Rectangle.Inflate(logo, -Ui.S(9), -Ui.S(9)), Color.White, 2.2f);
            Ui.Text(g, "روزنگار", Fonts.Get(16f, Weight.Black), Palette.Ink, new Rectangle(m, Ui.S(22), logo.X - Ui.S(12) - m, Ui.S(30)));
            Ui.Text(g, "ثبت زمان پروژه‌ها", Fonts.Get(8.5f), Palette.Ink3, new Rectangle(m, Ui.S(50), logo.X - Ui.S(12) - m, Ui.S(20)));

            int y = Ui.S(104);
            Ui.Text(g, "منو", Fonts.Get(8f, Weight.Medium), Palette.Ink3, new Rectangle(m, y, Width - 2 * m - Ui.S(6), Ui.S(20)));
            y += Ui.S(26);
            for (int i = 0; i < keys.Length; i++)
            {
                var r = new Rectangle(m - Ui.S(6), y, Width - 2 * m + Ui.S(12), Ui.S(44));
                bool sel = app.PageKey == keys[i]; string id = "nav" + i, key = keys[i];
                if (sel) Ui.Fill(g, r, Palette.AccentSoft, Ui.S(10)); else if (Hot(id)) Ui.Fill(g, r, Palette.Surface2, Ui.S(10));
                if (sel) Ui.Fill(g, new Rectangle(r.Right - Ui.S(3), r.Y + Ui.S(11), Ui.S(3), r.Height - Ui.S(22)), Palette.Accent, Ui.S(2));
                Color ink = sel ? (Palette.Dark ? Palette.AccentHover : Palette.Accent) : Palette.Ink2;
                Icons.Draw(g, glyphs[i], new RectangleF(r.Right - Ui.S(38), r.Y + Ui.S(12), Ui.S(20), Ui.S(20)), ink);
                Ui.Text(g, names[i], Fonts.Get(10f, sel ? Weight.Bold : Weight.Medium), sel ? Palette.Ink : Palette.Ink2, new Rectangle(r.X + Ui.S(48), r.Y, r.Width - Ui.S(96), r.Height));
                if (Hot(id) || sel) Ui.Text(g, "Ctrl+" + (i + 1), Fonts.Get(7.5f), Palette.Ink3, new Rectangle(r.X + Ui.S(10), r.Y, Ui.S(44), r.Height), Align.Left, false, false);
                AddHit(r, id, () => app.Navigate(key));
                y += Ui.S(48);
            }

            // Now-tracking card
            var snap = app.Snap; var st = app.State;
            var card = new Rectangle(m - Ui.S(6), Height - Ui.S(186), Width - 2 * m + Ui.S(12), Ui.S(112));
            if (st.Active != null)
            {
                var p = st.Projects.FirstOrDefault(a => a.Id == st.Active.ProjectId);
                Color pc = p == null ? Palette.Accent : Palette.Project(p.Color);
                Ui.Card(g, card, Palette.Mix(Palette.Surface, pc, Palette.Dark ? 0.14 : 0.07), Palette.Alpha(pc, 90));
                bool blink = (snap.Now / TimeSpan.TicksPerSecond) % 2 == 0;
                Ui.Circle(g, new RectangleF(card.Right - Ui.S(22), card.Y + Ui.S(19), Ui.S(8), Ui.S(8)), blink ? pc : Palette.Alpha(pc, 110));
                Ui.Text(g, "در حال ثبت", Fonts.Get(8f, Weight.Medium), Palette.Ink2, new Rectangle(card.X + Ui.S(12), card.Y + Ui.S(12), card.Width - Ui.S(42), Ui.S(22)));
                Ui.Text(g, p == null ? "" : p.Name, Fonts.Get(10f, Weight.Bold), Palette.Ink, new Rectangle(card.X + Ui.S(12), card.Y + Ui.S(36), card.Width - Ui.S(26), Ui.S(26)));
                Ui.Text(g, Dates.Duration(snap.Now - st.Active.StartUtc), Fonts.Get(14f, Weight.Bold), pc, new Rectangle(card.X + Ui.S(56), card.Y + Ui.S(66), card.Width - Ui.S(70), Ui.S(34)), Align.Right, false, true);
                var stop = new Rectangle(card.X + Ui.S(12), card.Y + Ui.S(66), Ui.S(36), Ui.S(36));
                Ui.Fill(g, stop, Hot("stop") ? Palette.Mix(Palette.Danger, Color.Black, 0.12) : Palette.Danger, Ui.S(18));
                Icons.Draw(g, Glyph.Stop, Rectangle.Inflate(stop, -Ui.S(9), -Ui.S(9)), Color.White);
                AddHit(stop, "stop", app.Stop, "توقف (Ctrl+Alt+Space)");
            }
            else
            {
                Ui.Card(g, card, Palette.Surface);
                Ui.Text(g, "تایمری روشن نیست", Fonts.Get(9.5f, Weight.Medium), Palette.Ink2, new Rectangle(card.X + Ui.S(12), card.Y + Ui.S(14), card.Width - Ui.S(24), Ui.S(24)));
                var last = ProjectOfLast();
                if (last != null)
                {
                    Ui.Text(g, "ادامهٔ «" + last.Name + "»", Fonts.Get(8.5f), Palette.Ink3, new Rectangle(card.X + Ui.S(12), card.Y + Ui.S(40), card.Width - Ui.S(24), Ui.S(22)));
                    Button(g, new Rectangle(card.X + Ui.S(12), card.Y + Ui.S(66), card.Width - Ui.S(24), Ui.S(36)), "شروع دوباره", Glyph.Play, Kind.Soft, "resume", app.ResumeLast, "Ctrl+Alt+Space");
                }
                else Ui.Text(g, "یک پروژه را از نمای کلی شروع کنید.", Fonts.Get(8.5f), Palette.Ink3, new Rectangle(card.X + Ui.S(12), card.Y + Ui.S(40), card.Width - Ui.S(24), Ui.S(22)));
            }

            // Theme + widget controls
            int by = Height - Ui.S(58);
            var seg = new Rectangle(m - Ui.S(6), by, Ui.S(92), Ui.S(36));
            Ui.Fill(g, seg, Palette.Sunken, Ui.S(10));
            var sun = new Rectangle(seg.Right - Ui.S(45), seg.Y + Ui.S(3), Ui.S(42), Ui.S(30)); var moon = new Rectangle(seg.X + Ui.S(3), seg.Y + Ui.S(3), Ui.S(42), Ui.S(30));
            Ui.Fill(g, Palette.Dark ? moon : sun, Palette.Surface, Ui.S(8));
            Icons.Draw(g, Glyph.Sun, Rectangle.Inflate(sun, -Ui.S(12), -Ui.S(6)), Palette.Dark ? Palette.Ink3 : Palette.Warning);
            Icons.Draw(g, Glyph.Moon, Rectangle.Inflate(moon, -Ui.S(12), -Ui.S(6)), Palette.Dark ? Palette.AccentHover : Palette.Ink3);
            AddHit(sun, "sun", () => app.SetTheme("light"), "پوستهٔ روشن");
            AddHit(moon, "moon", () => app.SetTheme("dark"), "پوستهٔ تیره (Ctrl+T)");
            IconButton(g, new Rectangle(seg.Right + Ui.S(8), by, Ui.S(36), Ui.S(36)), Glyph.Widget, "mini", app.ToggleMini, st.Settings.MiniWidget ? "بستن تایمر شناور" : "تایمر شناور", st.Settings.MiniWidget ? Palette.Accent : (Color?)null);
            Ui.Text(g, "۲٫۰٫۰", Fonts.Get(8f), Palette.Ink3, new Rectangle(Width - m - Ui.S(60), by, Ui.S(60), Ui.S(36)));
        }
        private Project ProjectOfLast()
        {
            string id = Engine.LastProjectId(app.State);
            return app.State.Projects.FirstOrDefault(p => p.Id == id && !p.Archived);
        }
    }

    // ===================================================================== Dashboard
    public sealed class DashboardPage : Page
    {
        private readonly ProjectBoard board;
        private readonly InputBox search;
        public DashboardPage(MainForm app) : base(app)
        {
            board = new ProjectBoard(app); Controls.Add(board);
            search = new InputBox { Icon = Glyph.Search, Placeholder = "جست‌وجوی پروژه…" };
            search.Text = app.Search;
            search.Box.TextChanged += (s, e) => { App.Search = search.Text.Trim(); board.ScrollY = 0; board.Invalidate(); };
            Controls.Add(search);
        }
        public void FocusSearch() { search.Box.Focus(); search.Box.SelectAll(); }
        private int heroH { get { return Ui.S(204); } }
        private int boardTop { get { return Ui.S(92) + heroH + Ui.S(16) + Ui.S(88) + Ui.S(68); } }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (board == null || search == null) return;
            int top = boardTop;
            board.Bounds = new Rectangle(Pad - Ui.S(14), top, Width - 2 * Pad + Ui.S(14), Math.Max(Ui.S(60), Height - top - Ui.S(12)));
            search.Bounds = new Rectangle(Pad + Ui.S(150), top - Ui.S(52), Ui.S(220), Ui.S(38));
        }
        protected override void Draw(Graphics g)
        {
            var now = DateTime.Now;
            Header(g, Greeting(now), Dates.Long(now) + "   •   " + Dates.Digits(now.ToString("HH:mm", CultureInfo.InvariantCulture)));
            Button(g, new Rectangle(Pad, Ui.S(24), ButtonWidth(g, "ثبت دستی", true), Ui.S(40)), "ثبت دستی", Glyph.Plus, Kind.Outline, "manual", () => App.EditSession(null), "Ctrl+M");

            int top = Ui.S(92), gap = Ui.S(16), inner = Width - 2 * Pad;
            int heroW = (int)((inner - gap) * 0.64);
            var hero = new Rectangle(Width - Pad - heroW, top, heroW, heroH);
            var goal = new Rectangle(Pad, top, inner - gap - heroW, heroH);
            Hero(g, hero); Goal(g, goal);

            int ty = top + heroH + gap, tw = (inner - 3 * gap) / 4, th = Ui.S(88);
            var tiles = new[] {
                new Rectangle(Width - Pad - tw, ty, tw, th), new Rectangle(Width - Pad - 2 * tw - gap, ty, tw, th),
                new Rectangle(Width - Pad - 3 * tw - 2 * gap, ty, tw, th), new Rectangle(Pad, ty, inner - 3 * tw - 3 * gap, th) };
            Tile(g, tiles[0], Glyph.Clock, "امروز", Dates.Duration(Snap.TodayTotal), "دیروز: " + Dates.Hm(Snap.Yesterday), Palette.Accent);
            int weekDays = (int)(Snap.Today - Snap.WeekStart).TotalDays + 1;
            Tile(g, tiles[1], Glyph.Calendar, "این هفته", Dates.Duration(Snap.WeekTotal), "میانگین روزانه: " + Dates.Hm(Snap.WeekTotal / Math.Max(1, weekDays)), Palette.Hex("#3B6FD8"));
            Tile(g, tiles[2], Glyph.Reports, "این ماه (" + Dates.Months[Dates.Persian.GetMonth(Snap.Today) - 1] + ")", Dates.Duration(Snap.MonthTotal), Dates.Digits(Snap.MonthActiveDays.ToString()) + " روز فعال", Palette.Hex("#9A55B4"));
            Tile(g, tiles[3], Glyph.Target, "کل زمان ثبت‌شده", Dates.Duration(Snap.Total), Dates.Digits(State.Projects.Count(p => !p.Archived).ToString()) + " پروژهٔ جاری", Palette.Hex("#E07B39"));

            int py = boardTop - Ui.S(54);
            int count = State.Projects.Count(p => p.Archived == App.ShowArchived);
            Ui.Text(g, "پروژه‌ها", Fonts.Get(13f, Weight.Bold), Palette.Ink, new Rectangle(Width - Pad - Ui.S(120), py, Ui.S(120), Ui.S(40)));
            int nw = Ui.Measure(g, "پروژه‌ها", Fonts.Get(13f, Weight.Bold)).Width;
            string cnt = Dates.Digits(count.ToString());
            int bw = BadgeWidth(g, cnt);
            Badge(g, new Rectangle(Width - Pad - nw - Ui.S(10) - bw, py + Ui.S(10), bw, Ui.S(22)), cnt, Palette.Surface2, Palette.Ink2);
            Segmented(g, new Rectangle(Width - Pad - nw - bw - Ui.S(200), py + Ui.S(1), Ui.S(170), Ui.S(38)), new[] { "جاری", "بایگانی" }, App.ShowArchived ? 1 : 0, "arch", i => { App.ShowArchived = i == 1; board.ScrollY = 0; Invalidate(true); });
            Button(g, new Rectangle(Pad, py + Ui.S(1), Ui.S(138), Ui.S(38)), "پروژهٔ جدید", Glyph.Plus, Kind.Primary, "new", () => App.EditProject(null), "Ctrl+N");
        }
        private static string Greeting(DateTime now)
        {
            int h = now.Hour;
            return h < 5 ? "شب بخیر" : h < 12 ? "صبح بخیر" : h < 17 ? "ظهر بخیر" : h < 21 ? "عصر بخیر" : "شب بخیر";
        }
        private void Hero(Graphics g, Rectangle r)
        {
            var a = State.Active;
            if (a != null)
            {
                var p = ProjectOf(a.ProjectId);
                Color pc = p == null ? Palette.Accent : Palette.Project(p.Color);
                using (var brush = new LinearGradientBrush(r, Palette.Mix(Palette.Surface, pc, Palette.Dark ? 0.22 : 0.13), Palette.Mix(Palette.Surface, pc, Palette.Dark ? 0.06 : 0.03), LinearGradientMode.Horizontal))
                using (var path = Ui.Round(r, Ui.S(16))) g.FillPath(brush, path);
                Ui.Stroke(g, r, Palette.Alpha(pc, 70), Ui.S(16));
                int x = r.Right - Ui.S(24);
                bool blink = (Snap.Now / TimeSpan.TicksPerSecond) % 2 == 0;
                Ui.Circle(g, new RectangleF(x - Ui.S(10), r.Y + Ui.S(28), Ui.S(10), Ui.S(10)), Palette.Alpha(pc, blink ? 255 : 120));
                if (blink) using (var pen = new Pen(Palette.Alpha(pc, 60), 4 * Ui.Scale)) g.DrawEllipse(pen, x - Ui.S(13), r.Y + Ui.S(25), Ui.S(16), Ui.S(16));
                Ui.Text(g, "در حال ثبت  •  از ساعت " + Dates.Clock(a.StartUtc, App.Zone), Fonts.Get(9f, Weight.Medium), Palette.Ink2, new Rectangle(r.X + Ui.S(24), r.Y + Ui.S(20), x - Ui.S(20) - r.X - Ui.S(24), Ui.S(26)));
                int textW = r.Width / 2;
                Ui.Text(g, p == null ? "" : p.Name, Fonts.Get(19f, Weight.Bold), Palette.Ink, new Rectangle(x - textW, r.Y + Ui.S(54), textW, Ui.S(42)));
                string note = string.IsNullOrEmpty(a.Note) ? "+ افزودن یادداشت برای این فعالیت" : a.Note.Replace("\r", " ").Replace("\n", " ");
                var noteRect = new Rectangle(x - textW, r.Y + Ui.S(98), textW, Ui.S(28));
                Ui.Text(g, note, Fonts.Get(9.5f), Hot("note") ? Palette.Accent : (string.IsNullOrEmpty(a.Note) ? Palette.Ink3 : Palette.Ink2), noteRect);
                AddHit(noteRect, "note", App.EditActiveNote, "ویرایش یادداشت");

                long elapsed = Snap.Now - a.StartUtc;
                Ui.Text(g, Dates.Duration(elapsed), Fonts.Get(36f, Weight.Black), pc, new Rectangle(r.X + Ui.S(22), r.Y + Ui.S(42), r.Width / 2 - Ui.S(30), Ui.S(64)), Align.Left, false, true);
                Ui.Text(g, "امروز روی این پروژه: " + Dates.Hm(Snapshot.Get(Snap.TodayBy, a.ProjectId)), Fonts.Get(9f), Palette.Ink2, new Rectangle(r.X + Ui.S(26), r.Y + Ui.S(104), r.Width / 2 - Ui.S(30), Ui.S(22)), Align.Left);
                Button(g, new Rectangle(r.X + Ui.S(24), r.Bottom - Ui.S(62), Ui.S(130), Ui.S(42)), "توقف", Glyph.Stop, Kind.Danger, "stop", App.Stop, "Ctrl+Alt+Space");
                Button(g, new Rectangle(r.X + Ui.S(164), r.Bottom - Ui.S(62), Ui.S(42), Ui.S(42)), null, Glyph.Note, Kind.Outline, "noteb", App.EditActiveNote, "یادداشت");

                int focus = State.Settings.FocusMinutes;
                var foot = new Rectangle(r.X + Ui.S(226), r.Bottom - Ui.S(56), x - r.X - Ui.S(226), Ui.S(30));
                if (focus > 0)
                {
                    long cycle = Snap.Now - App.FocusAnchor; double v = cycle / (double)(focus * TimeSpan.TicksPerMinute);
                    long left = Math.Max(0, focus * TimeSpan.TicksPerMinute - cycle);
                    Icons.Draw(g, Glyph.Coffee, new RectangleF(foot.Right - Ui.S(20), foot.Y + Ui.S(2), Ui.S(18), Ui.S(18)), Palette.Ink2);
                    Ui.Text(g, left > 0 ? "تا استراحت: " + Dates.Hm(left + TimeSpan.TicksPerMinute - 1) : "وقت استراحت است", Fonts.Get(8.5f, Weight.Medium), left > 0 ? Palette.Ink2 : Palette.Warning, new Rectangle(foot.X, foot.Y, foot.Width - Ui.S(28), Ui.S(22)));
                    ProgressBar(g, new Rectangle(foot.X, foot.Y + Ui.S(26), foot.Width, Ui.S(6)), v, left > 0 ? pc : Palette.Warning);
                }
                else Ui.Text(g, "با انتخاب پروژهٔ دیگر، ثبت زمان بی‌وقفه به آن منتقل می‌شود.", Fonts.Get(8.5f), Palette.Ink3, new Rectangle(foot.X, foot.Y, foot.Width, Ui.S(28)));
                return;
            }
            Ui.Card(g, r, Palette.Surface, null, Ui.S(16));
            int right = r.Right - Ui.S(26);
            Ui.Text(g, "برای چه کاری وقت می‌گذاری؟", Fonts.Get(16f, Weight.Bold), Palette.Ink, new Rectangle(r.X + Ui.S(24), r.Y + Ui.S(24), right - r.X - Ui.S(24), Ui.S(36)));
            Ui.Text(g, State.Settings.AskAfterSleep ? "یک پروژه را انتخاب کن تا ثبت زمان شروع شود." : "یک پروژه را انتخاب کن تا ثبت زمان شروع شود؛ زمان اسلیپ هم حساب می‌شود.", Fonts.Get(9.5f), Palette.Ink2, new Rectangle(r.X + Ui.S(24), r.Y + Ui.S(62), right - r.X - Ui.S(24), Ui.S(24)));
            var recent = State.Sessions.OrderByDescending(s => s.EndUtc).Select(s => s.ProjectId).Distinct()
                .Concat(State.Projects.Select(p => p.Id)).Distinct().Select(ProjectOf).Where(p => p != null && !p.Archived).Take(4).ToList();
            int cx = right;
            foreach (var p in recent)
            {
                var font = Fonts.Get(9.5f, Weight.Medium);
                int w = Math.Min(Ui.S(170), Ui.Measure(g, p.Name, font).Width + Ui.S(48));
                if (cx - w < r.X + Ui.S(24)) break;
                var chip = new Rectangle(cx - w, r.Y + Ui.S(104), w, Ui.S(38)); string id = "q" + p.Id, pid = p.Id;
                Color pc = Palette.Project(p.Color);
                Ui.Fill(g, chip, Hot(id) ? Palette.Mix(Palette.Surface, pc, 0.16) : Palette.Surface2, Ui.S(19));
                Ui.Stroke(g, chip, Hot(id) ? Palette.Alpha(pc, 140) : Palette.Line, Ui.S(19));
                Icons.Draw(g, Glyph.Play, new RectangleF(chip.Right - Ui.S(30), chip.Y + Ui.S(11), Ui.S(16), Ui.S(16)), pc);
                Ui.Text(g, p.Name, font, Palette.Ink, new Rectangle(chip.X + Ui.S(12), chip.Y, chip.Width - Ui.S(44), chip.Height));
                AddHit(chip, id, () => App.Start(pid), "شروع «" + p.Name + "»");
                cx -= w + Ui.S(10);
            }
            if (recent.Count == 0)
                Button(g, new Rectangle(right - Ui.S(170), r.Y + Ui.S(104), Ui.S(170), Ui.S(40)), "ساخت اولین پروژه", Glyph.Plus, Kind.Primary, "first", () => App.EditProject(null));
            Ui.Text(g, "میان‌بر سراسری: Ctrl+Alt+Space برای شروع/توقف از هر جای ویندوز", Fonts.Get(8.5f), Palette.Ink3, new Rectangle(r.X + Ui.S(24), r.Bottom - Ui.S(44), r.Width - Ui.S(50), Ui.S(24)));
        }
        private void Goal(Graphics g, Rectangle r)
        {
            Ui.Card(g, r, null, null, Ui.S(16));
            int goal = State.Settings.DailyGoalMinutes;
            Ui.Text(g, "هدف امروز", Fonts.Get(11f, Weight.Bold), Palette.Ink, new Rectangle(r.X + Ui.S(20), r.Y + Ui.S(18), r.Width - Ui.S(40), Ui.S(28)));
            if (Snap.Streak > 0)
            {
                string text = Dates.Digits(Snap.Streak.ToString()) + " روز پیاپی";
                int w = BadgeWidth(g, text) + Ui.S(18);
                var b = new Rectangle(r.X + Ui.S(18), r.Y + Ui.S(19), w, Ui.S(26));
                Ui.Fill(g, b, Palette.WarningSoft, Ui.S(13));
                Icons.Draw(g, Glyph.Flame, new RectangleF(b.Right - Ui.S(23), b.Y + Ui.S(5), Ui.S(16), Ui.S(16)), Palette.Warning);
                Ui.Text(g, text, Fonts.Get(8f, Weight.Bold), Palette.Warning, new Rectangle(b.X + Ui.S(8), b.Y, b.Width - Ui.S(32), b.Height), Align.Center);
                AddHit(b, "streak", null, goal > 0 ? "روزهای پشت‌سرهم که هدف روزانه کامل شده" : "روزهای پشت‌سرهم با فعالیت");
            }
            int ring = Math.Min(r.Height - Ui.S(76), Ui.S(124));
            var rr = new Rectangle(r.Right - Ui.S(22) - ring, r.Y + Ui.S(58), ring, ring);
            double v = goal <= 0 ? 0 : Snap.TodayTotal / (double)(goal * TimeSpan.TicksPerMinute);
            Ring(g, rr, v, v >= 1 ? Palette.Success : Palette.Accent, Ui.S(11));
            string pct = goal <= 0 ? "—" : Dates.Digits(((int)Math.Min(999, v * 100)).ToString()) + "٪";
            Ui.Text(g, pct, Fonts.Get(16f, Weight.Black), Palette.Ink, new Rectangle(rr.X, rr.Y + rr.Height / 2 - Ui.S(22), rr.Width, Ui.S(32)), Align.Center, false, true);
            Ui.Text(g, "پیشرفت", Fonts.Get(8f), Palette.Ink3, new Rectangle(rr.X, rr.Y + rr.Height / 2 + Ui.S(8), rr.Width, Ui.S(18)), Align.Center);
            int tx = r.X + Ui.S(18), tw = rr.X - Ui.S(14) - tx;
            if (goal <= 0)
            {
                Ui.Text(g, "هدفی تعیین نشده", Fonts.Get(11f, Weight.Medium), Palette.Ink, new Rectangle(tx, rr.Y + Ui.S(10), tw, Ui.S(28)));
                Button(g, new Rectangle(tx, rr.Y + Ui.S(52), Math.Min(tw, Ui.S(140)), Ui.S(36)), "تعیین هدف", Glyph.Target, Kind.Soft, "setgoal", () => App.Navigate("settings"));
                return;
            }
            Ui.Text(g, Dates.Hm(Snap.TodayTotal), Fonts.Get(20f, Weight.Black), Palette.Ink, new Rectangle(tx, rr.Y + Ui.S(2), tw, Ui.S(40)), Align.Right, false, true);
            Ui.Text(g, "از " + Dates.Human(goal * TimeSpan.TicksPerMinute), Fonts.Get(9f), Palette.Ink2, new Rectangle(tx, rr.Y + Ui.S(42), tw, Ui.S(22)));
            long left = goal * TimeSpan.TicksPerMinute - Snap.TodayTotal;
            string status = left <= 0 ? "هدف امروز کامل شد!" : (tw > Ui.S(190) ? Dates.Human(left) : Dates.Hm(left)) + " مانده";
            var sr = new Rectangle(tx, rr.Y + Ui.S(74), tw, Ui.S(30));
            Ui.Fill(g, sr, left <= 0 ? Palette.Alpha(Palette.Success, Palette.Dark ? 50 : 30) : Palette.Surface2, Ui.S(9));
            Ui.Text(g, status, Fonts.Get(8.5f, Weight.Medium), left <= 0 ? Palette.Success : Palette.Ink2, Rectangle.Inflate(sr, -Ui.S(8), 0), Align.Center);
        }
    }

    public sealed class ProjectBoard : Surface
    {
        private readonly MainForm app;
        public ProjectBoard(MainForm owner) { app = owner; Scrollable = true; }
        protected override void Draw(Graphics g)
        {
            var st = app.State; var snap = app.Snap;
            var projects = st.Projects.Where(p => p.Archived == app.ShowArchived && (app.Search.Length == 0 || p.Name.IndexOf(app.Search, StringComparison.CurrentCultureIgnoreCase) >= 0)).ToList();
            int gap = Ui.S(14), left = Ui.S(14), avail = Width - left;
            int cols = Math.Max(1, (avail + gap) / (Ui.S(270) + gap));
            int cw = (avail - (cols - 1) * gap) / cols, ch = Ui.S(124);
            int n = projects.Count + (app.ShowArchived || app.Search.Length > 0 ? 0 : 1);
            for (int i = 0; i < n; i++)
            {
                int col = i % cols, row = i / cols;
                var r = new Rectangle(Width - (col + 1) * cw - col * gap, row * (ch + gap) + Ui.S(2), cw, ch);
                if (i == projects.Count) { AddCard(g, r); continue; }
                Card(g, r, projects[i], st, snap);
            }
            ContentHeight = ((n + cols - 1) / cols) * (ch + gap) + Ui.S(8);
            if (projects.Count == 0 && n == 0)
            {
                string t = app.Search.Length > 0 ? "پروژه‌ای با این نام پیدا نشد" : "پروژهٔ بایگانی‌شده‌ای ندارید";
                Ui.Text(g, t, Fonts.Get(10f), Palette.Ink3, new Rectangle(0, Ui.S(30), Width, Ui.S(30)), Align.Center);
            }
        }
        private void AddCard(Graphics g, Rectangle r)
        {
            bool hot = Hot("add");
            using (var pen = new Pen(hot ? Palette.Accent : Palette.LineStrong, 1.4f * Ui.Scale) { DashStyle = DashStyle.Dash })
            using (var path = Ui.Round(new RectangleF(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2), Ui.S(14)))
            {
                if (hot) using (var b = new SolidBrush(Palette.Alpha(Palette.AccentSoft, 160))) g.FillPath(b, path);
                g.DrawPath(pen, path);
            }
            var c = new Rectangle(r.X + r.Width / 2 - Ui.S(18), r.Y + Ui.S(28), Ui.S(36), Ui.S(36));
            Ui.Fill(g, c, hot ? Palette.Accent : Palette.Surface2, Ui.S(18));
            Icons.Draw(g, Glyph.Plus, Rectangle.Inflate(c, -Ui.S(9), -Ui.S(9)), hot ? Palette.AccentInk : Palette.Ink2);
            Ui.Text(g, "پروژهٔ جدید", Fonts.Get(9.5f, Weight.Medium), hot ? Palette.Accent : Palette.Ink2, new Rectangle(r.X, c.Bottom + Ui.S(8), r.Width, Ui.S(24)), Align.Center);
            AddHit(r, "add", () => app.EditProject(null));
        }
        private void Card(Graphics g, Rectangle r, Project p, AppState st, Snapshot snap)
        {
            bool active = st.Active != null && st.Active.ProjectId == p.Id;
            string id = "card" + p.Id;
            bool hot = Hot(id) || (HoverId != null && HoverId.EndsWith(p.Id));
            Color pc = Palette.Project(p.Color);
            Color fill = active ? Palette.Mix(Palette.Surface, pc, Palette.Dark ? 0.16 : 0.08) : Palette.Surface;
            Ui.Fill(g, r, fill, Ui.S(14));
            Ui.Stroke(g, r, active ? pc : hot ? Palette.LineStrong : Palette.Line, Ui.S(14), active ? 1.6f * Ui.Scale : 1f);
            AddHit(r, id, p.Archived || active ? (Action)null : () => app.Start(p.Id), null, () => app.EditProject(p));

            int x = r.Right - Ui.S(18);
            Ui.Circle(g, new RectangleF(x - Ui.S(10), r.Y + Ui.S(22), Ui.S(10), Ui.S(10)), pc);
            int btn = Ui.S(46);
            var play = new Rectangle(r.X + Ui.S(16), r.Y + Ui.S(16), btn, btn);
            Ui.Text(g, p.Name, Fonts.Get(10.5f, Weight.Bold), Palette.Ink, new Rectangle(play.Right + Ui.S(36), r.Y + Ui.S(14), x - Ui.S(16) - play.Right - Ui.S(36), Ui.S(26)));
            if (active) Badge(g, new Rectangle(x - Ui.S(10) - BadgeWidth(g, "در حال ثبت"), r.Y + Ui.S(42), BadgeWidth(g, "در حال ثبت"), Ui.S(20)), "در حال ثبت", pc, Ui.Readable(pc));
            else Ui.Text(g, p.Archived ? "بایگانی‌شده" : "امروز", Fonts.Get(8f), Palette.Ink3, new Rectangle(play.Right, r.Y + Ui.S(42), x - play.Right, Ui.S(20)));
            long today = Snapshot.Get(snap.TodayBy, p.Id), week = Snapshot.Get(snap.WeekBy, p.Id), total = Snapshot.Get(snap.TotalBy, p.Id);
            Ui.Text(g, Dates.Duration(today), Fonts.Get(14f, Weight.Bold), today > 0 || active ? pc : Palette.Ink3, new Rectangle(play.Right, r.Y + Ui.S(60), x - play.Right, Ui.S(30)), Align.Right, false, true);
            if (p.WeeklyGoalMinutes > 0)
            {
                long goal = p.WeeklyGoalMinutes * TimeSpan.TicksPerMinute;
                Ui.Text(g, "هفته: " + Dates.Hm(week) + " از " + Dates.Digits((p.WeeklyGoalMinutes / 60).ToString()) + " ساعت", Fonts.Get(8f), Palette.Ink2, new Rectangle(r.X + Ui.S(16), r.Bottom - Ui.S(34), r.Width - Ui.S(34), Ui.S(18)));
                ProgressBar(g, new Rectangle(r.X + Ui.S(16), r.Bottom - Ui.S(13), r.Width - Ui.S(34), Ui.S(5)), week / (double)goal, week >= goal ? Palette.Success : pc);
            }
            else Ui.Text(g, "این هفته " + Dates.Hm(week) + "   •   کل " + Dates.Hm(total), Fonts.Get(8f), Palette.Ink3, new Rectangle(r.X + Ui.S(16), r.Bottom - Ui.S(30), r.Width - Ui.S(34), Ui.S(20)));

            if (!p.Archived)
            {
                string pid = "play" + p.Id; bool ph = Hot(pid);
                Color bf = active ? (ph ? Palette.Mix(Palette.Danger, Color.Black, 0.1) : Palette.Danger) : ph ? pc : Palette.Mix(Palette.Surface2, pc, Palette.Dark ? 0.2 : 0.12);
                Ui.Fill(g, play, bf, btn / 2);
                Icons.Draw(g, active ? Glyph.Stop : Glyph.Play, Rectangle.Inflate(play, -Ui.S(13), -Ui.S(13)), active || ph ? Color.White : pc);
                AddHit(play, pid, () => { if (active) app.Stop(); else app.Start(p.Id); }, active ? "توقف" : st.Active == null ? "شروع" : "تعویض به این پروژه");
            }
            if (hot || p.Archived)
            {
                var edit = new Rectangle(play.Right + Ui.S(2), r.Y + Ui.S(14), Ui.S(30), Ui.S(30));
                IconButton(g, edit, Glyph.Edit, "edit" + p.Id, () => app.EditProject(p), "ویرایش");
            }
        }
    }

    // ===================================================================== Timesheet
    public sealed class TimesheetPage : Page
    {
        private readonly EntryList list;
        public TimesheetPage(MainForm app) : base(app) { list = new EntryList(app); Controls.Add(list); WantsRepaint = true; }
        private readonly bool WantsRepaint;
        protected override bool WantsMouseRepaint { get { return WantsRepaint && Mouse.Y > Ui.S(190) && Mouse.Y < Ui.S(290); } }
        private int ListTop { get { return Ui.S(304) + Ui.S(46); } }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (list == null) return;
            list.Bounds = new Rectangle(Pad + 1, ListTop, Width - 2 * Pad - 2, Math.Max(Ui.S(40), Height - ListTop - Ui.S(84)));
        }
        protected override void Draw(Graphics g)
        {
            DateTime day = App.SelectedDay;
            Header(g, "تایم‌شیت", Dates.Long(day) + (day == Snap.Today ? "  (امروز)" : ""));
            // Day navigation (RTL: previous is on the right)
            int y = Ui.S(26), h = Ui.S(40), x = Pad;
            Button(g, new Rectangle(x, y, Ui.S(72), h), "امروز", null, Kind.Outline, "today", () => App.SetDay(DateTime.Today), null, day != Snap.Today);
            x += Ui.S(80);
            Button(g, new Rectangle(x, y, h, h), null, Glyph.ChevronLeft, Kind.Outline, "next", () => App.SetDay(day.AddDays(1)), "روز بعد");
            x += h + Ui.S(6);
            int dw = Ui.S(150);
            Button(g, new Rectangle(x, y, dw, h), Dates.Short(day), Glyph.Calendar, Kind.Outline, "pick", App.ChooseDay, "انتخاب از تقویم");
            x += dw + Ui.S(6);
            Button(g, new Rectangle(x, y, h, h), null, Glyph.ChevronRight, Kind.Outline, "prev", () => App.SetDay(day.AddDays(-1)), "روز قبل");

            WeekStrip(g, new Rectangle(Pad, Ui.S(92), Width - 2 * Pad, Ui.S(92)), day);
            Timeline(g, new Rectangle(Pad, Ui.S(198), Width - 2 * Pad, Ui.S(92)), day);

            var card = new Rectangle(Pad, Ui.S(304), Width - 2 * Pad, Height - Ui.S(304) - Ui.S(76));
            Ui.Card(g, card);
            Ui.Text(g, "فعالیت‌های روز", Fonts.Get(11f, Weight.Bold), Palette.Ink, new Rectangle(card.X + Ui.S(20), card.Y + Ui.S(10), card.Width - Ui.S(40), Ui.S(28)));
            int count = Snap.Slices.Count(t => t.LocalDay == day);
            Ui.Text(g, Dates.Digits(count.ToString()) + " مورد  •  جمع " + Dates.Duration(Snap.Day(day)), Fonts.Get(9f), Palette.Ink2, new Rectangle(card.X + Ui.S(20), card.Y + Ui.S(10), card.Width / 2, Ui.S(28)), Align.Left);
            using (var p = new Pen(Palette.Line)) g.DrawLine(p, card.X, ListTop - 2, card.Right, ListTop - 2);

            int fy = Height - Ui.S(62);
            Button(g, new Rectangle(Width - Pad - Ui.S(150), fy, Ui.S(150), Ui.S(42)), "ثبت دستی", Glyph.Plus, Kind.Primary, "add", () => App.EditSession(null), "Ctrl+M");
            Button(g, new Rectangle(Pad, fy, Ui.S(150), Ui.S(42)), "خروجی CSV روز", Glyph.Download, Kind.Outline, "csv", () => App.ExportCsv(day, day.AddDays(1)));
            Ui.Text(g, "دوبار کلیک روی هر ردیف = اصلاح  •  ردیف‌های چندروزه بین روزها تقسیم می‌شوند", Fonts.Get(8.5f), Palette.Ink3, new Rectangle(Pad + Ui.S(170), fy, Width - 2 * Pad - Ui.S(340), Ui.S(42)), Align.Center);
        }
        private void WeekStrip(Graphics g, Rectangle r, DateTime day)
        {
            Ui.Card(g, r);
            DateTime ws = Dates.WeekStart(day);
            int cells = 8, cw = (r.Width - Ui.S(16)) / cells;
            long max = Math.Max(TimeSpan.TicksPerHour, Enumerable.Range(0, 7).Max(i => Snap.Day(ws.AddDays(i))));
            for (int i = 0; i < 7; i++)
            {
                DateTime d = ws.AddDays(i); long v = Snap.Day(d);
                var c = new Rectangle(r.Right - Ui.S(8) - (i + 1) * cw, r.Y + Ui.S(8), cw - Ui.S(4), r.Height - Ui.S(16));
                bool sel = d == day; string id = "w" + i;
                if (sel) { Ui.Fill(g, c, Palette.AccentSoft, Ui.S(10)); Ui.Stroke(g, c, Palette.Alpha(Palette.Accent, 120), Ui.S(10)); }
                else if (Hot(id)) Ui.Fill(g, c, Palette.Surface2, Ui.S(10));
                Ui.Text(g, Dates.Days[(int)d.DayOfWeek], Fonts.Get(8f), d.DayOfWeek == DayOfWeek.Friday ? Palette.Danger : Palette.Ink3, new Rectangle(c.X, c.Y + Ui.S(4), c.Width, Ui.S(18)), Align.Center);
                Ui.Text(g, Dates.DayMonth(d), Fonts.Get(9f, sel ? Weight.Bold : Weight.Medium), Palette.Ink, new Rectangle(c.X, c.Y + Ui.S(22), c.Width, Ui.S(20)), Align.Center);
                Ui.Text(g, v > 0 ? Dates.Hm(v) : "—", Fonts.Get(9.5f, Weight.Bold), v > 0 ? (Palette.Dark ? Palette.AccentHover : Palette.Accent) : Palette.Ink3, new Rectangle(c.X, c.Y + Ui.S(42), c.Width, Ui.S(20)), Align.Center);
                int bw = (int)((c.Width - Ui.S(24)) * (v / (double)max));
                if (bw > 0) Ui.Fill(g, new Rectangle(c.X + (c.Width - bw) / 2, c.Bottom - Ui.S(8), bw, Ui.S(3)), Palette.Accent, Ui.S(2));
                if (d == Snap.Today) Ui.Circle(g, new RectangleF(c.Right - Ui.S(12), c.Y + Ui.S(8), Ui.S(6), Ui.S(6)), Palette.Accent);
                DateTime pick = d;
                AddHit(c, id, () => App.SetDay(pick));
            }
            var tc = new Rectangle(r.X + Ui.S(8), r.Y + Ui.S(8), cw - Ui.S(4), r.Height - Ui.S(16));
            Ui.Fill(g, tc, Palette.Surface2, Ui.S(10));
            long week = Enumerable.Range(0, 7).Sum(i => Snap.Day(ws.AddDays(i)));
            Ui.Text(g, "جمع هفته", Fonts.Get(8f), Palette.Ink3, new Rectangle(tc.X, tc.Y + Ui.S(8), tc.Width, Ui.S(18)), Align.Center);
            Ui.Text(g, Dates.Hm(week), Fonts.Get(12f, Weight.Black), Palette.Ink, new Rectangle(tc.X, tc.Y + Ui.S(28), tc.Width, Ui.S(26)), Align.Center);
            IconButton(g, new Rectangle(tc.X + Ui.S(4), tc.Bottom - Ui.S(26), Ui.S(24), Ui.S(22)), Glyph.ChevronLeft, "wn", () => App.SetDay(day.AddDays(7)), "هفتهٔ بعد");
            IconButton(g, new Rectangle(tc.Right - Ui.S(28), tc.Bottom - Ui.S(26), Ui.S(24), Ui.S(22)), Glyph.ChevronRight, "wp", () => App.SetDay(day.AddDays(-7)), "هفتهٔ قبل");
        }
        private void Timeline(Graphics g, Rectangle r, DateTime day)
        {
            Ui.Card(g, r);
            Ui.Text(g, "خط زمان", Fonts.Get(9.5f, Weight.Bold), Palette.Ink, new Rectangle(r.X + Ui.S(16), r.Y + Ui.S(8), r.Width - Ui.S(32), Ui.S(24)));
            var slices = Snap.Slices.Where(t => t.LocalDay == day).ToList();
            if (slices.Count > 0)
            {
                long first = slices.Min(t => t.StartUtc), last = slices.Max(t => t.EndUtc);
                Ui.Text(g, "اولین: " + Dates.Clock(first, App.Zone) + "   آخرین: " + Dates.Clock(last, App.Zone), Fonts.Get(8.5f), Palette.Ink3, new Rectangle(r.X + Ui.S(16), r.Y + Ui.S(8), r.Width / 2, Ui.S(24)), Align.Left);
            }
            var bar = new Rectangle(r.X + Ui.S(20), r.Y + Ui.S(38), r.Width - Ui.S(40), Ui.S(24));
            Ui.Fill(g, bar, Palette.Sunken, Ui.S(6));
            long dayStart = Dates.Boundary(day, App.Zone), dayEnd = Dates.Boundary(day.AddDays(1), App.Zone);
            double span = dayEnd - dayStart;
            using (var pen = new Pen(Palette.Alpha(Palette.Ink3, 60)))
                for (int h = 0; h <= 24; h += 3)
                {
                    int px = bar.Right - (int)(bar.Width * h / 24.0);
                    if (h > 0 && h < 24) g.DrawLine(pen, px, bar.Y, px, bar.Bottom);
                    Ui.Text(g, Dates.Number(h, "00"), Fonts.Get(7.5f), Palette.Ink3, new Rectangle(px - Ui.S(14), bar.Bottom + Ui.S(3), Ui.S(28), Ui.S(18)), Align.Center);
                }
            string tip = null; Rectangle tipRect = Rectangle.Empty;
            foreach (var t in slices)
            {
                var p = ProjectOf(t.ProjectId); if (p == null) continue;
                int x1 = bar.Right - (int)(bar.Width * ((t.StartUtc - dayStart) / span));
                int x2 = bar.Right - (int)(bar.Width * ((t.EndUtc - dayStart) / span));
                var seg = new Rectangle(x2, bar.Y + Ui.S(3), Math.Max(Ui.S(3), x1 - x2), bar.Height - Ui.S(6));
                Ui.Fill(g, seg, Palette.Project(p.Color), Ui.S(3));
                string text = p.Name + "  •  " + Dates.Clock(t.StartUtc, App.Zone) + "–" + (t.Active ? "اکنون" : Dates.Clock(t.EndUtc, App.Zone)) + "  •  " + Dates.Hm(t.Duration);
                if (Rectangle.Inflate(seg, Ui.S(2), Ui.S(4)).Contains(Mouse)) { tip = text; tipRect = seg; }
            }
            if (day == Snap.Today)
            {
                int nx = bar.Right - (int)(bar.Width * ((Snap.Now - dayStart) / span));
                using (var pen = new Pen(Palette.Danger, 2 * Ui.Scale)) g.DrawLine(pen, nx, bar.Y - Ui.S(4), nx, bar.Bottom + Ui.S(2));
                Ui.Circle(g, new RectangleF(nx - Ui.S(4), bar.Y - Ui.S(8), Ui.S(8), Ui.S(8)), Palette.Danger);
            }
            if (tip != null) AddHit(tipRect, "seg", null, tip);
        }
    }

    public sealed class EntryList : Surface
    {
        private readonly MainForm app;
        public EntryList(MainForm owner) { app = owner; Scrollable = true; }
        protected override Color Background { get { return Palette.Surface; } }
        protected override void Draw(Graphics g)
        {
            var day = app.SelectedDay;
            var rows = app.Snap.Slices.Where(t => t.LocalDay == day).OrderByDescending(t => t.StartUtc).ToList();
            int rh = Ui.S(62), y = 0;
            if (rows.Count == 0)
            {
                ContentHeight = 0;
                int s = Ui.S(48); var c = new Rectangle(Width / 2 - s / 2, Height / 2 - Ui.S(56), s, s);
                Ui.Fill(g, c, Palette.Surface2, s / 2);
                Icons.Draw(g, Glyph.Clock, Rectangle.Inflate(c, -Ui.S(13), -Ui.S(13)), Palette.Ink3);
                Ui.Text(g, "برای این روز فعالیتی ثبت نشده است", Fonts.Get(10.5f, Weight.Medium), Palette.Ink, new Rectangle(0, c.Bottom + Ui.S(10), Width, Ui.S(26)), Align.Center);
                Ui.Text(g, "تایمر یک پروژه را روشن کنید یا زمان را دستی ثبت کنید.", Fonts.Get(9f), Palette.Ink3, new Rectangle(0, c.Bottom + Ui.S(36), Width, Ui.S(22)), Align.Center);
                return;
            }
            var sessions = app.State.Sessions.ToDictionary(s => s.Id);
            foreach (var t in rows)
            {
                var p = app.State.Projects.FirstOrDefault(a => a.Id == t.ProjectId); if (p == null) continue;
                var r = new Rectangle(0, y, Width, rh);
                string id = "row" + t.SessionId + t.StartUtc; bool hot = HoverId != null && HoverId.StartsWith(id);
                if (t.Active) Ui.Fill(g, Rectangle.Inflate(r, -Ui.S(8), -Ui.S(4)), Palette.Mix(Palette.Surface, Palette.Project(p.Color), Palette.Dark ? 0.12 : 0.06), Ui.S(10));
                else if (hot) Ui.Fill(g, Rectangle.Inflate(r, -Ui.S(8), -Ui.S(4)), Palette.Surface2, Ui.S(10));
                Session session; sessions.TryGetValue(t.SessionId, out session);
                AddHit(r, id, null, null, t.Active ? (Action)app.EditActiveNote : () => { if (session != null) app.EditSession(session); });
                Color pc = Palette.Project(p.Color);
                Ui.Fill(g, new Rectangle(r.Right - Ui.S(20), r.Y + Ui.S(14), Ui.S(4), rh - Ui.S(28)), pc, Ui.S(2));
                int tx = r.Right - Ui.S(32), colW = Math.Max(Ui.S(200), Width / 3);
                Ui.Text(g, p.Name, Fonts.Get(10f, Weight.Bold), Palette.Ink, new Rectangle(tx - colW, r.Y + Ui.S(10), colW, Ui.S(24)));
                string note = string.IsNullOrEmpty(t.Note) ? "بدون یادداشت" : t.Note.Replace("\r", " ").Replace("\n", " ");
                Ui.Text(g, note, Fonts.Get(8.5f), string.IsNullOrEmpty(t.Note) ? Palette.Alpha(Palette.Ink3, 160) : Palette.Ink2, new Rectangle(tx - colW, r.Y + Ui.S(33), colW, Ui.S(20)));

                int mid = tx - colW - Ui.S(24);
                string range = Dates.Clock(t.StartUtc, app.Zone) + "  –  " + (t.Active ? "اکنون" : Dates.Clock(t.EndUtc, app.Zone));
                Ui.Text(g, range, Fonts.Get(9.5f, Weight.Medium), Palette.Ink2, new Rectangle(mid - Ui.S(140), r.Y, Ui.S(140), rh), Align.Right, false, true);
                string kind = t.Active ? "در حال ثبت" : t.Manual ? "دستی" : "تایمر";
                bool multi = session != null && (session.StartUtc != t.StartUtc || session.EndUtc != t.EndUtc);
                int bx = mid - Ui.S(160), bw = BadgeWidth(g, kind);
                Badge(g, new Rectangle(bx - bw, r.Y + (rh - Ui.S(22)) / 2, bw, Ui.S(22)), kind, t.Active ? pc : t.Manual ? Palette.WarningSoft : Palette.Surface2, t.Active ? Ui.Readable(pc) : t.Manual ? Palette.Warning : Palette.Ink2);
                if (multi) { int mw = BadgeWidth(g, "چندروزه"); Badge(g, new Rectangle(bx - bw - Ui.S(6) - mw, r.Y + (rh - Ui.S(22)) / 2, mw, Ui.S(22)), "چندروزه", Palette.AccentSoft, Palette.Accent); }

                Ui.Text(g, Dates.Duration(t.Duration), Fonts.Get(11.5f, Weight.Bold), t.Active ? pc : Palette.Ink, new Rectangle(Ui.S(16), r.Y, Ui.S(110), rh), Align.Left, false, true);
                if (hot || t.Active)
                {
                    int ax = Ui.S(130), s = Ui.S(34), ay = r.Y + (rh - s) / 2;
                    if (t.Active) { IconButton(g, new Rectangle(ax, ay, s, s), Glyph.Stop, id + "stop", app.Stop, "توقف", Palette.Danger); }
                    else if (session != null)
                    {
                        string sid = session.Id;
                        IconButton(g, new Rectangle(ax, ay, s, s), Glyph.Trash, id + "del", () => app.DeleteSession(sid), "حذف");
                        IconButton(g, new Rectangle(ax + s + Ui.S(2), ay, s, s), Glyph.Edit, id + "edit", () => app.EditSession(session), "اصلاح");
                        if (!p.Archived) IconButton(g, new Rectangle(ax + 2 * (s + Ui.S(2)), ay, s, s), Glyph.Play, id + "go", () => app.ContinueSession(session), "ادامه با همین پروژه و یادداشت", pc);
                    }
                }
                y += rh;
                using (var pen = new Pen(Palette.Line)) if (t != rows[rows.Count - 1]) g.DrawLine(pen, Ui.S(20), y, Width - Ui.S(20), y);
            }
            ContentHeight = y + Ui.S(6);
        }
    }

    // ===================================================================== Reports
    public sealed class ReportsPage : Page
    {
        private readonly ProjectTable table;
        public ReportsPage(MainForm app) : base(app) { table = new ProjectTable(app); Controls.Add(table); }
        protected override bool WantsMouseRepaint { get { return chartCard.Contains(Mouse) || donutCard.Contains(Mouse) || heatCard.Contains(Mouse) || hadOverlay; } }
        private bool hadOverlay;
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            hadOverlay = chartCard.Contains(e.Location) || donutCard.Contains(e.Location) || heatCard.Contains(e.Location);
        }
        private Rectangle chartCard, donutCard, tableCard, heatCard;
        private void Compute()
        {
            int gap = Ui.S(16), inner = Width - 2 * Pad, top = Ui.S(250);
            int avail = Height - top - Ui.S(20);
            int ch = Math.Max(Ui.S(210), (int)(avail * 0.54));
            int bh = Math.Max(Ui.S(150), avail - ch - gap);
            int rw = (int)((inner - gap) * 0.62);
            chartCard = new Rectangle(Width - Pad - rw, top, rw, ch);
            donutCard = new Rectangle(Pad, top, inner - gap - rw, ch);
            tableCard = new Rectangle(Width - Pad - rw, top + ch + gap, rw, bh);
            heatCard = new Rectangle(Pad, top + ch + gap, inner - gap - rw, bh);
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e); Compute();
            if (table == null) return;
            table.Bounds = new Rectangle(tableCard.X + 1, tableCard.Y + Ui.S(78), tableCard.Width - 2, Math.Max(Ui.S(20), tableCard.Height - Ui.S(84)));
        }
        protected override void Draw(Graphics g)
        {
            Compute();
            DateTime from = App.RangeFrom, until = App.RangeEnd;
            Header(g, "گزارش‌ها", Dates.Long(from) + "  تا  " + Dates.Long(until.AddDays(-1)));
            Button(g, new Rectangle(Pad, Ui.S(26), Ui.S(128), Ui.S(40)), "خروجی CSV", Glyph.Download, Kind.Outline, "csv", () => App.ExportCsv(from, until), "برای Excel");
            Button(g, new Rectangle(Pad + Ui.S(136), Ui.S(26), Ui.S(136), Ui.S(40)), "بازهٔ دلخواه", Glyph.Calendar, App.RangeKey == "custom" ? Kind.Soft : Kind.Outline, "custom", App.ChooseRange);
            var keys = new[] { "week", "lastweek", "month", "lastmonth", "year", "all" };
            Segmented(g, new Rectangle(Width - Pad - Math.Min(Ui.S(600), Width - 2 * Pad), Ui.S(90), Math.Min(Ui.S(600), Width - 2 * Pad), Ui.S(42)),
                new[] { "این هفته", "هفتهٔ قبل", "این ماه", "ماه قبل", "امسال", "همه" }, Array.IndexOf(keys, App.RangeKey), "rng", i => App.SetRange(keys[i]));

            var range = Snap.Slices.Where(t => t.LocalDay >= from && t.LocalDay < until).ToList();
            long total = range.Sum(t => t.Duration);
            var days = Engine.DayTotals(range);
            DateTime lastDay = until.AddDays(-1) > Snap.Today ? Snap.Today : until.AddDays(-1);
            int spanDays = Math.Max(1, (int)(lastDay - from).TotalDays + 1);
            int active = days.Count(kv => kv.Value > 0);
            var best = days.OrderByDescending(kv => kv.Value).FirstOrDefault();
            int gap = Ui.S(16), inner = Width - 2 * Pad, tw = (inner - 3 * gap) / 4, ty = Ui.S(148), th = Ui.S(86);
            Tile(g, new Rectangle(Width - Pad - tw, ty, tw, th), Glyph.Clock, "جمع زمان", Dates.Duration(total), Dates.Human(total), Palette.Accent);
            Tile(g, new Rectangle(Width - Pad - 2 * tw - gap, ty, tw, th), Glyph.Bolt, "میانگین روزانه", Dates.Hm(total / spanDays), "روی " + Dates.Digits(spanDays.ToString()) + " روز", Palette.Hex("#3B6FD8"));
            Tile(g, new Rectangle(Width - Pad - 3 * tw - 2 * gap, ty, tw, th), Glyph.Calendar, "روزهای فعال", Dates.Digits(active.ToString()) + " از " + Dates.Digits(spanDays.ToString()), active > 0 ? "میانگین روز فعال: " + Dates.Hm(total / Math.Max(1, active)) : "بدون فعالیت", Palette.Hex("#9A55B4"));
            Tile(g, new Rectangle(Pad, ty, inner - 3 * tw - 3 * gap, th), Glyph.Flame, "بهترین روز", best.Value > 0 ? Dates.Hm(best.Value) : "—", best.Value > 0 ? Dates.Days[(int)best.Key.DayOfWeek] + " " + Dates.DayMonth(best.Key) : "هنوز داده‌ای نیست", Palette.Hex("#E07B39"));

            BarChart(g, chartCard, range, from, until);
            Donut(g, donutCard, range, total);
            Ui.Card(g, tableCard);
            Ui.Text(g, "پروژه‌ها در این بازه", Fonts.Get(11f, Weight.Bold), Palette.Ink, new Rectangle(tableCard.X + Ui.S(20), tableCard.Y + Ui.S(12), tableCard.Width - Ui.S(40), Ui.S(28)));
            ProjectTable.Head(g, new Rectangle(tableCard.X, tableCard.Y + Ui.S(46), tableCard.Width, Ui.S(30)));
            Heatmap(g, heatCard);
        }
        private void BarChart(Graphics g, Rectangle card, List<TimeSlice> range, DateTime from, DateTime until)
        {
            Ui.Card(g, card);
            DateTime last = until.AddDays(-1) > Snap.Today ? Snap.Today : until.AddDays(-1);
            int span = (int)(last - from).TotalDays + 1;
            // Bucket by day, week or Persian month depending on the span.
            var buckets = new List<KeyValuePair<DateTime, DateTime>>(); string unit;
            if (span <= 31) { unit = "روزانه"; for (var d = from; d <= last; d = d.AddDays(1)) buckets.Add(new KeyValuePair<DateTime, DateTime>(d, d.AddDays(1))); }
            else if (span <= 26 * 7) { unit = "هفتگی"; for (var d = Dates.WeekStart(from); d <= last; d = d.AddDays(7)) buckets.Add(new KeyValuePair<DateTime, DateTime>(d < from ? from : d, d.AddDays(7))); }
            else { unit = "ماهانه"; for (var d = Dates.MonthStart(from); d <= last; d = Dates.MonthStart(d.AddDays(32))) buckets.Add(new KeyValuePair<DateTime, DateTime>(d < from ? from : d, Dates.MonthStart(d.AddDays(32)))); }
            if (buckets.Count > 40) buckets = buckets.Skip(buckets.Count - 40).ToList();
            Ui.Text(g, "روند زمان (" + unit + ")", Fonts.Get(11f, Weight.Bold), Palette.Ink, new Rectangle(card.X + Ui.S(20), card.Y + Ui.S(12), card.Width - Ui.S(40), Ui.S(28)));
            if (buckets.Count == 0) return;
            var order = State.Projects.Select(p => p.Id).ToList();
            var values = buckets.Select(b => range.Where(t => t.LocalDay >= b.Key && t.LocalDay < b.Value).GroupBy(t => t.ProjectId).ToDictionary(x => x.Key, x => x.Sum(t => t.Duration))).ToList();
            long max = Math.Max(TimeSpan.TicksPerHour, values.Select(v => v.Values.Sum()).DefaultIfEmpty(0).Max());
            double hours = Math.Ceiling(max / (double)TimeSpan.TicksPerHour);
            double step = hours <= 4 ? 1 : hours <= 8 ? 2 : hours <= 20 ? 5 : hours <= 60 ? 10 : hours <= 200 ? 50 : 100;
            hours = Math.Ceiling(hours / step) * step;
            var plot = new Rectangle(card.X + Ui.S(20), card.Y + Ui.S(52), card.Width - Ui.S(70), card.Height - Ui.S(52) - Ui.S(36));
            using (var pen = new Pen(Palette.Line) { DashStyle = DashStyle.Dot })
                for (double h = 0; h <= hours + 0.001; h += step)
                {
                    int y = plot.Bottom - (int)(plot.Height * h / hours);
                    g.DrawLine(pen, plot.X, y, plot.Right, y);
                    Ui.Text(g, Dates.Digits(h.ToString(CultureInfo.InvariantCulture)) + "س", Fonts.Get(7.5f), Palette.Ink3, new Rectangle(plot.Right + Ui.S(6), y - Ui.S(9), Ui.S(40), Ui.S(18)));
                }
            int n = buckets.Count; float slot = plot.Width / (float)n; int bw = (int)Math.Max(Ui.S(4), Math.Min(Ui.S(38), slot * 0.62f));
            int hover = -1;
            for (int i = 0; i < n; i++)
            {
                // Oldest bucket on the right (RTL timeline).
                int cx = plot.Right - (int)(slot * i + slot / 2);
                var col = new Rectangle(cx - (int)(slot / 2), plot.Y, (int)slot, plot.Height + Ui.S(30));
                bool hot = col.Contains(Mouse); if (hot) hover = i;
                if (hot) Ui.Fill(g, new Rectangle(cx - bw / 2 - Ui.S(5), plot.Y - Ui.S(4), bw + Ui.S(10), plot.Height + Ui.S(8)), Palette.Surface2, Ui.S(6));
                var v = values[i];
                var stack = order.Where(v.ContainsKey).ToList();
                int totalH = (int)Math.Round(plot.Height * (v.Values.Sum() / (double)TimeSpan.TicksPerHour) / hours);
                if (totalH > 0)
                {
                    // Stack segments inside one bar whose top corners are rounded.
                    var state = g.Save();
                    using (var clip = Ui.Round(new RectangleF(cx - bw / 2, plot.Bottom - totalH, bw, totalH + Ui.S(6)), Math.Min(Ui.S(5), bw / 2f))) g.SetClip(clip, CombineMode.Intersect);
                    g.SetClip(new Rectangle(cx - bw / 2, plot.Y - Ui.S(2), bw, plot.Height + Ui.S(2)), CombineMode.Intersect);
                    int y = plot.Bottom;
                    for (int k = 0; k < stack.Count; k++)
                    {
                        var p = ProjectOf(stack[k]);
                        int h = k == stack.Count - 1 ? y - (plot.Bottom - totalH) : (int)Math.Round(plot.Height * (v[stack[k]] / (double)TimeSpan.TicksPerHour) / hours);
                        if (h <= 0) continue;
                        using (var b = new SolidBrush(Palette.Project(p.Color))) g.FillRectangle(b, cx - bw / 2, y - h, bw, h);
                        if (k < stack.Count - 1) using (var sep = new Pen(Palette.Surface, 1.5f)) g.DrawLine(sep, cx - bw / 2, y - h, cx + bw / 2, y - h);
                        y -= h;
                    }
                    g.Restore(state);
                }
                bool label = n <= 14 || i % (int)Math.Ceiling(n / 14.0) == 0;
                if (label)
                {
                    string text = unit == "روزانه" ? (n <= 8 ? Dates.Days[(int)buckets[i].Key.DayOfWeek] : Dates.Digits(Dates.Persian.GetDayOfMonth(buckets[i].Key).ToString()))
                        : unit == "هفتگی" ? Dates.DayMonth(buckets[i].Key) : Dates.Months[Dates.Persian.GetMonth(buckets[i].Key) - 1];
                    Ui.Text(g, text, Fonts.Get(7.5f), hot ? Palette.Ink : Palette.Ink3, new Rectangle(cx - Ui.S(36), plot.Bottom + Ui.S(6), Ui.S(72), Ui.S(18)), Align.Center);
                }
            }
            if (hover >= 0)
            {
                var b = buckets[hover]; var v = values[hover];
                string title = unit == "روزانه" ? Dates.Days[(int)b.Key.DayOfWeek] + " " + Dates.DayMonth(b.Key) : unit == "هفتگی" ? "هفتهٔ " + Dates.DayMonth(b.Key) : Dates.MonthYear(b.Key);
                var lines = v.OrderByDescending(kv => kv.Value).Take(4).ToList();
                int w = Ui.S(210), h = Ui.S(52) + lines.Count * Ui.S(22);
                int x = Math.Max(card.X + Ui.S(8), Math.Min(card.Right - w - Ui.S(8), Mouse.X - w - Ui.S(14)));
                if (Mouse.X - w - Ui.S(14) < card.X) x = Mouse.X + Ui.S(14);
                int y = Math.Max(card.Y + Ui.S(8), Math.Min(card.Bottom - h - Ui.S(8), Mouse.Y - h / 2));
                var box = new Rectangle(x, y, w, h);
                Ui.Fill(g, new Rectangle(box.X, box.Y + Ui.S(3), box.Width, box.Height), Palette.Alpha(Color.Black, Palette.Dark ? 90 : 24), Ui.S(10));
                Ui.Card(g, box, Palette.Surface, Palette.LineStrong, Ui.S(10));
                Ui.Text(g, title, Fonts.Get(9f, Weight.Bold), Palette.Ink, new Rectangle(box.X + Ui.S(12), box.Y + Ui.S(8), w - Ui.S(24), Ui.S(20)));
                Ui.Text(g, Dates.Hm(v.Values.Sum()), Fonts.Get(9f, Weight.Bold), Palette.Accent, new Rectangle(box.X + Ui.S(12), box.Y + Ui.S(8), w - Ui.S(24), Ui.S(20)), Align.Left);
                int ly = box.Y + Ui.S(36);
                foreach (var kv in lines)
                {
                    var p = ProjectOf(kv.Key);
                    Ui.Circle(g, new RectangleF(box.Right - Ui.S(22), ly + Ui.S(6), Ui.S(9), Ui.S(9)), Palette.Project(p.Color));
                    Ui.Text(g, p.Name, Fonts.Get(8.5f), Palette.Ink2, new Rectangle(box.X + Ui.S(60), ly, w - Ui.S(90), Ui.S(20)));
                    Ui.Text(g, Dates.Hm(kv.Value), Fonts.Get(8.5f, Weight.Medium), Palette.Ink, new Rectangle(box.X + Ui.S(12), ly, Ui.S(50), Ui.S(20)), Align.Left);
                    ly += Ui.S(22);
                }
            }
        }
        private void Donut(Graphics g, Rectangle card, List<TimeSlice> range, long total)
        {
            Ui.Card(g, card);
            Ui.Text(g, "سهم پروژه‌ها", Fonts.Get(11f, Weight.Bold), Palette.Ink, new Rectangle(card.X + Ui.S(20), card.Y + Ui.S(12), card.Width - Ui.S(40), Ui.S(28)));
            var parts = range.GroupBy(t => t.ProjectId).Select(x => new KeyValuePair<string, long>(x.Key, x.Sum(t => t.Duration))).Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).ToList();
            int size = Math.Min(card.Height - Ui.S(72), (card.Width - Ui.S(40)) * 9 / 20);
            var dr = new Rectangle(card.Right - Ui.S(20) - size, card.Y + Ui.S(52) + (card.Height - Ui.S(64) - size) / 2, size, size);
            float thick = Math.Max(Ui.S(14), size * 0.16f);
            var ring = new RectangleF(dr.X + thick / 2, dr.Y + thick / 2, dr.Width - thick, dr.Height - thick);
            if (total <= 0)
            {
                using (var pen = new Pen(Palette.Sunken, thick)) g.DrawEllipse(pen, ring);
                Ui.Text(g, "بدون داده", Fonts.Get(9f), Palette.Ink3, dr, Align.Center);
                return;
            }
            float angle = -90; string hoverText = null;
            double mouseAngle = Math.Atan2(Mouse.Y - (dr.Y + dr.Height / 2.0), Mouse.X - (dr.X + dr.Width / 2.0)) * 180 / Math.PI;
            double mouseDist = Math.Sqrt(Math.Pow(Mouse.X - (dr.X + dr.Width / 2.0), 2) + Math.Pow(Mouse.Y - (dr.Y + dr.Height / 2.0), 2));
            foreach (var kv in parts)
            {
                var p = ProjectOf(kv.Key);
                float sweep = (float)(360.0 * kv.Value / total);
                double rel = ((mouseAngle - angle) % 360 + 360) % 360;
                bool hot = mouseDist > ring.Width / 2 - thick && mouseDist < ring.Width / 2 + thick / 2 && rel < sweep;
                using (var pen = new Pen(Palette.Project(p.Color), hot ? thick + Ui.S(5) : thick)) g.DrawArc(pen, ring, angle, Math.Max(0.5f, sweep - (parts.Count > 1 ? 1.2f : 0)));
                if (hot) hoverText = p.Name + "\n" + Dates.Hm(kv.Value) + "  •  " + Dates.Number(kv.Value * 100.0 / total, "0.#") + "٪";
                angle += sweep;
            }
            if (hoverText != null)
            {
                var lines = hoverText.Split('\n');
                Ui.Text(g, lines[0], Fonts.Get(9f, Weight.Bold), Palette.Ink, new Rectangle(dr.X + (int)thick, dr.Y + dr.Height / 2 - Ui.S(22), dr.Width - 2 * (int)thick, Ui.S(22)), Align.Center);
                Ui.Text(g, lines[1], Fonts.Get(8.5f), Palette.Ink2, new Rectangle(dr.X + (int)thick, dr.Y + dr.Height / 2, dr.Width - 2 * (int)thick, Ui.S(22)), Align.Center);
            }
            else
            {
                Ui.Text(g, Dates.Hm(total), Fonts.Get(14f, Weight.Black), Palette.Ink, new Rectangle(dr.X, dr.Y + dr.Height / 2 - Ui.S(24), dr.Width, Ui.S(30)), Align.Center, false, true);
                Ui.Text(g, Dates.Digits(parts.Count.ToString()) + " پروژه", Fonts.Get(8.5f), Palette.Ink3, new Rectangle(dr.X, dr.Y + dr.Height / 2 + Ui.S(6), dr.Width, Ui.S(20)), Align.Center);
            }
            int lx = card.X + Ui.S(16), lw = dr.X - Ui.S(16) - lx, ly = dr.Y + Ui.S(4);
            foreach (var kv in parts.Take(Math.Max(1, (card.Bottom - Ui.S(16) - ly) / Ui.S(30))))
            {
                var p = ProjectOf(kv.Key);
                Ui.Fill(g, new Rectangle(lx + lw - Ui.S(12), ly + Ui.S(8), Ui.S(10), Ui.S(10)), Palette.Project(p.Color), Ui.S(3));
                Ui.Text(g, p.Name, Fonts.Get(8.5f, Weight.Medium), Palette.Ink, new Rectangle(lx + Ui.S(46), ly, lw - Ui.S(64), Ui.S(26)));
                Ui.Text(g, Dates.Number(kv.Value * 100.0 / total, "0") + "٪", Fonts.Get(8.5f, Weight.Bold), Palette.Ink2, new Rectangle(lx, ly, Ui.S(44), Ui.S(26)), Align.Left);
                ly += Ui.S(30);
            }
        }
        private void Heatmap(Graphics g, Rectangle card)
        {
            Ui.Card(g, card);
            Ui.Text(g, "تقویم فعالیت", Fonts.Get(11f, Weight.Bold), Palette.Ink, new Rectangle(card.X + Ui.S(20), card.Y + Ui.S(12), card.Width - Ui.S(40), Ui.S(28)));
            Ui.Text(g, "هفته‌های اخیر", Fonts.Get(8.5f), Palette.Ink3, new Rectangle(card.X + Ui.S(20), card.Y + Ui.S(12), card.Width / 2, Ui.S(28)), Align.Left);
            int labelW = Ui.S(30), top = card.Y + Ui.S(66);
            int rows = 7, gap = Ui.S(3);
            int cell = Math.Max(Ui.S(8), Math.Min(Ui.S(18), (card.Bottom - Ui.S(46) - top) / rows - gap));
            int weeks = Math.Max(4, Math.Min(30, (card.Width - Ui.S(40) - labelW) / (cell + gap)));
            DateTime thisWeek = Dates.WeekStart(Snap.Today);
            int gridW = weeks * (cell + gap) - gap;
            int right = card.Right - Ui.S(20) - labelW;
            int left = right - gridW;
            string[] dn = { "ش", "", "د", "", "چ", "", "ج" };
            for (int r = 0; r < rows; r++) Ui.Text(g, dn[r], Fonts.Get(7.5f), Palette.Ink3, new Rectangle(right + Ui.S(4), top + r * (cell + gap), labelW, cell), Align.Center);
            long goal = Math.Max(TimeSpan.TicksPerHour, State.Settings.DailyGoalMinutes * TimeSpan.TicksPerMinute);
            string tip = null; Rectangle tipRect = Rectangle.Empty; int lastMonth = -1;
            for (int w = 0; w < weeks; w++)
            {
                // Newest week on the left, oldest on the right (RTL reading order).
                DateTime ws = thisWeek.AddDays(-7 * (weeks - 1 - w));
                int x = right - (w + 1) * (cell + gap) + gap;
                int month = Dates.Persian.GetMonth(ws.AddDays(6));
                if (month != lastMonth && w < weeks - 1) { Ui.Text(g, Dates.Months[month - 1], Fonts.Get(7.5f), Palette.Ink3, new Rectangle(x - Ui.S(40), top - Ui.S(20), Ui.S(40) + cell, Ui.S(18)), Align.Right); lastMonth = month; }
                for (int r = 0; r < rows; r++)
                {
                    DateTime d = ws.AddDays(r);
                    var c = new Rectangle(x, top + r * (cell + gap), cell, cell);
                    if (d > Snap.Today) { Ui.Stroke(g, c, Palette.Line, Ui.S(3)); continue; }
                    long v = Snap.Day(d);
                    double level = v <= 0 ? 0 : Math.Min(1, 0.25 + 0.75 * v / (double)goal);
                    Ui.Fill(g, c, v <= 0 ? Palette.Sunken : Palette.Mix(Palette.Mix(Palette.Surface, Palette.Accent, 0.22), Palette.Accent, (level - 0.25) / 0.75), Ui.S(3));
                    if (d == Snap.Today) Ui.Stroke(g, Rectangle.Inflate(c, 1, 1), Palette.Ink2, Ui.S(4));
                    if (c.Contains(Mouse)) { tip = Dates.Days[(int)d.DayOfWeek] + " " + Dates.DayMonth(d) + "  •  " + (v > 0 ? Dates.Hm(v) : "بدون فعالیت"); tipRect = c; }
                }
            }
            int ly = card.Bottom - Ui.S(30);
            Ui.Text(g, "کمتر", Fonts.Get(7.5f), Palette.Ink3, new Rectangle(right - Ui.S(30), ly, Ui.S(30), Ui.S(16)), Align.Right);
            for (int i = 0; i < 5; i++)
            {
                var c = new Rectangle(right - Ui.S(38) - (i + 1) * (Ui.S(12) + gap), ly + Ui.S(2), Ui.S(12), Ui.S(12));
                Ui.Fill(g, c, i == 0 ? Palette.Sunken : Palette.Mix(Palette.Mix(Palette.Surface, Palette.Accent, 0.22), Palette.Accent, (i - 1) / 3.0), Ui.S(3));
            }
            Ui.Text(g, "بیشتر", Fonts.Get(7.5f), Palette.Ink3, new Rectangle(right - Ui.S(38) - 5 * (Ui.S(12) + gap) - Ui.S(40), ly, Ui.S(36), Ui.S(16)), Align.Right);
            if (tip != null) AddHit(tipRect, "cell", null, tip);
        }
    }

    public sealed class ProjectTable : Surface
    {
        private readonly MainForm app;
        public ProjectTable(MainForm owner) { app = owner; Scrollable = true; }
        protected override Color Background { get { return Palette.Surface; } }
        private static int[] Cols(int width)
        {
            // name | time | share | days | avg  (right to left)
            int name = (int)(width * 0.34), time = (int)(width * 0.16), share = (int)(width * 0.26), days = (int)(width * 0.11);
            return new[] { name, time, share, days, width - name - time - share - days };
        }
        public static void Head(Graphics g, Rectangle r)
        {
            using (var b = new SolidBrush(Palette.Surface2)) g.FillRectangle(b, r.X + 1, r.Y, r.Width - 2, r.Height);
            var c = Cols(r.Width - Ui.S(40)); string[] t = { "پروژه", "زمان", "سهم", "روزها", "میانگین" };
            int x = r.Right - Ui.S(20);
            for (int i = 0; i < c.Length; i++) { Ui.Text(g, t[i], Fonts.Get(8f, Weight.Medium), Palette.Ink3, new Rectangle(x - c[i], r.Y, c[i] - Ui.S(8), r.Height)); x -= c[i]; }
        }
        protected override void Draw(Graphics g)
        {
            var range = app.Snap.Slices.Where(t => t.LocalDay >= app.RangeFrom && t.LocalDay < app.RangeEnd).ToList();
            long total = range.Sum(t => t.Duration);
            var rows = app.State.Projects.Select(p => new { P = p, Parts = range.Where(t => t.ProjectId == p.Id).ToList() })
                .Select(x => new { x.P, Time = x.Parts.Sum(t => t.Duration), Days = x.Parts.Select(t => t.LocalDay).Distinct().Count() })
                .Where(x => x.Time > 0 || !x.P.Archived).OrderByDescending(x => x.Time).ToList();
            int rh = Ui.S(44), y = 0; var c = Cols(Width - Ui.S(40));
            if (rows.Count == 0)
            {
                ContentHeight = 0;
                Ui.Text(g, "هنوز پروژه‌ای ندارید؛ از «نمای کلی» یک پروژه بسازید.", Fonts.Get(9.5f), Palette.Ink3, new Rectangle(0, Ui.S(10), Width, Ui.S(40)), Align.Center);
                return;
            }
            foreach (var row in rows)
            {
                var r = new Rectangle(0, y, Width, rh);
                string id = "r" + row.P.Id;
                if (Hot(id)) Ui.Fill(g, Rectangle.Inflate(r, -Ui.S(8), -Ui.S(3)), Palette.Surface2, Ui.S(8));
                AddHit(r, id, null, null, () => app.EditProject(row.P));
                int x = Width - Ui.S(20);
                Ui.Circle(g, new RectangleF(x - Ui.S(10), y + rh / 2 - Ui.S(5), Ui.S(10), Ui.S(10)), Palette.Project(row.P.Color));
                Ui.Text(g, row.P.Name + (row.P.Archived ? " (بایگانی)" : ""), Fonts.Get(9.5f, Weight.Medium), row.Time > 0 ? Palette.Ink : Palette.Ink3, new Rectangle(x - c[0], y, c[0] - Ui.S(20), rh)); x -= c[0];
                Ui.Text(g, Dates.Duration(row.Time), Fonts.Get(9.5f, Weight.Bold), row.Time > 0 ? Palette.Ink : Palette.Ink3, new Rectangle(x - c[1], y, c[1] - Ui.S(8), rh), Align.Right, false, true); x -= c[1];
                double share = total == 0 ? 0 : row.Time / (double)total;
                ProgressBar(g, new Rectangle(x - c[2] + Ui.S(52), y + rh / 2 - Ui.S(3), c[2] - Ui.S(60), Ui.S(6)), share, Palette.Project(row.P.Color));
                Ui.Text(g, Dates.Number(share * 100, "0.0") + "٪", Fonts.Get(8.5f), Palette.Ink2, new Rectangle(x - c[2], y, Ui.S(46), rh), Align.Left); x -= c[2];
                Ui.Text(g, Dates.Digits(row.Days.ToString()), Fonts.Get(9f), Palette.Ink2, new Rectangle(x - c[3], y, c[3] - Ui.S(8), rh)); x -= c[3];
                Ui.Text(g, row.Days > 0 ? Dates.Hm(row.Time / row.Days) : "—", Fonts.Get(9f), Palette.Ink2, new Rectangle(x - c[4], y, c[4] - Ui.S(8), rh));
                y += rh;
            }
            ContentHeight = y + Ui.S(4);
        }
    }

    // ===================================================================== Settings
    public sealed class SettingsPage : Page
    {
        public SettingsPage(MainForm app) : base(app) { Scrollable = true; }
        private int y;
        protected override void Draw(Graphics g)
        {
            if (!measuring) PaintCards(g);
            Header(g, "تنظیمات", "همهٔ تغییرها بلافاصله ذخیره می‌شوند.");
            var s = State.Settings;
            int gap = Ui.S(16), inner = Width - 2 * Pad, colW = Math.Max(Ui.S(380), (inner - gap) / 2);
            bool two = inner >= Ui.S(860);
            if (!two) colW = inner;
            int rx = Width - Pad - colW, lx = two ? Pad : rx;

            // ---- right column
            y = Ui.S(92);
            var c1 = BeginCard(g, rx, colW, "ظاهر", Glyph.Sun, 3);
            Row(g, c1, "پوسته", "روشن، تیره یا هماهنگ با تنظیم ویندوز");
            var themes = new[] { "light", "dark", "system" };
            Segmented(g, new Rectangle(c1.X + Ui.S(18), RowY + Ui.S(8), Ui.S(230), Ui.S(38)), new[] { "روشن", "تیره", "سیستم" }, Array.IndexOf(themes, s.Theme), "theme", i => App.SetTheme(themes[i]));
            NextRow();
            Row(g, c1, "تایمر شناور", "همیشه روی صفحه، بالای پنجره‌های دیگر");
            Toggle(g, ToggleRect(c1), s.MiniWidget, "mini", App.ToggleMini); NextRow();
            Row(g, c1, "نمایش در کنار ساعت ویندوز", "با بستن پنجره، تایمر ادامه می‌یابد");
            Ui.Text(g, "همیشه فعال", Fonts.Get(8.5f, Weight.Medium), Palette.Ink3, new Rectangle(c1.X + Ui.S(18), RowY, Ui.S(100), Ui.S(54)), Align.Left);
            NextRow(); EndCard();

            var c2 = BeginCard(g, rx, colW, "هدف و تمرکز", Glyph.Target, 5);
            Row(g, c2, "هدف روزانه", "مبنای حلقهٔ پیشرفت و روزهای پیاپی");
            Stepper(g, StepRect(c2), s.DailyGoalMinutes == 0 ? "بدون هدف" : Dates.Hm(s.DailyGoalMinutes * TimeSpan.TicksPerMinute) + " ساعت", "goal",
                () => App.Change(x => x.DailyGoalMinutes = Math.Max(0, x.DailyGoalMinutes - 30)), () => App.Change(x => x.DailyGoalMinutes = Math.Min(16 * 60, x.DailyGoalMinutes + 30)));
            NextRow();
            Row(g, c2, "یادآور استراحت (پومودورو)", s.FocusMinutes == 0 ? "بعد از هر دورهٔ تمرکز، یادآوری استراحت" : "هر " + Dates.Digits(s.FocusMinutes.ToString()) + " دقیقه کار، " + Dates.Digits(s.BreakMinutes.ToString()) + " دقیقه استراحت");
            Toggle(g, ToggleRect(c2), s.FocusMinutes > 0, "focus", () => App.Change(x => x.FocusMinutes = x.FocusMinutes > 0 ? 0 : 25));
            NextRow();
            Row(g, c2, "طول تمرکز / استراحت", null, s.FocusMinutes > 0);
            var half = StepRect(c2); half.Width = Ui.S(110);
            Stepper(g, new Rectangle(half.X + Ui.S(118), half.Y, Ui.S(110), half.Height), Dates.Digits(Math.Max(s.FocusMinutes, 25).ToString()) + " د", "fm",
                () => { if (s.FocusMinutes > 0) App.Change(x => x.FocusMinutes = Math.Max(10, x.FocusMinutes - 5)); }, () => { if (s.FocusMinutes > 0) App.Change(x => x.FocusMinutes = Math.Min(180, x.FocusMinutes + 5)); });
            Stepper(g, half, Dates.Digits(s.BreakMinutes.ToString()) + " د", "bm",
                () => { if (s.FocusMinutes > 0) App.Change(x => x.BreakMinutes = Math.Max(1, x.BreakMinutes - 1)); }, () => { if (s.FocusMinutes > 0) App.Change(x => x.BreakMinutes = Math.Min(60, x.BreakMinutes + 1)); });
            NextRow();
            Row(g, c2, "تشخیص دوری از سیستم", s.IdleMinutes == 0 ? "خاموش" : "پس از " + Dates.Digits(s.IdleMinutes.ToString()) + " دقیقه بی‌فعالیتی می‌پرسد");
            var idleStep = StepRect(c2); idleStep.Width = Ui.S(120);
            Toggle(g, new Rectangle(c2.X + Ui.S(18) + Ui.S(132), RowY, Ui.S(44), Ui.S(54)), s.IdleMinutes > 0, "idle", () => App.Change(x => x.IdleMinutes = x.IdleMinutes > 0 ? 0 : 15));
            if (s.IdleMinutes > 0)
                Stepper(g, idleStep, Dates.Digits(s.IdleMinutes.ToString()) + " د", "im", () => App.Change(x => x.IdleMinutes = Math.Max(2, x.IdleMinutes - 1 - (x.IdleMinutes > 10 ? 4 : 0))), () => App.Change(x => x.IdleMinutes = Math.Min(120, x.IdleMinutes + (x.IdleMinutes >= 10 ? 5 : 1))));
            NextRow();
            Row(g, c2, "پرسیدن دربارهٔ زمان اسلیپ", s.AskAfterSleep ? "بعد از بیدار شدن سیستم می‌پرسد" : "زمان اسلیپ جزو کار حساب می‌شود", s.IdleMinutes > 0);
            Toggle(g, ToggleRect(c2), s.AskAfterSleep, "sleep", () => App.Change(x => x.AskAfterSleep = !x.AskAfterSleep));
            NextRow(); EndCard();
            int rightBottom = y;

            // ---- left column
            if (two) y = Ui.S(92);
            var c3 = BeginCard(g, lx, colW, "ویندوز و میان‌بر", Glyph.Bolt, 2);
            Row(g, c3, "اجرا هنگام ورود به ویندوز", "برنامه بی‌صدا کنار ساعت باز می‌شود");
            Toggle(g, ToggleRect(c3), App.StartupEnabled(), "startup", () => App.SetStartup(!App.StartupEnabled())); NextRow();
            Row(g, c3, "میان‌بر سراسری Ctrl+Alt+Space", App.HotkeyStatus);
            Toggle(g, ToggleRect(c3), s.GlobalHotkey, "hotkey", () => App.Change(x => x.GlobalHotkey = !x.GlobalHotkey)); NextRow();
            EndCard();

            var c4 = BeginCard(g, lx, colW, "پشتیبان و داده‌ها", Glyph.Shield, 0);
            Ui.Text(g, "ذخیرهٔ خودکار هر ۱۵ ثانیه، نگه‌داری نسخهٔ قبلی و ۳۰ پشتیبان روزانه. برای انتقال به دستگاه دیگر یک فایل پشتیبان بگیرید.", Fonts.Get(9f), Palette.Ink2,
                new Rectangle(c4.X + Ui.S(18), y, c4.Width - Ui.S(36), Ui.S(44)), Align.Right, true);
            y += Ui.S(52);
            int bx = c4.Right - Ui.S(18), bwid = (c4.Width - Ui.S(36) - Ui.S(16)) / 3;
            Button(g, new Rectangle(bx - bwid, y, bwid, Ui.S(40)), "گرفتن پشتیبان", Glyph.Download, Kind.Soft, "backup", App.Backup);
            Button(g, new Rectangle(bx - 2 * bwid - Ui.S(8), y, bwid, Ui.S(40)), "بازیابی", Glyph.Upload, Kind.Outline, "restore", App.ImportBackup);
            Button(g, new Rectangle(bx - 3 * bwid - Ui.S(16), y, bwid, Ui.S(40)), "پوشهٔ داده‌ها", Glyph.Folder, Kind.Outline, "folder", App.OpenDataFolder);
            y += Ui.S(56); EndCard();

            var c5 = BeginCard(g, lx, colW, "میان‌برهای صفحه‌کلید", Glyph.Keyboard, 0);
            var keys = new[] {
                new[] { "Ctrl+Alt+Space", "شروع/توقف از هر جای ویندوز" }, new[] { "Ctrl+1 … Ctrl+4", "رفتن به صفحه‌ها" },
                new[] { "Ctrl+N", "پروژهٔ جدید" }, new[] { "Ctrl+M", "ثبت دستی فعالیت" }, new[] { "Ctrl+F", "جست‌وجوی پروژه" }, new[] { "Ctrl+T", "تغییر پوسته" } };
            foreach (var k in keys)
            {
                var kf = Fonts.Get(8f, Weight.Medium);
                int kw = Ui.Measure(g, k[0], kf, false).Width + Ui.S(18);
                var cap = new Rectangle(c5.X + Ui.S(18), y + Ui.S(4), kw, Ui.S(24));
                Ui.Fill(g, cap, Palette.Surface2, Ui.S(6)); Ui.Stroke(g, cap, Palette.LineStrong, Ui.S(6));
                Ui.Text(g, k[0], kf, Palette.Ink, cap, Align.Center, false, false);
                Ui.Text(g, k[1], Fonts.Get(9f), Palette.Ink2, new Rectangle(cap.Right + Ui.S(10), y, c5.Right - Ui.S(18) - cap.Right - Ui.S(10), Ui.S(32)));
                y += Ui.S(32);
            }
            y += Ui.S(8); EndCard();
            int leftBottom = y;
            y = Math.Max(rightBottom, leftBottom);
            Button(g, new Rectangle(Pad, y, Ui.S(170), Ui.S(42)), "خروج کامل از برنامه", Glyph.Power, Kind.Outline, "exit", App.ExitApp);
            Ui.Text(g, "روزنگار ۲٫۰٫۰  •  فونت وزیرمتن (OFL)  •  داده‌ها فقط روی همین دستگاه", Fonts.Get(8.5f), Palette.Ink3, new Rectangle(Pad + Ui.S(190), y, Width - 2 * Pad - Ui.S(190), Ui.S(42)));
            ContentHeight = y + Ui.S(64);
        }
        // ---- two-pass card layout: a measuring pass finds each card's height, the real pass paints chrome first.
        private sealed class CardInfo { public Rectangle R; public string Title; public Glyph Icon; }
        private readonly List<CardInfo> cards = new List<CardInfo>();
        private bool measuring;
        private int cardStart, RowY;
        private Rectangle cardRect; private string heading; private Glyph headingIcon;
        protected override void OnPaint(PaintEventArgs e)
        {
            cards.Clear();
            using (var bmp = new Bitmap(1, 1)) using (var g = Graphics.FromImage(bmp)) { measuring = true; try { Draw(g); } finally { measuring = false; } }
            base.OnPaint(e);
        }
        private Rectangle BeginCard(Graphics g, int x, int w, string title, Glyph icon, int rows)
        {
            cardStart = y; cardRect = new Rectangle(x, y, w, 0); heading = title; headingIcon = icon;
            y += Ui.S(58);
            return cardRect;
        }
        private void EndCard()
        {
            var r = new Rectangle(cardRect.X, cardStart, cardRect.Width, y - cardStart + Ui.S(6));
            if (measuring) cards.Add(new CardInfo { R = r, Title = heading, Icon = headingIcon });
            y = r.Bottom + Ui.S(16);
        }
        private void PaintCards(Graphics g)
        {
            foreach (var c in cards)
            {
                var r = c.R;
                Ui.Card(g, r);
                var ic = new Rectangle(r.Right - Ui.S(50), r.Y + Ui.S(13), Ui.S(32), Ui.S(32));
                Ui.Fill(g, ic, Palette.AccentSoft, Ui.S(9));
                Icons.Draw(g, c.Icon, Rectangle.Inflate(ic, -Ui.S(7), -Ui.S(7)), Palette.Dark ? Palette.AccentHover : Palette.Accent);
                Ui.Text(g, c.Title, Fonts.Get(11.5f, Weight.Bold), Palette.Ink, new Rectangle(r.X + Ui.S(18), r.Y + Ui.S(12), r.Width - Ui.S(78), Ui.S(34)));
                using (var p = new Pen(Palette.Line)) g.DrawLine(p, r.X + Ui.S(18), r.Y + Ui.S(56), r.Right - Ui.S(18), r.Y + Ui.S(56));
            }
        }
        private void Row(Graphics g, Rectangle card, string title, string desc, bool enabled = true)
        {
            RowY = y;
            if (measuring) return;
            int tx = card.X + Ui.S(262), tw = card.Right - Ui.S(18) - tx;
            Ui.Text(g, title, Fonts.Get(10f, Weight.Medium), enabled ? Palette.Ink : Palette.Ink3, new Rectangle(tx, y + Ui.S(desc == null ? 15 : 7), tw, Ui.S(24)));
            if (desc != null) Ui.Text(g, desc, Fonts.Get(8.5f), Palette.Ink3, new Rectangle(tx, y + Ui.S(31), tw, Ui.S(20)));
        }
        private void NextRow() { y += Ui.S(56); }
        private Rectangle ToggleRect(Rectangle card) { return new Rectangle(card.X + Ui.S(18), RowY, Ui.S(44), Ui.S(54)); }
        private Rectangle StepRect(Rectangle card) { return new Rectangle(card.X + Ui.S(18), RowY + Ui.S(8), Ui.S(230), Ui.S(38)); }
    }
}
