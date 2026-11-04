using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Rooznegar
{
    public sealed class MainForm : Form, IMessageFilter
    {
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInput info);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
        [StructLayout(LayoutKind.Sequential)] private struct LastInput { public uint Size; public uint Time; }

        public AppState State;
        public Snapshot Snap;
        public TimeZoneInfo Zone = TimeZoneInfo.Local;
        public string PageKey = "dashboard";
        public DateTime SelectedDay = DateTime.Today;
        public DateTime RangeFrom = Dates.WeekStart(DateTime.Today), RangeEnd = DateTime.Today.AddDays(1);
        public string RangeKey = "week";
        public string Search = "";
        public bool ShowArchived;
        public long FocusAnchor;
        public string HotkeyStatus = "Off";

        private readonly Store store;
        private readonly bool preview;
        private readonly Panel host;
        private readonly Sidebar sidebar;
        private readonly Toast toast;
        private Page page;
        private Timer ticker;
        private NotifyIcon tray;
        private IntPtr trayIconHandle = IntPtr.Zero;
        private string trayIconKey = "";
        private MiniWidget mini;
        private bool exiting, hiddenNotice, hotkeyRegistered, idleDialogOpen, awaitingResume;
        private long lastSave, lastZoneCheck, lastTick, idleStart, ignoreIdleBefore;
        private DateTime lastDay = DateTime.Today;

        public MainForm(Store storage, AppState data, bool previewMode = false)
        {
            store = storage; State = data; preview = previewMode;
            ApplyThemeSetting();
            Text = "Rooznegar";
            AutoScaleMode = AutoScaleMode.None;
            var workArea = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(Ui.S(1320), workArea.Width - Ui.S(40)), Math.Min(Ui.S(860), workArea.Height - Ui.S(56)));
            MinimumSize = new Size(Math.Min(Ui.S(1080), workArea.Width), Math.Min(Ui.S(700), workArea.Height));
            StartPosition = FormStartPosition.CenterScreen; Font = Fonts.Get(10f); BackColor = Palette.Bg;
            DoubleBuffered = true; KeyPreview = true; RightToLeft = RightToLeft.No;
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Rooznegar.ico");
            try { if (File.Exists(iconPath)) Icon = new Icon(iconPath); else Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch (Exception) { Icon = SystemIcons.Application; }

            Snap = Snapshot.Build(State, Now, Zone);
            if (State.Active != null) FocusAnchor = State.Active.StartUtc;
            // Remember the last checkpoint of a timer left over from a previous run before any autosave touches it.
            long leftoverCheckpoint = State.Active == null ? 0 : Math.Max(State.Active.StartUtc, State.Active.LastSeenUtc);
            awaitingResume = !preview && State.Active != null;
            host = new Panel { Dock = DockStyle.Fill, BackColor = Palette.Bg };
            toast = new Toast(); host.Controls.Add(toast);
            sidebar = new Sidebar(this);
            Controls.Add(host); Controls.Add(sidebar);
            BuildTray(); Navigate("dashboard");
            lastTick = Now;
            ticker = new Timer { Interval = 1000 }; ticker.Tick += (s, e) => Tick(); ticker.Start();
            Shown += (s, e) =>
            {
                if (!preview && !string.IsNullOrEmpty(store.RecoveryMessage)) Dialogs.Message(this, "Data recovery", store.RecoveryMessage);
                if (!preview && State.Active != null)
                {
                    bool resume = Dialogs.Confirm(this, "A timer was left running",
                        "“Keep tracking” records all the time since it started.\n“Cancel” ends it at the last saved moment.", "Keep tracking");
                    awaitingResume = false;
                    if (!resume && State.Active != null) Mutate(() => Engine.Stop(State, Math.Max(State.Active.StartUtc, leftoverCheckpoint)));
                }
                awaitingResume = false;
                if (!preview && Environment.GetCommandLineArgs().Contains("--background")) HideToTray();
                if (State.Settings.MiniWidget) ShowMini(true);
            };
            FormClosing += HandleClosing;
            if (!preview)
            {
                SystemEvents.SessionEnding += SessionEnding;
                SystemEvents.UserPreferenceChanged += PreferenceChanged;
                Application.AddMessageFilter(this);
            }
        }
        public long Now { get { return DateTime.UtcNow.Ticks; } }

        // ------------------------------------------------------------------ navigation & rendering
        public void Navigate(string key)
        {
            PageKey = key;
            var old = page;
            host.SuspendLayout();
            page = key == "timesheet" ? new TimesheetPage(this) : key == "reports" ? new ReportsPage(this) : key == "settings" ? (Page)new SettingsPage(this) : new DashboardPage(this);
            host.Controls.Add(page); page.BringToFront(); toast.BringToFront();
            if (old != null) { host.Controls.Remove(old); old.Dispose(); }
            host.ResumeLayout();
            sidebar.Invalidate();
        }
        private void RefreshAll()
        {
            Snap = Snapshot.Build(State, Now, Zone);
            if (page != null) page.Tick();
            sidebar.Invalidate();
            if (mini != null && mini.Visible) mini.Invalidate(true);
        }
        private bool tickErrorShown;
        private void Tick()
        {
            try { TickCore(); }
            catch (Exception e)
            {
                ErrorLog.Write("Tick", e);
                if (!tickErrorShown && !preview) { tickErrorShown = true; Dialogs.Error(this, e); }
                if (preview) throw;
            }
        }
        private void TickCore()
        {
            long now = Now, gap = now - lastTick; lastTick = now;
            if (now - lastZoneCheck > TimeSpan.TicksPerMinute)
            {
                lastZoneCheck = now;
                TimeZoneInfo.ClearCachedData(); var currentZone = TimeZoneInfo.Local;
                if (currentZone.Id != Zone.Id || currentZone.GetUtcOffset(DateTime.UtcNow) != Zone.GetUtcOffset(DateTime.UtcNow)) Zone = currentZone;
            }
            if (DateTime.Today != lastDay)
            {
                if (SelectedDay == lastDay) SelectedDay = DateTime.Today;
                if (RangeKey != "custom") SetRange(RangeKey, false);
                lastDay = DateTime.Today;
            }
            RefreshAll();
            UpdateTray();
            CheckIdle(gap);
            CheckFocus();
            if (!preview && !awaitingResume && now - lastSave >= TimeSpan.TicksPerSecond * 15)
            {
                try { Save(); }
                catch (Exception e) { ticker.Stop(); Dialogs.Error(this, new IOException("Saving failed. Check the data folder and free disk space.\n" + e.Message)); ticker.Start(); lastSave = Now; }
            }
        }
        private void Mutate(Action action)
        {
            string before = Store.Serializer().Serialize(State);
            try { action(); Save(); RefreshAll(); BuildTrayMenu(); UpdateTray(); }
            catch (Exception e) { State = Store.Parse(before); if (preview) throw; Dialogs.Error(this, e); RefreshAll(); }
        }
        private void Save()
        {
            if (State.Active != null) State.Active.LastSeenUtc = Math.Max(State.Active.StartUtc, Now);
            if (!preview || store != null) store.Save(State);
            lastSave = Now;
        }
        public void ShowToast(string text, string action = null, Action act = null) { toast.Show(text, action, act); }

        // ------------------------------------------------------------------ timer actions
        public void Start(string id)
        {
            bool switching = State.Active != null && State.Active.ProjectId != id;
            Mutate(() => Engine.Start(State, id, Now));
            if (State.Active != null && State.Active.ProjectId == id && (switching || Now - State.Active.StartUtc < TimeSpan.TicksPerSecond * 2)) FocusAnchor = State.Active.StartUtc;
        }
        public void Stop() { Mutate(() => Engine.Stop(State, Now)); }
        public void ResumeLast()
        {
            string id = Engine.LastProjectId(State);
            if (id == null) { Navigate("dashboard"); ShowToast("Create a project first."); return; }
            var last = State.Sessions.Where(a => a.ProjectId == id).OrderByDescending(a => a.EndUtc).FirstOrDefault();
            Start(id);
            if (State.Active != null && last != null && string.IsNullOrEmpty(State.Active.Note) && !string.IsNullOrEmpty(last.Note)) Mutate(() => State.Active.Note = last.Note);
        }
        public void ToggleTimer() { if (State.Active != null) Stop(); else ResumeLast(); }
        public void ContinueSession(Session s)
        {
            Start(s.ProjectId);
            if (State.Active != null && State.Active.ProjectId == s.ProjectId) Mutate(() => State.Active.Note = s.Note ?? "");
        }
        public void EditActiveNote()
        {
            if (State.Active == null) return;
            using (var d = new NoteDialog(State.Active.Note)) if (d.ShowDialog(this) == DialogResult.OK && State.Active != null) Mutate(() => State.Active.Note = d.Value);
        }
        public void EditProject(Project p)
        {
            using (var d = new ProjectDialog(p))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (d.DeleteRequested && p != null) { DeleteProject(p.Id); return; }
                Mutate(() =>
                {
                    if (State.Projects.Any(a => a.Id != (p == null ? "" : p.Id) && string.Equals(a.Name, d.ProjectName, StringComparison.CurrentCultureIgnoreCase))) throw new ArgumentException("A project with this name already exists.");
                    if (p == null) State.Projects.Add(new Project { Id = Guid.NewGuid().ToString("N"), Name = d.ProjectName, Color = d.ProjectColor, WeeklyGoalMinutes = d.WeeklyGoal });
                    else
                    {
                        if (d.Archived && State.Active != null && State.Active.ProjectId == p.Id) Engine.Stop(State, Now);
                        p.Name = d.ProjectName; p.Color = d.ProjectColor; p.Archived = d.Archived; p.WeeklyGoalMinutes = d.WeeklyGoal;
                    }
                });
            }
        }
        public void DeleteProject(string id, bool confirm = true)
        {
            var p = State.Projects.FirstOrDefault(a => a.Id == id); if (p == null) return;
            int count = State.Sessions.Count(a => a.ProjectId == id) + (State.Active != null && State.Active.ProjectId == id ? 1 : 0);
            long total = Snapshot.Get(Snap.TotalBy, id);
            string detail = count == 0 ? "This project has no activities yet." :
                DeleteDetail(count, total);
            if (State.Active != null && State.Active.ProjectId == id) detail += "\nThis project's timer is running and will be stopped.";
            if (confirm && !Dialogs.Confirm(this, "Delete “" + p.Name + "”?", detail + "\n\nIf you only want to hide it from the list, archive it instead.", "Delete project", true)) return;
            var copy = new Project { Id = p.Id, Name = p.Name, Color = p.Color, Archived = p.Archived, WeeklyGoalMinutes = p.WeeklyGoalMinutes };
            List<Session> removed = null;
            Mutate(() => removed = Engine.DeleteProject(State, id, Now));
            if (removed == null || State.Projects.Any(a => a.Id == id)) return;
            ShowToast("Project “" + copy.Name + "” deleted.", "Undo", () =>
            {
                int skipped = 0;
                Mutate(() => skipped = Engine.RestoreProject(State, copy, removed));
                ShowToast(skipped == 0 ? "The project and all its activities are back." : "The project is back; " + Activities(skipped) + " overlapping newer entries could not be restored.");
            });
        }
        private static string Activities(int n) { return n == 1 ? "1 activity" : n + " activities"; }
        private static string DeleteDetail(int count, long total)
        {
            return Activities(count) + " totalling " + Dates.Human(total) + " will be deleted and removed from all reports.";
        }
        private Func<DateTime, bool> Marked { get { return day => Snap.Day(day) > 0; } }
        public void EditSession(Session existing)
        {
            if (!State.Projects.Any(p => !p.Archived) && existing == null) { Dialogs.Message(this, "No projects yet", "Create a project first."); return; }
            using (var d = new SessionDialog(State, SelectedDay, existing, Zone, Marked)) if (d.ShowDialog(this) == DialogResult.OK)
            {
                Mutate(() => Engine.SaveSession(State, d.Value, Now));
                ShowToast(existing == null ? "Activity added." : "Activity updated.");
            }
        }
        public void DeleteSession(string id)
        {
            var s = State.Sessions.FirstOrDefault(a => a.Id == id); if (s == null) return;
            var copy = new Session { Id = s.Id, ProjectId = s.ProjectId, StartUtc = s.StartUtc, EndUtc = s.EndUtc, Note = s.Note, Manual = s.Manual };
            Mutate(() => State.Sessions.Remove(s));
            ShowToast("Activity deleted" + (Dates.Local(copy.StartUtc, Zone).Date != Dates.Local(copy.EndUtc, Zone).Date ? " (all of its days)" : "") + ".", "Undo",
                () => Mutate(() => Engine.SaveSession(State, copy, Now)));
        }
        public void SetDay(DateTime day) { SelectedDay = day.Date; RefreshAll(); }
        public void ChooseDay() { using (var d = new DateDialog(SelectedDay, Marked)) if (d.ShowDialog(this) == DialogResult.OK) SetDay(d.Value); }
        public void SetRange(string key, bool refresh = true)
        {
            DateTime today = DateTime.Today, ws = Dates.WeekStart(today), ms = Dates.MonthStart(today);
            switch (key)
            {
                case "lastweek": RangeFrom = ws.AddDays(-7); RangeEnd = ws; break;
                case "month": RangeFrom = ms; RangeEnd = today.AddDays(1); break;
                case "lastmonth": RangeFrom = Dates.MonthStart(ms.AddDays(-1)); RangeEnd = ms; break;
                case "year": RangeFrom = Dates.YearStart(today); RangeEnd = today.AddDays(1); break;
                case "all": RangeFrom = Snap.Slices.Select(t => t.LocalDay).DefaultIfEmpty(today).Min(); RangeEnd = today.AddDays(1); break;
                default: key = "week"; RangeFrom = ws; RangeEnd = today.AddDays(1); break;
            }
            RangeKey = key;
            if (refresh) RefreshAll();
        }
        public void ChooseRange()
        {
            using (var d = new RangeDialog(RangeFrom, RangeEnd)) if (d.ShowDialog(this) == DialogResult.OK) { RangeFrom = d.From; RangeEnd = d.Until; RangeKey = "custom"; RefreshAll(); }
        }
        public void ExportCsv(DateTime from, DateTime until)
        {
            using (var d = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "Rooznegar-" + DateTime.Now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) + ".csv" })
                if (d.ShowDialog(this) == DialogResult.OK)
                    try { File.WriteAllText(d.FileName, Engine.Csv(State, Now, Zone, from, until), new UTF8Encoding(true)); ShowToast("CSV report saved; it opens in Excel.", "Open", () => OpenPath(d.FileName)); }
                    catch (Exception e) { Dialogs.Error(this, e); }
        }
        public void Backup()
        {
            using (var d = new SaveFileDialog { Filter = "Rooznegar backup (*.json)|*.json", FileName = "Rooznegar-Backup-" + DateTime.Now.ToString("yyyyMMdd-HHmm", System.Globalization.CultureInfo.InvariantCulture) + ".json" })
                if (d.ShowDialog(this) == DialogResult.OK) try { Save(); File.Copy(store.FilePath, d.FileName, true); ShowToast("Backup saved."); } catch (Exception e) { Dialogs.Error(this, e); }
        }
        public void ImportBackup()
        {
            using (var d = new OpenFileDialog { Filter = "Rooznegar backup (*.json)|*.json" })
                if (d.ShowDialog(this) == DialogResult.OK) try
                {
                    if (new FileInfo(d.FileName).Length > 64 * 1024 * 1024) throw new ArgumentException("The backup file is too large.");
                    var incoming = Store.Parse(File.ReadAllText(d.FileName, Encoding.UTF8));
                    if (!Dialogs.Confirm(this, "Restore backup", "This file will replace all current projects and times.\nA full copy of your current data is kept before restoring.\nA timer running in the backup is stopped at its last saved time.", "Restore", true)) return;
                    Save(); File.Copy(store.FilePath, Path.Combine(store.DirectoryPath, "before-restore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".json"), true);
                    if (incoming.Active != null) Engine.Stop(incoming, incoming.Active.LastSeenUtc);
                    Mutate(() => State = incoming);
                    ApplyThemeSetting(); Navigate(PageKey); ShowToast("Backup restored.");
                } catch (Exception e) { Dialogs.Error(this, e); }
        }
        public void OpenDataFolder() { OpenPath(store.DirectoryPath); }
        private void OpenPath(string path) { try { Process.Start(path); } catch (Exception e) { Dialogs.Error(this, e); } }

        // ------------------------------------------------------------------ settings
        public void Change(Action<Settings> change)
        {
            Mutate(() => change(State.Settings));
            UpdateHotkey();
        }
        public void SetTheme(string theme)
        {
            if (State.Settings.Theme == theme) return;
            Mutate(() => State.Settings.Theme = theme);
            ApplyThemeSetting(); Retheme();
        }
        private void ApplyThemeSetting()
        {
            string t = State.Settings.Theme;
            Palette.Apply(t == "dark" || (t == "system" && Palette.SystemPrefersDark()));
        }
        private void Retheme()
        {
            BackColor = Palette.Bg; host.BackColor = Palette.Bg; Native.TitleBar(this);
            Navigate(PageKey); BuildTrayMenu(); trayIconKey = ""; UpdateTray();
            if (mini != null) mini.Invalidate(true);
            Invalidate(true);
        }
        private void PreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (State.Settings.Theme != "system") return;
            bool dark = Palette.SystemPrefersDark();
            if (dark != Palette.Dark) BeginInvoke(new Action(() => { ApplyThemeSetting(); Retheme(); }));
        }
        public bool StartupEnabled()
        {
            if (preview) return State.Settings.StartWithWindows;
            try { using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) return key != null && key.GetValue("Rooznegar") != null; } catch { return false; }
        }
        public void SetStartup(bool wanted)
        {
            try
            {
                if (!preview)
                    using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                    { if (wanted) key.SetValue("Rooznegar", "\"" + Application.ExecutablePath + "\" --background"); else key.DeleteValue("Rooznegar", false); }
                Mutate(() => State.Settings.StartWithWindows = wanted);
            }
            catch (Exception error) { Dialogs.Error(this, error); }
        }
        public void ToggleMini()
        {
            bool on = !State.Settings.MiniWidget;
            Mutate(() => State.Settings.MiniWidget = on);
            ShowMini(on);
        }
        private void ShowMini(bool on)
        {
            if (on)
            {
                if (mini == null || mini.IsDisposed) mini = new MiniWidget(this);
                mini.Place(); if (!mini.Visible) mini.Show();
            }
            else if (mini != null && mini.Visible) mini.Hide();
        }
        public void SaveMiniPosition(Point p)
        {
            State.Settings.MiniX = p.X; State.Settings.MiniY = p.Y;
            try { Save(); } catch { }
        }

        // ------------------------------------------------------------------ hotkey, idle, focus
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.TitleBar(this);
            UpdateHotkey();
        }
        private void UpdateHotkey()
        {
            if (!IsHandleCreated) return;
            if (Ui.IsMono || preview) { HotkeyStatus = State.Settings.GlobalHotkey ? "On" : "Off"; return; }
            if (hotkeyRegistered) { UnregisterHotKey(Handle, 1); hotkeyRegistered = false; }
            if (!State.Settings.GlobalHotkey) { HotkeyStatus = "Off"; return; }
            hotkeyRegistered = RegisterHotKey(Handle, 1, 0x0002 | 0x0001 | 0x4000, 0x20);
            HotkeyStatus = hotkeyRegistered ? "On: start or stop the timer from any app" : "Another app is already using these keys";
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == 1)
            {
                ToggleTimer();
                var p = State.Active == null ? null : State.Projects.FirstOrDefault(a => a.Id == State.Active.ProjectId);
                if (tray != null && !Visible) tray.ShowBalloonTip(1500, "Rooznegar", p == null ? "Tracking stopped." : "Started “" + p.Name + "”.", ToolTipIcon.None);
                return;
            }
            base.WndProc(ref m);
        }
        private long IdleTicks()
        {
            if (Ui.IsMono) return 0;
            try
            {
                var info = new LastInput { Size = (uint)Marshal.SizeOf(typeof(LastInput)) };
                if (!GetLastInputInfo(ref info)) return 0;
                uint ms = unchecked((uint)Environment.TickCount - info.Time);
                return ms * TimeSpan.TicksPerMillisecond;
            }
            catch { return 0; }
        }
        private void CheckIdle(long gap)
        {
            int minutes = State.Settings.IdleMinutes;
            if (preview || awaitingResume || State.Active == null || minutes <= 0 || idleDialogOpen) { if (State.Active == null) idleStart = 0; return; }
            long threshold = minutes * TimeSpan.TicksPerMinute, now = Now;
            // A long gap between ticks means the computer was asleep or hibernating. By default that time
            // counts as work (as in 1.0), so idleness is only measured from the moment it woke up.
            if (gap > TimeSpan.TicksPerSecond * 30)
            {
                if (State.Settings.AskAfterSleep) { if (idleStart == 0 && gap >= threshold) idleStart = now - gap; }
                else ignoreIdleBefore = now;
            }
            long idle = IdleTicks();
            long candidate = Math.Max(now - idle, ignoreIdleBefore);
            if (idle > 0 && idleStart == 0 && now - candidate >= threshold) idleStart = candidate;
            if (idleStart != 0 && idle < TimeSpan.TicksPerSecond * 3)
            {
                long from = Math.Max(idleStart, State.Active.StartUtc); idleStart = 0;
                if (now - from < threshold) return;
                var p = State.Projects.FirstOrDefault(a => a.Id == State.Active.ProjectId);
                idleDialogOpen = true;
                try
                {
                    using (var d = new IdleDialog(p == null ? "" : p.Name, from, now, Zone))
                    {
                        if (d.ShowDialog(Visible ? this : null) == DialogResult.OK && d.Choice != IdleChoice.Keep)
                        {
                            bool keep = d.Choice == IdleChoice.DiscardContinue;
                            Mutate(() => Engine.DiscardIdle(State, from, Now, keep));
                            if (keep) FocusAnchor = Now;
                            ShowToast(Dates.Human(now - from) + " of idle time removed.");
                        }
                    }
                }
                finally { idleDialogOpen = false; }
            }
        }
        private void CheckFocus()
        {
            int focus = State.Settings.FocusMinutes;
            if (State.Active == null || focus <= 0 || preview || awaitingResume) return;
            if (Now - FocusAnchor >= focus * TimeSpan.TicksPerMinute)
            {
                FocusAnchor = Now;
                try { SystemSounds.Asterisk.Play(); } catch { }
                if (tray != null) tray.ShowBalloonTip(6000, "Time for a break", "You have focused for " + Dates.Count(focus, "minute") + ". Take a " + State.Settings.BreakMinutes + "-minute break; your eyes and body will thank you.", ToolTipIcon.Info);
                ShowToast("Time for a break — " + Dates.Count(State.Settings.BreakMinutes, "minute"));
            }
        }

        // ------------------------------------------------------------------ keyboard & mouse wheel
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.D1: Navigate("dashboard"); return true;
                case Keys.Control | Keys.D2: Navigate("timesheet"); return true;
                case Keys.Control | Keys.D3: Navigate("reports"); return true;
                case Keys.Control | Keys.D4: Navigate("settings"); return true;
                case Keys.Control | Keys.N: EditProject(null); return true;
                case Keys.Control | Keys.M: EditSession(null); return true;
                case Keys.Control | Keys.T: SetTheme(Palette.Dark ? "light" : "dark"); return true;
                case Keys.Control | Keys.F:
                    if (PageKey != "dashboard") Navigate("dashboard");
                    var dash = page as DashboardPage; if (dash != null) dash.FocusSearch();
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != 0x020A || !Visible || Form.ActiveForm != this) return false;
            Control c = this; Point screen = Cursor.Position;
            while (true)
            {
                var child = c.GetChildAtPoint(c.PointToClient(screen), GetChildAtPointSkip.Invisible);
                if (child == null) break; c = child;
            }
            for (var x = c; x != null; x = x.Parent)
            {
                var s = x as Surface;
                if (s != null && s.CanScroll) { s.Wheel((short)((m.WParam.ToInt64() >> 16) & 0xFFFF)); return true; }
            }
            return false;
        }

        // ------------------------------------------------------------------ tray
        private void BuildTray()
        {
            tray = new NotifyIcon { Icon = Icon ?? SystemIcons.Application, Visible = !preview, Text = "Rooznegar" };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowFromTray(); };
            BuildTrayMenu(); UpdateTray();
        }
        private sealed class MenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Palette.Surface; } }
            public override Color ImageMarginGradientBegin { get { return Palette.Surface; } }
            public override Color ImageMarginGradientMiddle { get { return Palette.Surface; } }
            public override Color ImageMarginGradientEnd { get { return Palette.Surface; } }
            public override Color MenuBorder { get { return Palette.LineStrong; } }
            public override Color MenuItemBorder { get { return Palette.AccentSoft; } }
            public override Color MenuItemSelected { get { return Palette.AccentSoft; } }
            public override Color SeparatorDark { get { return Palette.Line; } }
            public override Color SeparatorLight { get { return Palette.Line; } }
        }
        private sealed class MenuRenderer : ToolStripProfessionalRenderer
        {
            public MenuRenderer() : base(new MenuColors()) { RoundedEdges = true; }
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = e.Item.Enabled ? Palette.Ink : Palette.Ink3; base.OnRenderItemText(e); }
            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = Palette.Ink2; base.OnRenderArrow(e); }
        }
        private static Image Dot(string color)
        {
            var b = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(b)) { g.SmoothingMode = SmoothingMode.AntiAlias; using (var br = new SolidBrush(Palette.Project(color))) g.FillEllipse(br, 3, 3, 10, 10); }
            return b;
        }
        private void BuildTrayMenu()
        {
            if (tray == null) return; var old = tray.ContextMenuStrip;
            var menu = new ContextMenuStrip { RightToLeft = Ui.Direction, Font = Fonts.Get(9.5f), Renderer = new MenuRenderer(), ShowImageMargin = true };
            var active = State.Active == null ? null : State.Projects.FirstOrDefault(a => a.Id == State.Active.ProjectId);
            var status = new ToolStripMenuItem(active == null ? "Not tracking" : "Tracking: " + active.Name) { Enabled = false };
            if (active != null) status.Image = Dot(active.Color);
            menu.Items.Add(status);
            menu.Items.Add(new ToolStripSeparator());
            if (State.Active != null) menu.Items.Add("Stop tracking", null, (s, e) => Stop());
            else
            {
                string last = Engine.LastProjectId(State); var lp = State.Projects.FirstOrDefault(p => p.Id == last);
                if (lp != null) menu.Items.Add("Continue “" + lp.Name + "”", Dot(lp.Color), (s, e) => ResumeLast());
            }
            var projects = new ToolStripMenuItem("Start / switch project");
            foreach (var p in State.Projects.Where(a => !a.Archived)) { string id = p.Id; var item = projects.DropDownItems.Add(p.Name, Dot(p.Color), (s, e) => Start(id)); if (State.Active != null && State.Active.ProjectId == id) ((ToolStripMenuItem)item).Checked = true; }
            if (projects.DropDownItems.Count > 0) menu.Items.Add(projects);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(State.Settings.MiniWidget ? "Close floating timer" : "Show floating timer", null, (s, e) => ToggleMini());
            menu.Items.Add("Open Rooznegar", null, (s, e) => ShowFromTray());
            menu.Items.Add("Quit", null, (s, e) => ExitApp());
            tray.ContextMenuStrip = menu;
            if (old != null)
            {
                foreach (var item in old.Items.OfType<ToolStripMenuItem>().SelectMany(i => new[] { i }.Concat(i.DropDownItems.OfType<ToolStripMenuItem>())).ToList())
                    if (item.Image != null) { item.Image.Dispose(); item.Image = null; }
                old.Dispose();
            }
        }
        private void UpdateTray()
        {
            if (tray == null) return;
            var a = State.Active;
            var p = a == null ? null : State.Projects.FirstOrDefault(x => x.Id == a.ProjectId);
            string text = p == null ? "Rooznegar — not tracking" : "Rooznegar: " + p.Name + " — " + Dates.Hm(Now - a.StartUtc);
            tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
            string key = (p == null ? "idle" : p.Color) + Palette.Dark;
            if (key == trayIconKey) return;
            trayIconKey = key;
            try
            {
                using (var bmp = TrayBitmap(p == null ? (Color?)null : Palette.Project(p.Color)))
                {
                    IntPtr h = bmp.GetHicon();
                    var icon = Icon.FromHandle(h);
                    tray.Icon = icon;
                    if (trayIconHandle != IntPtr.Zero && !Ui.IsMono) DestroyIcon(trayIconHandle);
                    trayIconHandle = h;
                }
            }
            catch { }
        }
        /// <summary>Tray icon: the app mark, plus a dot in the active project's colour while tracking.</summary>
        private static Bitmap TrayBitmap(Color? active)
        {
            int s = Math.Max(16, (int)(16 * Ui.Scale)) * 2;
            var bmp = new Bitmap(s, s);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var r = new RectangleF(s * 0.03f, s * 0.03f, s * 0.94f, s * 0.94f);
                using (var path = Ui.Round(r, s * 0.24f)) using (var b = new SolidBrush(Palette.Hex("#0F766E"))) g.FillPath(b, path);
                var state = g.Save();
                Icons.Draw(g, Glyph.Logo, new RectangleF(s * 0.16f, s * 0.16f, s * 0.68f, s * 0.68f), Color.White, 2.6f * s / 32f / Ui.Scale);
                g.Restore(state);
                if (active.HasValue)
                {
                    float d = s * 0.46f;
                    using (var ring = new SolidBrush(Color.White)) g.FillEllipse(ring, s - d - 0.5f, s - d - 0.5f, d, d);
                    using (var dot = new SolidBrush(active.Value)) g.FillEllipse(dot, s - d + s * 0.06f, s - d + s * 0.06f, d - s * 0.12f - 1, d - s * 0.12f - 1);
                }
            }
            return bmp;
        }
        public void ShowFromTray() { Show(); if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal; Activate(); }
        private void HideToTray()
        {
            Hide();
            if (!hiddenNotice) { tray.ShowBalloonTip(3500, "Rooznegar is still running", "The timer keeps going. Click the Rooznegar icon next to the Windows clock to open it.", ToolTipIcon.Info); hiddenNotice = true; }
        }
        private void HandleClosing(object sender, FormClosingEventArgs e)
        {
            if (preview) return;
            if (!exiting && e.CloseReason != CloseReason.WindowsShutDown && e.CloseReason != CloseReason.ApplicationExitCall) { e.Cancel = true; HideToTray(); return; }
            try { if (State.Active != null) Engine.Stop(State, Now); Save(); }
            catch (Exception error) { if (e.CloseReason != CloseReason.WindowsShutDown) { e.Cancel = true; exiting = false; Dialogs.Error(this, error); } }
        }
        public void ExitApp()
        {
            if (State.Active != null && !Dialogs.Confirm(this, "Quit", "Stop the running timer and quit Rooznegar?", "Stop and quit", true)) return;
            exiting = true; if (mini != null) mini.Hide(); Close();
        }
        private void SessionEnding(object sender, SessionEndingEventArgs e)
        {
            try { if (InvokeRequired) Invoke(new Action(() => { Engine.Stop(State, Now); Save(); exiting = true; })); else { Engine.Stop(State, Now); Save(); exiting = true; } } catch { }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (ticker != null) ticker.Dispose();
                if (tray != null) { tray.Visible = false; tray.Dispose(); }
                if (mini != null) mini.Dispose();
                if (hotkeyRegistered && IsHandleCreated) UnregisterHotKey(Handle, 1);
                if (!preview) { SystemEvents.SessionEnding -= SessionEnding; SystemEvents.UserPreferenceChanged -= PreferenceChanged; Application.RemoveMessageFilter(this); }
            }
            base.Dispose(disposing);
        }

        // ------------------------------------------------------------------ previews & smoke test
        private void Shot(Control c, string file)
        {
            Application.DoEvents(); c.Refresh(); Application.DoEvents();
            var r = c.RectangleToScreen(c.ClientRectangle);
            using (var b = new Bitmap(r.Width, r.Height))
            {
                using (var g = Graphics.FromImage(b)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                b.Save(file);
            }
        }
        internal void CapturePreviews(string directory)
        {
            Directory.CreateDirectory(directory);
            foreach (string theme in new[] { "light", "dark" })
            {
                State.Settings.Theme = theme; ApplyThemeSetting(); Retheme();
                foreach (string key in new[] { "dashboard", "timesheet", "reports", "settings" })
                {
                    Navigate(key); RefreshAll();
                    Shot(this, Path.Combine(directory, theme + "-" + key + ".png"));
                    Console.WriteLine(theme + " " + key + " ok");
                }
            }
            State.Settings.Theme = "light"; ApplyThemeSetting(); Retheme(); Navigate("dashboard");
            var shots = new List<KeyValuePair<string, Form>> {
                new KeyValuePair<string, Form>("dialog-session", new SessionDialog(State, DateTime.Today, null, Zone, Marked)),
                new KeyValuePair<string, Form>("dialog-project", new ProjectDialog(State.Projects[1])),
                new KeyValuePair<string, Form>("dialog-delete", new AskDialog("Delete “" + State.Projects[1].Name + "”?", DeleteDetail(State.Sessions.Count(a => a.ProjectId == State.Projects[1].Id), Snapshot.Get(Snap.TotalBy, State.Projects[1].Id)) + "\n\nIf you only want to hide it from the list, archive it instead.", "Delete project", Kind.Danger, "Cancel")),
                new KeyValuePair<string, Form>("dialog-range", new RangeDialog(RangeFrom, RangeEnd)),
                new KeyValuePair<string, Form>("dialog-idle", new IdleDialog(State.Projects[0].Name, Now - TimeSpan.TicksPerMinute * 34, Now, Zone)) };
            foreach (var s in shots)
            {
                s.Value.StartPosition = FormStartPosition.Manual; s.Value.Location = new Point(40, 40); s.Value.Show(this);
                Shot(s.Value, Path.Combine(directory, s.Key + ".png")); s.Value.Close(); s.Value.Dispose();
                Console.WriteLine(s.Key + " ok");
            }
            ShowMini(true); mini.Location = new Point(40, 40); Shot(mini, Path.Combine(directory, "mini.png")); ShowMini(false);
            toast.Show("Activity deleted.", "Undo", null); Shot(this, Path.Combine(directory, "toast.png"));
            var saved = State; State = AppState.Fresh(); toast.Visible = false;
            foreach (string key in new[] { "dashboard", "timesheet", "reports" }) { Navigate(key); RefreshAll(); Shot(this, Path.Combine(directory, "first-run-" + key + ".png")); }
            State = saved; Navigate("dashboard"); RefreshAll();
        }
        /// <summary>
        /// Headless stress test used by "--smoke": drives every page, state and dialog, sweeps the mouse over
        /// every surface to exercise hover/tooltip painting, and fails if anything throws or is logged.
        /// </summary>
        internal void Smoke()
        {
            Action<string> step = name => { Application.DoEvents(); if (ErrorLog.Count > 0) throw new Exception("error after '" + name + "': " + ErrorLog.Last); Console.WriteLine("  ok  " + name); };
            var pages = new[] { "dashboard", "timesheet", "reports", "settings" };
            Action sweep = () =>
            {
                foreach (var surface in AllSurfaces(this))
                {
                    if (!surface.Visible || surface.Width < 10 || surface.Height < 10) continue;
                    int stepPx = Math.Max(Ui.S(28), Math.Min(surface.Width, surface.Height) / 14);
                    for (int y = 2; y < surface.Height; y += stepPx)
                        for (int x = 2; x < surface.Width; x += stepPx * 2) { surface.Probe(new Point(x, y)); surface.Refresh(); }
                    surface.Probe(new Point(-1, -1));
                    if (surface.CanScroll) { surface.ScrollY = 1 << 20; surface.Refresh(); surface.ScrollY = 0; surface.Refresh(); }
                }
            };

            // timer flow
            if (State.Active != null) Engine.Stop(State, Now);
            Start(State.Projects[0].Id); Start(State.Projects[1].Id); Stop();
            if (State.Active != null || store.Load().Active != null) throw new Exception("UI stop did not persist.");
            ResumeLast(); if (State.Active == null || State.Active.ProjectId != State.Projects[1].Id) throw new Exception("Resume last failed.");
            ToggleTimer(); if (State.Active != null) throw new Exception("Toggle did not stop.");
            ToggleTimer(); if (State.Active == null) throw new Exception("Toggle did not resume.");
            step("start / switch / stop / resume / toggle");

            // every page, both themes, three window sizes, with a mouse sweep
            foreach (string theme in new[] { "light", "dark" })
            {
                SetTheme(theme);
                foreach (var size in new[] { MinimumSize, new Size(Ui.S(1320), Ui.S(860)), new Size(Ui.S(1600), Ui.S(1000)) })
                {
                    Size = size;
                    foreach (var key in pages) { Navigate(key); RefreshAll(); Refresh(); }
                }
                Size = new Size(Ui.S(1320), Ui.S(860));
                foreach (var key in pages) { Navigate(key); RefreshAll(); Refresh(); sweep(); }
                step(theme + " theme: all pages, sizes and hover sweep");
            }
            SetTheme("light");

            // report ranges, including a custom future range and a single day
            Navigate("reports");
            foreach (string key in new[] { "week", "lastweek", "month", "lastmonth", "year", "all" }) { SetRange(key); Refresh(); }
            RangeFrom = DateTime.Today.AddDays(5); RangeEnd = DateTime.Today.AddDays(9); RangeKey = "custom"; RefreshAll(); Refresh();
            RangeFrom = DateTime.Today; RangeEnd = DateTime.Today.AddDays(1); RefreshAll(); Refresh();
            RangeFrom = DateTime.Today.AddYears(-3); RangeEnd = DateTime.Today.AddDays(1); RefreshAll(); Refresh(); sweep();
            step("report ranges");

            // timesheet days: past, future, empty
            Navigate("timesheet");
            foreach (int d in new[] { 0, -1, -7, -40, 3 }) { SetDay(DateTime.Today.AddDays(d)); Refresh(); }
            SetDay(DateTime.Today); sweep();
            step("timesheet days");

            // sessions: continue, delete + undo
            var done = State.Sessions.OrderByDescending(a => a.EndUtc).First();
            ContinueSession(done); Stop();
            var victim = State.Sessions.OrderByDescending(a => a.EndUtc).First();
            int before = State.Sessions.Count;
            DeleteSession(victim.Id);
            if (State.Sessions.Count != before - 1) throw new Exception("Delete session failed.");
            Mutate(() => Engine.SaveSession(State, victim, Now));
            if (State.Sessions.Count != before) throw new Exception("Undo delete session failed.");
            step("continue / delete / undo activity");

            // settings round trip
            Navigate("settings");
            Change(x => x.DailyGoalMinutes = 0); Refresh(); Change(x => x.DailyGoalMinutes = 480);
            Change(x => x.FocusMinutes = 25); Change(x => x.IdleMinutes = 0); Change(x => x.AskAfterSleep = true); Refresh();
            Change(x => { x.FocusMinutes = 0; x.IdleMinutes = 15; x.AskAfterSleep = false; });
            ToggleMini(); RefreshAll(); if (mini == null || !mini.Visible) throw new Exception("Mini widget did not open."); mini.Refresh(); ToggleMini();
            SetStartup(true); SetStartup(false);
            SetTheme("system"); SetTheme("light");
            step("settings, mini widget, themes");

            // dashboard states: archived view, search with no hit, archive/unarchive
            Navigate("dashboard");
            ShowArchived = true; RefreshAll(); Refresh(); ShowArchived = false;
            Search = "A project that does not exist"; RefreshAll(); Refresh(); Search = "";
            var archived = State.Projects.First(p => !p.Archived);
            Mutate(() => archived.Archived = true); Refresh(); Mutate(() => archived.Archived = false);
            step("dashboard states");

            // project deletion with undo, including the running project
            var target = State.Projects.First(p => !p.Archived);
            Start(target.Id);
            int projects = State.Projects.Count, sessionsOfTarget = State.Sessions.Count(a => a.ProjectId == target.Id);
            string targetId = target.Id;
            DeleteProject(targetId, false);
            if (State.Projects.Count != projects - 1 || State.Active != null || State.Sessions.Any(a => a.ProjectId == targetId)) throw new Exception("Delete project failed.");
            foreach (var key in pages) { Navigate(key); RefreshAll(); Refresh(); }
            if (store.Load().Projects.Any(p => p.Id == targetId)) throw new Exception("Deletion was not saved.");
            step("delete running project");
            var undo = typeof(Toast).GetField("onAction", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(toast) as Action;
            if (undo == null) throw new Exception("Undo action missing.");
            undo();
            if (State.Projects.Count != projects || State.Sessions.Count(a => a.ProjectId == targetId) != sessionsOfTarget + 1) throw new Exception("Undo project delete failed.");
            step("undo project deletion");

            // empty state: delete everything
            foreach (var p in State.Projects.ToList()) DeleteProject(p.Id, false);
            foreach (var key in pages) { Navigate(key); RefreshAll(); Refresh(); sweep(); }
            ResumeLast();
            step("no projects at all");

            // every dialog: construct, show, paint, close (light and dark)
            State.Projects.Add(new Project { Id = "smoke", Name = "Smoke test project", Color = "#3B6FD8" }); Save();
            foreach (string theme in new[] { "light", "dark" })
            {
                SetTheme(theme);
                var dialogs = new Form[] {
                    new SessionDialog(State, DateTime.Today, null, Zone, Marked), new SessionDialog(State, DateTime.Today.AddDays(-2), null, Zone, Marked),
                    new ProjectDialog(), new ProjectDialog(State.Projects[0]), new DateDialog(DateTime.Today), new RangeDialog(DateTime.Today.AddDays(-6), DateTime.Today.AddDays(1)),
                    new NoteDialog("Note"), new IdleDialog("Project", Now - TimeSpan.TicksPerMinute * 30, Now, Zone),
                    new AskDialog("Question", "A fairly long message to check that text wraps onto several lines in the question dialog.", "Yes", Kind.Danger, "Cancel") };
                foreach (var d in dialogs)
                {
                    d.StartPosition = FormStartPosition.Manual; d.Location = new Point(20, 20); d.Show(this); d.Refresh();
                    foreach (var surface in AllSurfaces(d)) { for (int x = 2; x < surface.Width; x += Ui.S(30)) { surface.Probe(new Point(x, surface.Height / 2)); surface.Refresh(); } }
                    d.Close(); d.Dispose();
                }
            }
            SetTheme("light");
            step("all dialogs");
            toast.Show("Test notification", "Undo", () => { }); Refresh();
            step("toast");
            if (ErrorLog.Count > 0) throw new Exception(ErrorLog.Last);
        }
        private static IEnumerable<Surface> AllSurfaces(Control root)
        {
            foreach (Control c in root.Controls)
            {
                var s = c as Surface; if (s != null) yield return s;
                foreach (var inner in AllSurfaces(c)) yield return inner;
            }
        }
    }
}
