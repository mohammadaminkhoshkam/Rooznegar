using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace Rooznegar
{
    public sealed class Project
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Color { get; set; }
        public bool Archived { get; set; }
        /// <summary>Optional weekly target in minutes (0 = none).</summary>
        public int WeeklyGoalMinutes { get; set; }
    }
    public sealed class Session
    {
        public string Id { get; set; }
        public string ProjectId { get; set; }
        public long StartUtc { get; set; }
        public long EndUtc { get; set; }
        public string Note { get; set; }
        public bool Manual { get; set; }
    }
    public sealed class RunningSession
    {
        public string ProjectId { get; set; }
        public long StartUtc { get; set; }
        public long LastSeenUtc { get; set; }
        public string Note { get; set; }
    }
    public sealed class Settings
    {
        public int DailyGoalMinutes { get; set; }
        public bool StartWithWindows { get; set; }
        /// <summary>"light", "dark" or "system".</summary>
        public string Theme { get; set; }
        /// <summary>Ask about away-from-keyboard time after this many minutes (0 = off).</summary>
        public int IdleMinutes { get; set; }
        /// <summary>Also ask about time spent asleep/hibernated. Off by default: sleep counts as work.</summary>
        public bool AskAfterSleep { get; set; }
        /// <summary>Focus interval before a break reminder (0 = off).</summary>
        public int FocusMinutes { get; set; }
        public int BreakMinutes { get; set; }
        public bool GlobalHotkey { get; set; }
        public bool MiniWidget { get; set; }
        public int MiniX { get; set; }
        public int MiniY { get; set; }
        public Settings()
        {
            DailyGoalMinutes = 480; Theme = "system"; IdleMinutes = 15; FocusMinutes = 0; BreakMinutes = 5;
            GlobalHotkey = true; MiniWidget = false; MiniX = -1; MiniY = -1;
        }
    }
    public sealed class AppState
    {
        public int Schema { get; set; }
        public List<Project> Projects { get; set; }
        public List<Session> Sessions { get; set; }
        public RunningSession Active { get; set; }
        public Settings Settings { get; set; }
        public AppState()
        {
            Schema = 1; Projects = new List<Project>(); Sessions = new List<Session>(); Settings = new Settings();
        }
        /// <summary>First-run state: no projects; the user creates their own.</summary>
        public static AppState Fresh() { return new AppState(); }
        /// <summary>Three example projects, used only by the tests and the screenshot/demo mode.</summary>
        public static AppState Sample()
        {
            var s = new AppState();
            s.Projects.Add(new Project { Id = Guid.NewGuid().ToString("N"), Name = "یادگیری زبان", Color = "#0F766E" });
            s.Projects.Add(new Project { Id = Guid.NewGuid().ToString("N"), Name = "پژوهش و توسعه", Color = "#3B6FD8" });
            s.Projects.Add(new Project { Id = Guid.NewGuid().ToString("N"), Name = "کارهای شخصی", Color = "#E07B39" });
            return s;
        }
    }
    public sealed class TimeSlice
    {
        public string SessionId;
        public string ProjectId;
        public DateTime LocalDay;
        public long StartUtc;
        public long EndUtc;
        public string Note;
        public bool Active;
        public bool Manual;
        public long Duration { get { return Math.Max(0, EndUtc - StartUtc); } }
    }
    public static class Dates
    {
        public static readonly PersianCalendar Persian = new PersianCalendar();
        public static readonly string[] Months = { "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند" };
        public static readonly string[] Days = { "یکشنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه", "پنجشنبه", "جمعه", "شنبه" };
        public static string Digits(string value)
        {
            if (value == null) return "";
            var b = new StringBuilder(value);
            for (int i = 0; i < b.Length; i++) if (b[i] >= '0' && b[i] <= '9') b[i] = (char)('۰' + b[i] - '0');
            return b.ToString();
        }
        public static string Short(DateTime day)
        {
            return Digits(string.Format(CultureInfo.InvariantCulture, "{0:0000}/{1:00}/{2:00}", Persian.GetYear(day), Persian.GetMonth(day), Persian.GetDayOfMonth(day)));
        }
        public static string Long(DateTime day)
        {
            return Days[(int)day.DayOfWeek] + "، " + Digits(Persian.GetDayOfMonth(day).ToString()) + " " + Months[Persian.GetMonth(day) - 1] + " " + Digits(Persian.GetYear(day).ToString());
        }
        public static string Duration(long ticks)
        {
            long total = Math.Max(0, ticks) / TimeSpan.TicksPerSecond;
            return Digits(string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", total / 3600, total / 60 % 60, total % 60));
        }
        /// <summary>Compact h:mm, e.g. ۴:۰۷ (hours are not wrapped at 24).</summary>
        public static string Hm(long ticks)
        {
            long minutes = Math.Max(0, ticks) / TimeSpan.TicksPerMinute;
            return Digits(string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", minutes / 60, minutes % 60));
        }
        /// <summary>Readable Persian duration, e.g. «۴ ساعت و ۷ دقیقه».</summary>
        public static string Human(long ticks)
        {
            long minutes = Math.Max(0, ticks) / TimeSpan.TicksPerMinute, h = minutes / 60, m = minutes % 60;
            if (h == 0) return Digits(m.ToString(CultureInfo.InvariantCulture)) + " دقیقه";
            if (m == 0) return Digits(h.ToString(CultureInfo.InvariantCulture)) + " ساعت";
            return Digits(h.ToString(CultureInfo.InvariantCulture)) + " ساعت و " + Digits(m.ToString(CultureInfo.InvariantCulture)) + " دقیقه";
        }
        public static string Clock(long utcTicks, TimeZoneInfo zone)
        {
            return Digits(Local(utcTicks, zone).ToString("HH:mm", CultureInfo.InvariantCulture));
        }
        public static string DayMonth(DateTime day)
        {
            return Digits(Persian.GetDayOfMonth(day).ToString(CultureInfo.InvariantCulture)) + " " + Months[Persian.GetMonth(day) - 1];
        }
        public static string MonthYear(DateTime day)
        {
            return Months[Persian.GetMonth(day) - 1] + " " + Digits(Persian.GetYear(day).ToString(CultureInfo.InvariantCulture));
        }
        public static DateTime YearStart(DateTime day) { return Persian.ToDateTime(Persian.GetYear(day), 1, 1, 0, 0, 0, 0); }
        /// <summary>Parses «9:30», «۰۹:۳۰:۰۵» or «930» into a time of day.</summary>
        public static TimeSpan ParseTime(string text)
        {
            var b = new StringBuilder();
            foreach (char c in (text ?? "").Trim())
            {
                if (c >= '۰' && c <= '۹') b.Append((char)('0' + c - '۰'));
                else if (c >= '٠' && c <= '٩') b.Append((char)('0' + c - '٠'));
                else if (c == '.' || c == '٫' || c == '/') b.Append(':');
                else b.Append(c);
            }
            string t = b.ToString();
            if (t.IndexOf(':') < 0 && t.Length >= 3 && t.Length <= 4 && t.All(char.IsDigit)) t = t.Substring(0, t.Length - 2) + ":" + t.Substring(t.Length - 2);
            if (t.IndexOf(':') < 0 && t.Length >= 1 && t.Length <= 2 && t.All(char.IsDigit)) t = t + ":00";
            var parts = t.Split(':');
            int h, m, sec = 0;
            if (parts.Length < 2 || parts.Length > 3 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out h) || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out m)
                || (parts.Length == 3 && !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out sec)) || h > 23 || m > 59 || sec > 59)
                throw new ArgumentException("ساعت را به شکل ۰۹:۳۰ وارد کنید.");
            return new TimeSpan(h, m, sec);
        }
        /// <summary>Culture-independent number with Persian digits and the Persian decimal separator.</summary>
        public static string Number(double value, string format)
        {
            return Digits(value.ToString(format, CultureInfo.InvariantCulture).Replace('.', '٫'));
        }
        public static string DurationCsv(long ticks)
        {
            long total = Math.Max(0, ticks) / TimeSpan.TicksPerSecond;
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", total / 3600, total / 60 % 60, total % 60);
        }
        public static DateTime WeekStart(DateTime day) { return day.Date.AddDays(-(((int)day.DayOfWeek + 1) % 7)); }
        public static DateTime MonthStart(DateTime day) { return day.Date.AddDays(1 - Persian.GetDayOfMonth(day)); }
        public static DateTime Local(long ticks, TimeZoneInfo zone)
        {
            return TimeZoneInfo.ConvertTimeFromUtc(new DateTime(ticks, DateTimeKind.Utc), zone);
        }
        public static long Utc(DateTime local, TimeZoneInfo zone)
        {
            local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(local)) throw new ArgumentException("این ساعت در منطقهٔ زمانی سیستم معتبر نیست.");
            return TimeZoneInfo.ConvertTimeToUtc(local, zone).Ticks;
        }
        // Midnight may fall in a DST gap; move to the first valid instant of that date.
        public static long Boundary(DateTime day, TimeZoneInfo zone)
        {
            day = DateTime.SpecifyKind(day.Date, DateTimeKind.Unspecified);
            while (zone.IsInvalidTime(day)) day = day.AddMinutes(1);
            return Utc(day, zone);
        }
    }
    public static class Engine
    {
        public static void Start(AppState s, string projectId, long now)
        {
            if (!s.Projects.Any(p => p.Id == projectId && !p.Archived)) throw new ArgumentException("پروژه یافت نشد.");
            if (s.Active != null && s.Active.ProjectId == projectId) return;
            Stop(s, now);
            s.Active = new RunningSession { ProjectId = projectId, StartUtc = now, LastSeenUtc = now, Note = "" };
        }
        public static void Stop(AppState s, long now)
        {
            if (s.Active == null) return;
            if (now < s.Active.StartUtc) throw new InvalidOperationException("ساعت سیستم به عقب تغییر کرده است. ابتدا ساعت را اصلاح کنید.");
            if (now > s.Active.StartUtc)
                s.Sessions.Add(new Session { Id = Guid.NewGuid().ToString("N"), ProjectId = s.Active.ProjectId,
                    StartUtc = s.Active.StartUtc, EndUtc = now, Note = s.Active.Note ?? "", Manual = false });
            s.Active = null;
        }
        /// <summary>
        /// Removes away-from-keyboard time from the running timer: the current run ends at
        /// <paramref name="idleStart"/> and, when <paramref name="keepRunning"/> is set, the same
        /// project restarts at <paramref name="now"/> with the same note.
        /// </summary>
        public static void DiscardIdle(AppState s, long idleStart, long now, bool keepRunning)
        {
            if (s.Active == null) return;
            if (now < s.Active.StartUtc) throw new InvalidOperationException("ساعت سیستم به عقب تغییر کرده است. ابتدا ساعت را اصلاح کنید.");
            idleStart = Math.Min(now, Math.Max(idleStart, s.Active.StartUtc));
            string project = s.Active.ProjectId, note = s.Active.Note;
            Stop(s, idleStart);
            if (keepRunning && s.Projects.Any(p => p.Id == project && !p.Archived))
                s.Active = new RunningSession { ProjectId = project, StartUtc = now, LastSeenUtc = now, Note = note ?? "" };
        }
        /// <summary>
        /// Permanently removes a project and all of its activities. A running timer on it is stopped first,
        /// so the removed activities include the time tracked up to <paramref name="now"/>.
        /// Returns the removed activities so the caller can offer undo.
        /// </summary>
        public static List<Session> DeleteProject(AppState s, string projectId, long now)
        {
            var project = s.Projects.FirstOrDefault(p => p.Id == projectId);
            if (project == null) throw new ArgumentException("پروژه یافت نشد.");
            if (s.Active != null && s.Active.ProjectId == projectId) Stop(s, now);
            var removed = s.Sessions.Where(a => a.ProjectId == projectId).ToList();
            s.Sessions.RemoveAll(a => a.ProjectId == projectId);
            s.Projects.Remove(project);
            return removed;
        }
        /// <summary>
        /// Undo for <see cref="DeleteProject"/>. Activities that would now overlap something recorded in the
        /// meantime (or the running timer) are skipped; the number skipped is returned.
        /// </summary>
        public static int RestoreProject(AppState s, Project project, IEnumerable<Session> sessions)
        {
            if (s.Projects.Any(p => p.Id == project.Id)) throw new ArgumentException("این پروژه از قبل وجود دارد.");
            string name = project.Name;
            for (int i = 2; s.Projects.Any(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase)); i++) name = project.Name + " (" + i + ")";
            if (name.Length > 100) name = name.Substring(0, 100);
            s.Projects.Add(new Project { Id = project.Id, Name = name, Color = project.Color, Archived = project.Archived, WeeklyGoalMinutes = project.WeeklyGoalMinutes });
            int skipped = 0;
            foreach (var a in sessions.OrderBy(x => x.StartUtc))
            {
                bool overlaps = s.Sessions.Any(b => a.StartUtc < b.EndUtc && a.EndUtc > b.StartUtc) || (s.Active != null && a.EndUtc > s.Active.StartUtc) || s.Sessions.Any(b => b.Id == a.Id);
                if (overlaps || a.EndUtc <= a.StartUtc) { skipped++; continue; }
                s.Sessions.Add(new Session { Id = a.Id, ProjectId = project.Id, StartUtc = a.StartUtc, EndUtc = a.EndUtc, Note = a.Note, Manual = a.Manual });
            }
            return skipped;
        }
        /// <summary>The project of the running timer, otherwise of the latest finished activity.</summary>
        public static string LastProjectId(AppState s)
        {
            if (s.Active != null) return s.Active.ProjectId;
            var last = s.Sessions.Where(a => s.Projects.Any(p => p.Id == a.ProjectId && !p.Archived)).OrderByDescending(a => a.EndUtc).FirstOrDefault();
            return last == null ? null : last.ProjectId;
        }
        public static Dictionary<DateTime, long> DayTotals(IEnumerable<TimeSlice> slices)
        {
            var d = new Dictionary<DateTime, long>();
            foreach (var t in slices) { long v; d.TryGetValue(t.LocalDay, out v); d[t.LocalDay] = v + t.Duration; }
            return d;
        }
        /// <summary>
        /// Consecutive days ending today (or yesterday, while today is still in progress) whose total
        /// reaches the daily goal; any tracked time counts when no goal is set.
        /// </summary>
        public static int Streak(Dictionary<DateTime, long> days, long goalTicks, DateTime today)
        {
            Func<DateTime, bool> met = d => { long v; days.TryGetValue(d.Date, out v); return goalTicks > 0 ? v >= goalTicks : v > 0; };
            DateTime day = today.Date; if (!met(day)) day = day.AddDays(-1);
            int count = 0; while (met(day) && count < 10000) { count++; day = day.AddDays(-1); }
            return count;
        }
        public static IEnumerable<TimeSlice> Slices(AppState s, long now, TimeZoneInfo zone)
        {
            foreach (var a in s.Sessions)
                foreach (var t in Split(a.Id, a.ProjectId, a.StartUtc, a.EndUtc, a.Note, false, a.Manual, zone)) yield return t;
            if (s.Active != null)
                foreach (var t in Split("active", s.Active.ProjectId, s.Active.StartUtc, Math.Max(s.Active.StartUtc, now), s.Active.Note, true, false, zone)) yield return t;
        }
        public static IEnumerable<TimeSlice> Split(string id, string project, long start, long end, string note, bool active, bool manual, TimeZoneInfo zone)
        {
            if (end <= start) yield break;
            while (start < end)
            {
                DateTime day = Dates.Local(start, zone).Date;
                long next = Dates.Boundary(day.AddDays(1), zone);
                long until = Math.Min(end, next);
                if (until <= start) throw new InvalidOperationException("مرز روز قابل محاسبه نیست.");
                yield return new TimeSlice { SessionId = id, ProjectId = project, LocalDay = day, StartUtc = start, EndUtc = until, Note = note ?? "", Active = active, Manual = manual };
                start = until;
            }
        }
        public static long Total(IEnumerable<TimeSlice> slices, DateTime from, DateTime until, string projectId = null)
        {
            return slices.Where(t => t.LocalDay >= from.Date && t.LocalDay < until.Date && (projectId == null || t.ProjectId == projectId)).Sum(t => t.Duration);
        }
        public static void SaveSession(AppState s, Session value, long now)
        {
            if (!s.Projects.Any(p => p.Id == value.ProjectId)) throw new ArgumentException("پروژه یافت نشد.");
            if (value.StartUtc <= 0 || value.EndUtc <= value.StartUtc) throw new ArgumentException("زمان پایان باید بعد از شروع باشد.");
            if (value.EndUtc > now) throw new ArgumentException("ثبت زمان در آینده مجاز نیست.");
            if (s.Sessions.Any(a => a.Id != value.Id && value.StartUtc < a.EndUtc && value.EndUtc > a.StartUtc))
                throw new ArgumentException("این بازه با یک فعالیت ثبت‌شده هم‌پوشانی دارد.");
            if (s.Active != null && value.EndUtc > s.Active.StartUtc && value.StartUtc <= now)
                throw new ArgumentException("این بازه با تایمر فعال هم‌پوشانی دارد؛ ابتدا تایمر را متوقف کنید.");
            if (string.IsNullOrEmpty(value.Id)) value.Id = Guid.NewGuid().ToString("N");
            var index = s.Sessions.FindIndex(a => a.Id == value.Id);
            if (index >= 0) s.Sessions[index] = value; else s.Sessions.Add(value);
        }
        public static string Csv(AppState s, long now, TimeZoneInfo zone, DateTime from, DateTime until)
        {
            var b = new StringBuilder("تاریخ شمسی,پروژه,شروع,پایان,مدت,مدت به ثانیه,یادداشت,نوع\r\n");
            foreach (var t in Slices(s, now, zone).Where(t => t.LocalDay >= from.Date && t.LocalDay < until.Date).OrderBy(t => t.StartUtc))
            {
                var p = s.Projects.First(a => a.Id == t.ProjectId);
                var fields = new[] { Dates.Short(t.LocalDay), p.Name, Dates.Local(t.StartUtc, zone).ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                    Dates.Local(t.EndUtc, zone).ToString("HH:mm:ss", CultureInfo.InvariantCulture), Dates.DurationCsv(t.Duration),
                    (t.Duration / TimeSpan.TicksPerSecond).ToString(CultureInfo.InvariantCulture), t.Note,
                    t.Active ? "در حال ثبت" : t.Manual ? "ثبت دستی" : "تایمر" };
                b.AppendLine(string.Join(",", fields.Select(CsvCell)));
            }
            return b.ToString();
        }
        private static string CsvCell(string text)
        {
            text = text ?? "";
            // Protect text cells from spreadsheet formula injection.
            var trimmed = text.TrimStart();
            if (trimmed.Length > 0 && "=+-@".IndexOf(trimmed[0]) >= 0) text = "'" + text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
    }
    public sealed class Store
    {
        public readonly string DirectoryPath;
        public readonly string FilePath;
        public string RecoveryMessage;
        public Store(string directory)
        {
            DirectoryPath = directory; FilePath = Path.Combine(directory, "data.json");
        }
        public static JavaScriptSerializer Serializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024, RecursionLimit = 100 };
        }
        public static AppState Parse(string json)
        {
            var s = Serializer().Deserialize<AppState>(json);
            Validate(s); return s;
        }
        public static void Validate(AppState s)
        {
            if (s == null || s.Schema != 1 || s.Projects == null || s.Sessions == null || s.Settings == null)
                throw new InvalidDataException("ساختار فایل داده معتبر نیست.");
            if (s.Projects.Count > 10000 || s.Sessions.Count > 1000000) throw new InvalidDataException("فایل بیش از حد بزرگ است.");
            var ids = new HashSet<string>();
            foreach (var p in s.Projects)
            {
                if (p == null || string.IsNullOrEmpty(p.Id) || !ids.Add(p.Id) || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 100)
                    throw new InvalidDataException("اطلاعات پروژه معتبر نیست.");
                if (p.Color == null || !System.Text.RegularExpressions.Regex.IsMatch(p.Color, "^#[0-9a-fA-F]{6}$")) p.Color = "#0F766E";
            }
            var sessionIds = new HashSet<string>();
            foreach (var a in s.Sessions)
                if (a == null || string.IsNullOrEmpty(a.Id) || !sessionIds.Add(a.Id) || !ids.Contains(a.ProjectId) || a.StartUtc < 1 || a.EndUtc <= a.StartUtc || a.EndUtc >= DateTime.MaxValue.Ticks)
                    throw new InvalidDataException("اطلاعات فعالیت معتبر نیست.");
            var sorted = s.Sessions.OrderBy(a => a.StartUtc).ToList();
            for (int i = 1; i < sorted.Count; i++) if (sorted[i].StartUtc < sorted[i - 1].EndUtc) throw new InvalidDataException("فعالیت‌های فایل با هم هم‌پوشانی دارند.");
            if (s.Active != null && (!ids.Contains(s.Active.ProjectId) || s.Active.StartUtc < 1 || s.Active.StartUtc >= DateTime.MaxValue.Ticks || s.Active.LastSeenUtc < s.Active.StartUtc || sorted.Any(a => a.EndUtc > s.Active.StartUtc)))
                throw new InvalidDataException("اطلاعات تایمر فعال معتبر نیست.");
            if (s.Settings.DailyGoalMinutes < 0 || s.Settings.DailyGoalMinutes > 1440) s.Settings.DailyGoalMinutes = 480;
            if (s.Settings.Theme != "light" && s.Settings.Theme != "dark") s.Settings.Theme = "system";
            if (s.Settings.IdleMinutes < 0 || s.Settings.IdleMinutes > 240) s.Settings.IdleMinutes = 15;
            if (s.Settings.FocusMinutes < 0 || s.Settings.FocusMinutes > 240) s.Settings.FocusMinutes = 0;
            if (s.Settings.BreakMinutes < 1 || s.Settings.BreakMinutes > 120) s.Settings.BreakMinutes = 5;
            foreach (var p in s.Projects) if (p.WeeklyGoalMinutes < 0 || p.WeeklyGoalMinutes > 10080) p.WeeklyGoalMinutes = 0;
        }
        public AppState Load()
        {
            if (!File.Exists(FilePath) && !File.Exists(FilePath + ".bak")) return AppState.Fresh();
            try { return Parse(File.ReadAllText(FilePath, Encoding.UTF8)); }
            catch (Exception primary)
            {
                try
                {
                    var s = Parse(File.ReadAllText(FilePath + ".bak", Encoding.UTF8));
                    RecoveryMessage = "فایل اصلی خوانده نشد. آخرین نسخهٔ پشتیبان بازیابی شد.";
                    if (File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".damaged-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture), true);
                    return s;
                }
                catch { throw new IOException("داده‌ها قابل خواندن نیستند. فایل‌های موجود حفظ شده‌اند. مسیر: " + DirectoryPath, primary); }
            }
        }
        public void Save(AppState state)
        {
            Validate(state);
            Directory.CreateDirectory(DirectoryPath);
            string json = Serializer().Serialize(state), temp = FilePath + ".tmp";
            byte[] bytes = new UTF8Encoding(false).GetBytes(json);
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
            }
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak", true);
            else File.Move(temp, FilePath);
            // The primary commit is complete. A failed optional daily checkpoint
            // must not make the UI roll back a mutation already on disk.
            try
            {
                string backups = Path.Combine(DirectoryPath, "Backups"); Directory.CreateDirectory(backups);
                string daily = Path.Combine(backups, "data-" + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".json");
                if (!File.Exists(daily))
                {
                    File.Copy(FilePath, daily);
                    foreach (string stale in Directory.GetFiles(backups, "data-*.json").OrderByDescending(x => x).Skip(30)) File.Delete(stale);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
