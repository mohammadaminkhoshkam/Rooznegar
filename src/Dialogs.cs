using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Rooznegar
{
    public static class Native
    {
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        /// <summary>Asks Windows 10 (20H1+) / 11 to draw a dark title bar to match the dark theme.</summary>
        public static void TitleBar(Form form)
        {
            if (Ui.IsMono || !form.IsHandleCreated) return;
            try { int on = Palette.Dark ? 1 : 0; if (DwmSetWindowAttribute(form.Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(form.Handle, 19, ref on, 4); } catch { }
        }
    }

    /// <summary>Themed modal dialog: large title, content area and a footer with primary/secondary actions.</summary>
    public class DialogBase : Form
    {
        private readonly string heading, subtitle;
        protected readonly int Top0;
        private readonly List<KeyValuePair<string, Point>> captions = new List<KeyValuePair<string, Point>>();
        protected Btn Ok, Cancel;
        public DialogBase(string title, int width, int height, string sub = null)
        {
            heading = title; subtitle = sub;
            Text = title; ClientSize = new Size(Ui.S(width), Ui.S(height)); FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None; Font = Fonts.Get(10f); BackColor = Palette.Surface; ForeColor = Palette.Ink; RightToLeft = Ui.Direction;
            KeyPreview = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            Top0 = Ui.S(sub == null ? 66 : 92);
            try { if (Application.OpenForms.Count > 0 && Application.OpenForms[0].Icon != null) Icon = Application.OpenForms[0].Icon; } catch { }
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Native.TitleBar(this); }
        /// <summary>Child controls are placed with the same start-edge layout as surfaces, so they are mirrored once before showing.</summary>
        protected override void OnLoad(EventArgs e)
        {
            foreach (Control c in Controls) c.Bounds = Ui.Flip(c.Bounds, ClientSize.Width);
            base.OnLoad(e);
        }
        protected void Caption(string text, int y, int x = -1)
        {
            captions.Add(new KeyValuePair<string, Point>(text, new Point(x < 0 ? ClientSize.Width - Ui.S(24) : x, y)));
        }
        protected int W { get { return ClientSize.Width - Ui.S(48); } }
        protected int FooterTop { get { return ClientSize.Height - Ui.S(72); } }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Ui.HighQuality(g);
            g.Clear(Palette.Surface);
            Ui.Mirror(g, ClientSize.Width);
            Ui.Text(g, heading, Fonts.Get(15f, Weight.Bold), Palette.Ink, new Rectangle(Ui.S(24), Ui.S(18), W, Ui.S(34)));
            if (subtitle != null) Ui.Text(g, subtitle, Fonts.Get(9.5f), Palette.Ink2, new Rectangle(Ui.S(24), Ui.S(54), W, Ui.S(24)));
            foreach (var c in captions)
                Ui.Text(g, c.Key, Fonts.Get(9f, Weight.Medium), Palette.Ink2, new Rectangle(Ui.S(24), c.Value.Y, c.Value.X - Ui.S(24), Ui.S(22)));
            using (var b = new SolidBrush(Palette.Surface2)) g.FillRectangle(b, 0, FooterTop, ClientSize.Width, ClientSize.Height - FooterTop);
            using (var p = new Pen(Palette.Line)) g.DrawLine(p, 0, FooterTop, ClientSize.Width, FooterTop);
        }
        /// <summary>Adds Cancel at the end edge with the primary action just before it, as Windows dialogs do.</summary>
        public void Finish(Action save, string okText = "Save", Kind kind = Kind.Primary, string cancelText = "Cancel")
        {
            int y = FooterTop + Ui.S(16);
            Cancel = new Btn(cancelText, Kind.Outline).Fit(Ui.S(96)); Cancel.Location = new Point(Ui.S(24), y);
            Ok = new Btn(okText, kind).Fit(Ui.S(120)); Ok.Location = new Point(Cancel.Right + Ui.S(10), y);
            Ok.Click += (s, e) => { try { save(); DialogResult = DialogResult.OK; } catch (Exception error) { Dialogs.Error(this, error); } };
            Cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
            Ok.Under = Cancel.Under = Palette.Surface2;
            Controls.Add(Ok); Controls.Add(Cancel);
            if (okText != null) AcceptButton = Ok; CancelButton = Cancel;
            Ok.TabIndex = 100; Cancel.TabIndex = 101;
        }
        protected InputBox Input(int y, int height = 42, bool multiline = false, string text = "", int x = -1, int width = -1)
        {
            var box = new InputBox(multiline) { Bounds = new Rectangle(x < 0 ? Ui.S(24) : x, y, width < 0 ? W : width, Ui.S(height)) };
            box.Text = text ?? ""; Controls.Add(box); return box;
        }
    }

    public static class Dialogs
    {
        public static void Error(IWin32Window owner, Exception e) { Message(owner, "Something went wrong", e.Message, true); }
        public static void Message(IWin32Window owner, string title, string text, bool warning = false)
        {
            using (var d = new AskDialog(title, text, "OK", warning ? Kind.Danger : Kind.Primary, null)) d.ShowDialog(owner);
        }
        public static bool Confirm(IWin32Window owner, string title, string text, string okText = "Yes", bool danger = false)
        {
            using (var d = new AskDialog(title, text, okText, danger ? Kind.Danger : Kind.Primary, "Cancel")) return d.ShowDialog(owner) == DialogResult.OK;
        }
    }

    public sealed class AskDialog : DialogBase
    {
        public AskDialog(string title, string text, string okText, Kind kind, string cancelText) : base(title, 440, 120)
        {
            int textH;
            using (var g = CreateGraphics()) textH = Ui.MeasureHeight(g, text, Fonts.Get(10f), W) + Ui.S(8);
            ClientSize = new Size(ClientSize.Width, Top0 + textH + Ui.S(20) + Ui.S(72));
            var body = new Canvas { Bounds = new Rectangle(Ui.S(24), Top0, W, textH) };
            body.Painter = (c, g) => Ui.Text(g, text, Fonts.Get(10f), Palette.Ink2, new Rectangle(0, 0, c.Width, c.Height), Align.Right, true);
            Controls.Add(body);
            Finish(() => { }, okText, kind, cancelText ?? "Close");
            if (cancelText == null) { Cancel.Visible = false; Ok.Left = Ui.S(24); }
        }
    }

    /// <summary>Month view. Weeks start on Monday, in the first column at the start edge.</summary>
    public sealed class CalendarView : Surface
    {
        public DateTime Month;
        public DateTime Selected;
        public bool RangeMode;
        public DateTime? RangeStart { get; set; }
        public DateTime? RangeEnd { get; set; }
        public Func<DateTime, bool> Marked;
        public event Action Changed;
        public CalendarView(DateTime selected)
        {
            Selected = selected.Date; Month = Dates.MonthStart(Selected);
        }
        protected override Color Background { get { return Palette.Surface; } }
        private static readonly string[] heads = { "Mo", "Tu", "We", "Th", "Fr", "Sa", "Su" };
        protected override void Draw(Graphics g)
        {
            int header = Ui.S(40), wk = Ui.S(28);
            Ui.Text(g, Dates.MonthYear(Month), Fonts.Get(11f, Weight.Bold), Palette.Ink, new Rectangle(0, 0, Width, header), Align.Center);
            IconButton(g, new Rectangle(Width - Ui.S(36), Ui.S(4), Ui.S(32), Ui.S(32)), Glyph.ChevronRight, "prev", () => { Month = Dates.MonthStart(Month.AddDays(-1)); Invalidate(); }, "Previous month");
            IconButton(g, new Rectangle(Ui.S(4), Ui.S(4), Ui.S(32), Ui.S(32)), Glyph.ChevronLeft, "next", () => { Month = Dates.MonthStart(Month.AddDays(32)); Invalidate(); }, "Next month");
            int cw = Width / 7, ch = (Height - header - wk) / 6;
            for (int i = 0; i < 7; i++)
                Ui.Text(g, heads[i], Fonts.Get(8.5f, Weight.Medium), i >= 5 ? Palette.Danger : Palette.Ink3, new Rectangle(Width - (i + 1) * cw, header, cw, wk), Align.Center);
            int offset = ((int)Month.DayOfWeek + 6) % 7, days = DateTime.DaysInMonth(Month.Year, Month.Month);
            DateTime? hoverDay = null;
            for (int d = 0; d < days; d++)
            {
                int cell = offset + d, col = cell % 7, row = cell / 7;
                var r = new Rectangle(Width - (col + 1) * cw, header + wk + row * ch, cw, ch);
                if (r.Contains(ContentMouse)) hoverDay = Month.AddDays(d);
            }
            DateTime? a = RangeStart, b = RangeEnd ?? (RangeMode && RangeStart.HasValue && !RangeEnd.HasValue ? hoverDay : null);
            if (a.HasValue && b.HasValue && b < a) { var t = a; a = b; b = t; }
            for (int d = 0; d < days; d++)
            {
                DateTime day = Month.AddDays(d);
                int cell = offset + d, col = cell % 7, row = cell / 7;
                var r = new Rectangle(Width - (col + 1) * cw, header + wk + row * ch, cw, ch);
                int size = Math.Min(cw, ch) - Ui.S(4);
                var dot = new Rectangle(r.X + (r.Width - size) / 2, r.Y + (r.Height - size) / 2, size, size);
                bool selected = RangeMode ? (a.HasValue && (day == a.Value || (b.HasValue && day == b.Value))) : day == Selected;
                bool inRange = RangeMode && a.HasValue && b.HasValue && day > a.Value && day < b.Value;
                if (inRange) { using (var br = new SolidBrush(Palette.AccentSoft)) g.FillRectangle(br, r.X, dot.Y + Ui.S(2), r.Width, dot.Height - Ui.S(4)); }
                string id = "d" + d;
                if (selected) Ui.Circle(g, dot, Palette.Accent);
                else if (Hot(id)) Ui.Circle(g, dot, Palette.Surface2);
                if (day == DateTime.Today && !selected) Ui.Stroke(g, dot, Palette.Accent, size / 2, 1.4f * Ui.Scale);
                Color ink = selected ? Palette.AccentInk : col >= 5 ? Palette.Danger : Palette.Ink;
                Ui.Text(g, (d + 1).ToString(), Fonts.Get(9.5f, selected ? Weight.Bold : Weight.Regular), ink, r, Align.Center);
                if (Marked != null && Marked(day)) Ui.Circle(g, new RectangleF(r.X + r.Width / 2f - Ui.S(2), dot.Bottom - Ui.S(7), Ui.S(4), Ui.S(4)), selected ? Palette.AccentInk : Palette.Accent);
                DateTime pick = day;
                AddHit(r, id, () => Pick(pick));
            }
        }
        protected override bool WantsMouseRepaint { get { return RangeMode && RangeStart.HasValue && !RangeEnd.HasValue; } }
        private void Pick(DateTime day)
        {
            if (!RangeMode) Selected = day;
            else if (!RangeStart.HasValue || RangeEnd.HasValue) { RangeStart = day; RangeEnd = null; }
            else { if (day < RangeStart.Value) { RangeEnd = RangeStart; RangeStart = day; } else RangeEnd = day; }
            Invalidate(); if (Changed != null) Changed();
        }
        public void Show(DateTime day) { Selected = day.Date; Month = Dates.MonthStart(day); Invalidate(); }
    }

    public sealed class DateDialog : DialogBase
    {
        public DateTime Value { get; private set; }
        public DateDialog(DateTime current, Func<DateTime, bool> marked = null, string title = "Choose a date") : base(title, 380, 470)
        {
            var cal = new CalendarView(current) { Bounds = new Rectangle(Ui.S(20), Top0 - Ui.S(6), ClientSize.Width - Ui.S(40), Ui.S(300)), Marked = marked };
            Controls.Add(cal);
            var today = new Btn("Today", Kind.Soft).Fit(); today.Location = new Point(ClientSize.Width - Ui.S(24) - today.Width, cal.Bottom + Ui.S(8));
            var yesterday = new Btn("Yesterday", Kind.Secondary).Fit(); yesterday.Location = new Point(today.Left - Ui.S(8) - yesterday.Width, today.Top);
            today.Click += (s, e) => cal.Show(DateTime.Today); yesterday.Click += (s, e) => cal.Show(DateTime.Today.AddDays(-1));
            Controls.Add(today); Controls.Add(yesterday);
            cal.DoubleClick += (s, e) => { Value = cal.Selected; DialogResult = DialogResult.OK; };
            Finish(() => Value = cal.Selected, "Select");
        }
    }

    public sealed class RangeDialog : DialogBase
    {
        public DateTime From, Until;
        public RangeDialog(DateTime from, DateTime untilExclusive) : base("Custom range", 380, 530, "Pick the first day, then the last day.")
        {
            var cal = new CalendarView(untilExclusive.AddDays(-1)) { RangeMode = true, RangeStart = from, RangeEnd = untilExclusive.AddDays(-1), Bounds = new Rectangle(Ui.S(20), Top0 - Ui.S(6), ClientSize.Width - Ui.S(40), Ui.S(300)) };
            Controls.Add(cal);
            var label = new Canvas { Bounds = new Rectangle(Ui.S(24), cal.Bottom + Ui.S(8), W, Ui.S(36)) };
            label.Painter = (c, g) =>
            {
                string text = cal.RangeStart.HasValue ? "From " + Dates.Long(cal.RangeStart.Value) + (cal.RangeEnd.HasValue ? "  to  " + Dates.Long(cal.RangeEnd.Value) : "  —  now pick the last day") : "Pick the first day";
                Ui.Fill(g, new Rectangle(0, 0, c.Width, c.Height), Palette.Surface2, Ui.S(10));
                Ui.Text(g, text, Fonts.Get(9f, Weight.Medium), Palette.Ink2, new Rectangle(Ui.S(10), 0, c.Width - Ui.S(20), c.Height), Align.Center);
            };
            cal.Changed += () => label.Invalidate();
            Controls.Add(label);
            Finish(() =>
            {
                if (!cal.RangeStart.HasValue) throw new ArgumentException("Pick a first day.");
                From = cal.RangeStart.Value; Until = (cal.RangeEnd ?? cal.RangeStart.Value).AddDays(1);
            }, "Show report");
        }
    }

    public sealed class ProjectDialog : DialogBase
    {
        public string ProjectName, ProjectColor;
        public bool Archived, DeleteRequested;
        public int WeeklyGoal;
        public ProjectDialog(Project value = null) : base(value == null ? "New project" : "Edit project", 470, 470, value == null ? "Choose a short name and a color to recognize the project at a glance." : null)
        {
            ProjectColor = value == null ? Palette.ProjectColors[0] : value.Color;
            Archived = value != null && value.Archived; WeeklyGoal = value == null ? 0 : value.WeeklyGoalMinutes;
            int y = Top0;
            Caption("Project name", y);
            var name = Input(y + Ui.S(26), 44, false, value == null ? "" : value.Name); name.Box.MaxLength = 100; name.Box.Font = Fonts.Get(11.5f);
            name.Placeholder = "e.g. Thesis, Spanish, Client website";
            y += Ui.S(86);
            Caption("Color", y);
            var swatches = new Canvas { Bounds = new Rectangle(Ui.S(24), y + Ui.S(26), W, Ui.S(44)) };
            swatches.Painter = (c, g) =>
            {
                int n = Palette.ProjectColors.Length, size = Ui.S(30), gap = (c.Width - n * size) / Math.Max(1, n - 1);
                for (int i = 0; i < n; i++)
                {
                    string col = Palette.ProjectColors[i];
                    var r = new Rectangle(c.Width - (i + 1) * size - i * gap, (c.Height - size) / 2, size, size);
                    bool sel = string.Equals(col, ProjectColor, StringComparison.OrdinalIgnoreCase);
                    if (sel || c.Hot("c" + i)) Ui.Stroke(g, Rectangle.Inflate(r, Ui.S(3), Ui.S(3)), sel ? Palette.Project(col) : Palette.LineStrong, size, 2f * Ui.Scale);
                    Ui.Circle(g, r, Palette.Project(col));
                    if (sel) Icons.Draw(g, Glyph.Check, Rectangle.Inflate(r, -Ui.S(6), -Ui.S(6)), Color.White, 2.4f);
                    c.AddHit(Rectangle.Inflate(r, Ui.S(3), Ui.S(3)), "c" + i, () => { ProjectColor = col; c.Invalidate(); });
                }
            };
            Controls.Add(swatches);
            y += Ui.S(90);
            Caption("Weekly goal (optional)", y);
            var goal = new Canvas { Bounds = new Rectangle(Ui.S(24), y + Ui.S(26), W, Ui.S(44)) };
            goal.Painter = (c, g) =>
            {
                string text = WeeklyGoal == 0 ? "No goal" : (WeeklyGoal / 60).ToString() + " hours per week";
                c.Stepper(g, new Rectangle(c.Width - Ui.S(220), 0, Ui.S(220), c.Height), text, "goal",
                    () => { WeeklyGoal = Math.Max(0, WeeklyGoal - 60); c.Invalidate(); }, () => { WeeklyGoal = Math.Min(100 * 60, WeeklyGoal + 60); c.Invalidate(); });
                Ui.Text(g, "Progress shows on the project card.", Fonts.Get(8.5f), Palette.Ink3, new Rectangle(0, 0, c.Width - Ui.S(236), c.Height));
            };
            Controls.Add(goal);
            y += Ui.S(90);
            if (value != null)
            {
                var archive = new Canvas { Bounds = new Rectangle(Ui.S(24), y, W, Ui.S(48)) };
                archive.Painter = (c, g) =>
                {
                    Ui.Text(g, "Archive project", Fonts.Get(10f, Weight.Medium), Palette.Ink, new Rectangle(Ui.S(60), 0, c.Width - Ui.S(60), Ui.S(26)));
                    Ui.Text(g, "Hides it from the active list; its time records are kept.", Fonts.Get(8.5f), Palette.Ink3, new Rectangle(Ui.S(60), Ui.S(24), c.Width - Ui.S(60), Ui.S(22)));
                    c.Toggle(g, new Rectangle(0, 0, Ui.S(44), c.Height), Archived, "arch", () => { Archived = !Archived; c.Invalidate(); });
                };
                Controls.Add(archive);
            }
            else ClientSize = new Size(ClientSize.Width, ClientSize.Height - Ui.S(52));
            Finish(() =>
            {
                ProjectName = name.Text.Trim();
                if (ProjectName.Length == 0) throw new ArgumentException("Enter a project name.");
            }, value == null ? "Create project" : "Save");
            if (value != null)
            {
                // Deletion is confirmed (with the amount of time at stake) by the caller.
                var delete = new Btn("Delete project", Kind.Ghost, Glyph.Trash) { Under = Palette.Surface2, Ink = () => Palette.Danger }.Fit();
                delete.Location = new Point(ClientSize.Width - Ui.S(24) - delete.Width, Ok.Top);
                delete.Click += (s, e) => { DeleteRequested = true; DialogResult = DialogResult.OK; };
                Controls.Add(delete);
            }
            Shown += (s, e) => name.Box.Focus();
        }
    }

    public sealed class NoteDialog : DialogBase
    {
        public string Value;
        public NoteDialog(string note) : base("Activity note", 460, 300, "What are you working on? Shown in the timesheet and the CSV export.")
        {
            var input = Input(Top0, 110, true, note); input.Box.MaxLength = 2000;
            Finish(() => Value = input.Text.Trim());
            Shown += (s, e) => { input.Box.Focus(); input.Box.SelectAll(); };
        }
    }

    public enum IdleChoice { Keep, DiscardContinue, DiscardStop }

    public sealed class IdleDialog : DialogBase
    {
        public IdleChoice Choice = IdleChoice.Keep;
        public IdleDialog(string project, long idleStart, long now, TimeZoneInfo zone) : base("You were away for a while", 500, 290)
        {
            string text = "No keyboard or mouse activity was seen from " + Dates.Clock(idleStart, zone) + " to " + Dates.Clock(now, zone) + " (" + Dates.Human(now - idleStart) + "), but the timer for “" + project + "” was running.\n\nShould this time count as work?";
            var body = new Canvas { Bounds = new Rectangle(Ui.S(24), Top0, W, Ui.S(120)) };
            body.Painter = (c, g) => Ui.Text(g, text, Fonts.Get(10f), Palette.Ink2, new Rectangle(0, 0, c.Width, c.Height), Align.Right, true);
            Controls.Add(body);
            Finish(() => Choice = IdleChoice.DiscardContinue, "Discard and continue", Kind.Primary, "Keep");
            Cancel.Click += (s, e) => Choice = IdleChoice.Keep;
            var stop = new Btn("Discard and stop", Kind.Ghost).Fit(); stop.Under = Palette.Surface2; stop.Location = new Point(Ok.Right + Ui.S(10), Ok.Top);
            stop.Click += (s, e) => { Choice = IdleChoice.DiscardStop; DialogResult = DialogResult.OK; };
            Controls.Add(stop);
            TopMost = true; StartPosition = FormStartPosition.CenterScreen;
        }
    }

    public sealed class SessionDialog : DialogBase
    {
        public Session Value;
        private DateTime startDay, endDay;
        private string projectId;
        public SessionDialog(AppState state, DateTime day, Session existing, TimeZoneInfo zone, Func<DateTime, bool> marked)
            : base(existing == null ? "Add time manually" : "Edit activity", 540, 650, existing == null ? "Add time you worked while the timer was off." : null)
        {
            var choices = state.Projects.Where(p => !p.Archived || (existing != null && p.Id == existing.ProjectId)).ToList();
            projectId = existing != null ? existing.ProjectId : (Engine.LastProjectId(state) ?? (choices.Count > 0 ? choices[0].Id : null));
            DateTime now = DateTime.Now;
            TimeSpan st, et;
            if (existing != null)
            {
                DateTime a = Dates.Local(existing.StartUtc, zone), b = Dates.Local(existing.EndUtc, zone);
                startDay = a.Date; endDay = b.Date; st = a.TimeOfDay; et = b.TimeOfDay;
            }
            else if (day == DateTime.Today)
            {
                var end = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute / 5 * 5, 0);
                var begin = end.AddHours(-1); startDay = begin.Date; endDay = end.Date; st = begin.TimeOfDay; et = end.TimeOfDay;
            }
            else { startDay = endDay = day; st = TimeSpan.FromHours(9); et = TimeSpan.FromHours(10); }

            int y = Top0;
            Caption("Project", y);
            int rows = Math.Max(1, Math.Min(4, (choices.Count + 1) / 2));
            var picker = new Canvas { Bounds = new Rectangle(Ui.S(24), y + Ui.S(26), W, rows * Ui.S(44) + Ui.S(4)), Scrollable = choices.Count > 8 };
            picker.Painter = (c, g) =>
            {
                int cw = (c.Width - Ui.S(18)) / 2;
                for (int i = 0; i < choices.Count; i++)
                {
                    var p = choices[i];
                    var r = new Rectangle(i % 2 == 0 ? c.Width - cw : c.Width - 2 * cw - Ui.S(8), i / 2 * Ui.S(44), cw, Ui.S(38));
                    bool sel = p.Id == projectId;
                    Ui.Fill(g, r, sel ? Palette.AccentSoft : c.Hot("p" + i) ? Palette.Surface2 : Palette.Surface, Ui.S(10));
                    Ui.Stroke(g, r, sel ? Palette.Accent : Palette.Line, Ui.S(10), sel ? 1.5f * Ui.Scale : 1f);
                    c.Swatch(g, new Rectangle(r.Right - Ui.S(24), r.Y + (r.Height - Ui.S(12)) / 2, Ui.S(12), Ui.S(12)), p.Color);
                    Ui.Text(g, p.Name, Fonts.Get(9.5f, sel ? Weight.Medium : Weight.Regular), Palette.Ink, new Rectangle(r.X + Ui.S(10), r.Y, r.Width - Ui.S(42), r.Height));
                    string id = p.Id;
                    c.AddHit(r, "p" + i, () => { projectId = id; c.Invalidate(); });
                }
                c.SetContentHeight((choices.Count + 1) / 2 * Ui.S(44));
            };
            Controls.Add(picker);
            y = picker.Bottom + Ui.S(16);

            int half = (W - Ui.S(16)) / 2;
            Caption("Start", y); Caption("End", y, Ui.S(24) + half);
            // Each half reads date then time: [start date][start time]   [end date][end time].
            var startDate = new Btn("", Kind.Outline, Glyph.Calendar) { Bounds = new Rectangle(ClientSize.Width - Ui.S(24) - (half - Ui.S(96)), y + Ui.S(26), half - Ui.S(96), Ui.S(42)) };
            var startTime = Input(y + Ui.S(26), 42, false, st.ToString(@"hh\:mm"), ClientSize.Width - Ui.S(24) - half, Ui.S(88));
            var endDate = new Btn("", Kind.Outline, Glyph.Calendar) { Bounds = new Rectangle(Ui.S(24) + Ui.S(96), y + Ui.S(26), half - Ui.S(96), Ui.S(42)) };
            var endTime = Input(y + Ui.S(26), 42, false, et.ToString(@"hh\:mm"), Ui.S(24), Ui.S(88));
            foreach (var t in new[] { startTime, endTime }) { t.Box.TextAlign = HorizontalAlignment.Center; t.Box.RightToLeft = RightToLeft.No; t.Box.Font = Fonts.Get(11f, Weight.Medium); }
            if (existing != null && (st.Seconds != 0 || et.Seconds != 0)) { startTime.Text = st.ToString(@"hh\:mm\:ss"); endTime.Text = et.ToString(@"hh\:mm\:ss"); }
            Controls.Add(startDate); Controls.Add(endDate);
            y += Ui.S(84);

            var summary = new Canvas { Bounds = new Rectangle(Ui.S(24), y, W, Ui.S(44)) };
            Func<string> describe = () =>
            {
                try
                {
                    DateTime from = startDay.Add(Dates.ParseTime(startTime.Text)), to = endDay.Add(Dates.ParseTime(endTime.Text));
                    if (to <= from) return "!The end time must be after the start.";
                    string text = "Duration: " + Dates.Human((to - from).Ticks);
                    if (to.Date != from.Date) text += "  •  crosses midnight and is split across days";
                    return text;
                }
                catch (Exception e) { return "!" + e.Message; }
            };
            summary.Painter = (c, g) =>
            {
                string text = describe(); bool bad = text.StartsWith("!");
                Ui.Fill(g, new Rectangle(0, 0, c.Width, c.Height), bad ? Palette.DangerSoft : Palette.AccentSoft, Ui.S(10));
                Icons.Draw(g, bad ? Glyph.Close : Glyph.Clock, new RectangleF(c.Width - Ui.S(34), (c.Height - Ui.S(18)) / 2f, Ui.S(18), Ui.S(18)), bad ? Palette.Danger : Palette.Accent);
                Ui.Text(g, bad ? text.Substring(1) : text, Fonts.Get(9.5f, Weight.Medium), bad ? Palette.Danger : (Palette.Dark ? Palette.AccentHover : Palette.Accent), new Rectangle(Ui.S(12), 0, c.Width - Ui.S(54), c.Height));
            };
            Controls.Add(summary);
            Action refresh = () =>
            {
                startDate.Text = Dates.Short(startDay); endDate.Text = Dates.Short(endDay); summary.Invalidate();
            };
            startDate.Click += (s, e) =>
            {
                using (var d = new DateDialog(startDay, marked, "Start date")) if (d.ShowDialog(this) == DialogResult.OK)
                {
                    var shift = endDay - startDay; startDay = d.Value; endDay = startDay + (shift.Ticks < 0 ? TimeSpan.Zero : shift); refresh();
                }
            };
            endDate.Click += (s, e) => { using (var d = new DateDialog(endDay, marked, "End date")) if (d.ShowDialog(this) == DialogResult.OK) { endDay = d.Value; refresh(); } };
            startTime.Box.TextChanged += (s, e) => summary.Invalidate();
            endTime.Box.TextChanged += (s, e) =>
            {
                // Typing an end time earlier than the start on the same day means "after midnight".
                try { if (endDay == startDay && Dates.ParseTime(endTime.Text) < Dates.ParseTime(startTime.Text)) { endDay = startDay.AddDays(1); refresh(); } } catch { }
                summary.Invalidate();
            };
            refresh();
            y += Ui.S(60);
            Caption("Note (optional)", y);
            var note = Input(y + Ui.S(26), 84, true, existing == null ? "" : existing.Note); note.Box.MaxLength = 2000;
            y = note.Bottom + Ui.S(16);
            ClientSize = new Size(ClientSize.Width, y + Ui.S(72));
            Finish(() =>
            {
                if (projectId == null) throw new ArgumentException("Create a project first.");
                DateTime from = startDay.Add(Dates.ParseTime(startTime.Text)), to = endDay.Add(Dates.ParseTime(endTime.Text));
                if (to <= from) throw new ArgumentException("The end time must be after the start.");
                Value = new Session { Id = existing == null ? null : existing.Id, ProjectId = projectId, StartUtc = Dates.Utc(from, zone), EndUtc = Dates.Utc(to, zone), Note = note.Text.Trim(), Manual = true };
                var copy = Store.Parse(Store.Serializer().Serialize(state)); Engine.SaveSession(copy, Value, DateTime.UtcNow.Ticks);
            });
        }
    }
}
