using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rooznegar;

public static class Tests
{
    static int count;
    static TimeZoneInfo zone = TimeZoneInfo.CreateCustomTimeZone("Test+0330", TimeSpan.FromMinutes(210), "Test +03:30", "Test +03:30");
    static long U(DateTime t) { return Dates.Utc(t, zone); }
    static DateTime date = new DateTime(2026, 6, 5);
    static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); count++; Console.WriteLine("PASS " + name); }
    static void Throws(Action action, string name) { try { action(); } catch (ArgumentException) { Check(true, name); return; } catch (InvalidOperationException) { Check(true, name); return; } Check(false, name); }
    public static void Main()
    {
        var s = AppState.Sample(); long t = U(date.AddHours(9)); string a = s.Projects[0].Id, b = s.Projects[1].Id;
        Engine.Start(s, a, t); Engine.Start(s, a, t + TimeSpan.TicksPerMinute);
        Check(s.Active.StartUtc == t && s.Sessions.Count == 0, "clicking active project does not reset it");
        Engine.Start(s, b, t + TimeSpan.TicksPerMinute * 30);
        Check(s.Sessions.Count == 1 && s.Sessions[0].EndUtc == s.Active.StartUtc, "switch is atomic without gap or overlap");
        Engine.Stop(s, t + TimeSpan.TicksPerHour);
        Check(s.Active == null && s.Sessions.Count == 2 && s.Sessions.Sum(x => x.EndUtc - x.StartUtc) == TimeSpan.TicksPerHour, "project totals conserve elapsed time");
        Engine.Stop(s, t + TimeSpan.TicksPerHour * 2); Check(s.Sessions.Count == 2, "stopping twice is harmless");
        Engine.Start(s, a, t + TimeSpan.TicksPerHour * 2);
        var restored = Store.Parse(Store.Serializer().Serialize(s));
        var afterSleep = Engine.Slices(restored, t + TimeSpan.TicksPerHour * 5, zone).Where(x => x.Active).Sum(x => x.Duration);
        Check(afterSleep == TimeSpan.TicksPerHour * 3, "three hours of sleep count without timer ticks");
        Check(restored.Active.ProjectId == a && restored.Active.StartUtc == s.Active.StartUtc, "active state survives serialization/restart");
        Throws(() => Engine.Stop(restored, restored.Active.StartUtc - 1), "backward clock cannot create negative session");
        Throws(() => Engine.Start(s, "missing", t), "unknown project cannot start");
        var split = Engine.Split("n", a, U(date.AddHours(23).AddMinutes(50)), U(date.AddDays(1).AddMinutes(20)), "", false, false, zone).ToList();
        Check(split.Count == 2 && split[0].Duration == TimeSpan.TicksPerMinute * 10 && split[1].Duration == TimeSpan.TicksPerMinute * 20, "midnight splits ten and twenty minutes");
        Check(split[0].LocalDay == date && split[1].LocalDay == date.AddDays(1), "daily slices belong to correct dates");
        var multi = Engine.Split("n", a, U(date.AddHours(21)), U(date.AddDays(4).AddHours(4)), "", false, false, zone).ToList();
        Check(multi.Count == 5 && multi.Sum(x => x.Duration) == TimeSpan.TicksPerHour * 79, "multi-day sleep preserves all 79 hours");
        var boundary = Engine.Split("n", a, U(date), U(date.AddDays(1)), "", false, false, zone).ToList();
        Check(boundary.Count == 1 && boundary[0].Duration == TimeSpan.TicksPerDay, "exact midnight has no empty extra day");
        var leap = new DateTime(2024, 2, 28);
        Check(Dates.Short(leap.AddDays(1)) == "2024-02-29" && Dates.Short(leap.AddDays(2)) == "2024-03-01", "leap day and ISO dates");
        Check(Dates.WeekStart(date).DayOfWeek == DayOfWeek.Monday && Dates.WeekStart(new DateTime(2026, 6, 7)) == new DateTime(2026, 6, 1) && Dates.MonthStart(date) == new DateTime(2026, 6, 1), "Monday weeks and month boundary");
        Check(Dates.Duration(TimeSpan.TicksPerHour * 125 + TimeSpan.TicksPerMinute * 3) == "125:03:00", "durations exceeding 24 hours are not wrapped");
        var manual = AppState.Sample(); string p = manual.Projects[0].Id;
        Engine.SaveSession(manual, new Session { ProjectId = p, StartUtc = t, EndUtc = t + TimeSpan.TicksPerHour, Note = "text", Manual = true }, t + TimeSpan.TicksPerDay);
        Throws(() => Engine.SaveSession(manual, new Session { ProjectId = p, StartUtc = t + 1, EndUtc = t + TimeSpan.TicksPerHour * 2 }, t + TimeSpan.TicksPerDay), "manual overlaps are rejected");
        Engine.SaveSession(manual, new Session { ProjectId = p, StartUtc = t + TimeSpan.TicksPerHour, EndUtc = t + TimeSpan.TicksPerHour * 2 }, t + TimeSpan.TicksPerDay);
        Check(manual.Sessions.Count == 2, "touching sessions are allowed");
        Throws(() => Engine.SaveSession(manual, new Session { ProjectId = p, StartUtc = t + TimeSpan.TicksPerDay, EndUtc = t + TimeSpan.TicksPerDay * 2 }, t), "future records are rejected");
        Engine.Start(manual, p, t + TimeSpan.TicksPerHour * 3);
        Throws(() => Engine.SaveSession(manual, new Session { ProjectId = p, StartUtc = t + TimeSpan.TicksPerHour * 3, EndUtc = t + TimeSpan.TicksPerHour * 4 }, t + TimeSpan.TicksPerDay), "manual entries cannot overlap active timer");
        manual.Projects[0].Name = "=1+1"; manual.Sessions[0].Note = "a,\"b\"\nline";
        var csv = Engine.Csv(manual, t + TimeSpan.TicksPerHour * 4, zone, date, date.AddDays(1));
        Check(csv.Contains("\"'=1+1\"") && csv.Contains("\"a,\"\"b\"\"\nline\""), "CSV quotes and formula injection protection");
        Check(Engine.Total(split, date, date.AddDays(1)) == TimeSpan.TicksPerMinute * 10, "daily total excludes next-day part");
        manual.Projects[1].Archived = true; Throws(() => Engine.Start(manual, manual.Projects[1].Id, t), "archived project cannot be started");
        var path = Path.Combine(Path.GetTempPath(), "rooznegar-test-" + Guid.NewGuid().ToString("N")); var store = new Store(path);
        try
        {
            var clean = AppState.Sample(); store.Save(clean); clean.Projects[0].Name = "Renamed"; store.Save(clean);
            Check(store.Load().Projects[0].Name == "Renamed" && File.Exists(store.FilePath + ".bak"), "atomic file persistence and previous backup");
            File.WriteAllText(store.FilePath, "broken"); var fallback = new Store(path); var recovered = fallback.Load();
            Check(recovered.Projects[0].Name == "Language learning" && fallback.RecoveryMessage != null, "corrupt primary recovers previous valid state");
            Check(Directory.GetFiles(Path.Combine(path, "Backups")).Length == 1, "daily recovery checkpoint exists");
            Check(Directory.GetFiles(path, "*.damaged-*").Length == 1, "damaged data is preserved for recovery");
        }
        finally { Directory.Delete(path, true); }
        // A 23-hour DST day must split by real local midnight, not fixed 24-hour chunks.
        var dst = TimeZoneInfo.CreateCustomTimeZone("TestDST", TimeSpan.Zero, "test", "standard", "daylight", new[] {
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2026,1,1), new DateTime(2026,12,31), TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1,1,1,2,0,0), 3, 29), TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1,1,1,3,0,0), 10, 25)) });
        var spring = new DateTime(2026,3,29); var dstParts = Engine.Split("dst", p, Dates.Boundary(spring, dst), Dates.Boundary(spring.AddDays(1), dst), "", false, false, dst).ToList();
        Check(dstParts.Count == 1 && dstParts[0].Duration == TimeSpan.TicksPerHour * 23, "DST day uses real local boundaries");
        // ---- 2.0: idle handling, resume, streaks, time parsing, backward compatibility
        var idle = AppState.Sample(); string ip = idle.Projects[0].Id;
        Engine.Start(idle, ip, t); idle.Active.Note = "note";
        Engine.DiscardIdle(idle, t + TimeSpan.TicksPerMinute * 40, t + TimeSpan.TicksPerMinute * 70, true);
        Check(idle.Sessions.Count == 1 && idle.Sessions[0].EndUtc - idle.Sessions[0].StartUtc == TimeSpan.TicksPerMinute * 40
            && idle.Active != null && idle.Active.StartUtc == t + TimeSpan.TicksPerMinute * 70 && idle.Active.Note == "note", "idle time is removed and the timer continues");
        Engine.DiscardIdle(idle, t + TimeSpan.TicksPerMinute * 80, t + TimeSpan.TicksPerMinute * 100, false);
        Check(idle.Active == null && idle.Sessions.Count == 2 && idle.Sessions[1].EndUtc == t + TimeSpan.TicksPerMinute * 80, "idle time can be discarded and the timer stopped");
        var early = AppState.Sample(); Engine.Start(early, early.Projects[0].Id, t);
        Engine.DiscardIdle(early, t - TimeSpan.TicksPerHour, t + TimeSpan.TicksPerMinute * 5, false);
        Check(early.Sessions.Count == 0 && early.Active == null, "idle start before the timer never creates negative time");
        Check(Engine.LastProjectId(idle) == ip, "resume picks the latest project");
        idle.Projects[0].Archived = true; Check(Engine.LastProjectId(idle) == null, "resume ignores archived projects");
        var days = new Dictionary<DateTime, long> { { date, TimeSpan.TicksPerHour * 8 }, { date.AddDays(-1), TimeSpan.TicksPerHour * 9 }, { date.AddDays(-2), TimeSpan.TicksPerHour * 2 } };
        Check(Engine.Streak(days, TimeSpan.TicksPerHour * 8, date) == 2, "streak counts consecutive goal days");
        Check(Engine.Streak(days, TimeSpan.TicksPerHour * 8, date.AddDays(1)) == 2, "streak survives while today is in progress");
        Check(Engine.Streak(days, 0, date) == 3, "without a goal any tracked day counts");
        Check(Engine.DayTotals(split)[date] == TimeSpan.TicksPerMinute * 10, "day totals follow midnight split");
        Check(Dates.ParseTime("9:05") == new TimeSpan(9, 5, 0) && Dates.ParseTime("0930") == new TimeSpan(9, 30, 0) && Dates.ParseTime("23.59.10") == new TimeSpan(23, 59, 10), "time parsing accepts shorthands");
        Throws(() => Dates.ParseTime("25:00"), "invalid times are rejected");
        Check(Dates.Hm(TimeSpan.TicksPerHour * 26 + TimeSpan.TicksPerMinute * 7) == "26:07" && Dates.Human(TimeSpan.TicksPerMinute * 95) == "1 hour 35 minutes" && Dates.Human(TimeSpan.TicksPerMinute * 120) == "2 hours", "compact and readable durations");
        var legacy = Store.Parse("{\"Schema\":1,\"Projects\":[{\"Id\":\"a\",\"Name\":\"x\",\"Color\":\"#0F766E\",\"Archived\":false}],\"Sessions\":[],\"Active\":null,\"Settings\":{\"DailyGoalMinutes\":300,\"StartWithWindows\":false}}");
        Check(legacy.Settings.DailyGoalMinutes == 300 && legacy.Settings.Theme == "system" && legacy.Settings.IdleMinutes == 15 && !legacy.Settings.AskAfterSleep && legacy.Settings.GlobalHotkey && legacy.Projects[0].WeeklyGoalMinutes == 0, "version 1.0 data files load with new defaults");
        var odd = AppState.Sample(); odd.Settings.Theme = "neon"; odd.Settings.IdleMinutes = -4; odd.Projects[0].WeeklyGoalMinutes = -1; Store.Validate(odd);
        Check(odd.Settings.Theme == "system" && odd.Settings.IdleMinutes == 15 && odd.Projects[0].WeeklyGoalMinutes == 0, "out-of-range settings are repaired");
        // ---- project deletion and undo
        var del = AppState.Sample(); string d0 = del.Projects[0].Id, d1 = del.Projects[1].Id;
        Engine.SaveSession(del, new Session { ProjectId = d0, StartUtc = t, EndUtc = t + TimeSpan.TicksPerHour, Note = "a" }, t + TimeSpan.TicksPerDay);
        Engine.SaveSession(del, new Session { ProjectId = d1, StartUtc = t + TimeSpan.TicksPerHour, EndUtc = t + TimeSpan.TicksPerHour * 2 }, t + TimeSpan.TicksPerDay);
        Engine.Start(del, d0, t + TimeSpan.TicksPerHour * 3);
        var gone = Engine.DeleteProject(del, d0, t + TimeSpan.TicksPerHour * 4);
        Check(del.Active == null && gone.Count == 2 && gone.Sum(x => x.EndUtc - x.StartUtc) == TimeSpan.TicksPerHour * 2 && del.Projects.All(q => q.Id != d0)
            && del.Sessions.Count == 1 && del.Sessions[0].ProjectId == d1, "deleting a project stops its timer and removes only its activities");
        Store.Validate(del); Check(true, "state stays valid after deleting a project");
        Throws(() => Engine.DeleteProject(del, "missing", t), "deleting an unknown project is rejected");
        var lost = new Project { Id = d0, Name = "Language learning", Color = "#0F766E" };
        Engine.SaveSession(del, new Session { ProjectId = d1, StartUtc = t + TimeSpan.TicksPerMinute * 30, EndUtc = t + TimeSpan.TicksPerMinute * 50 }, t + TimeSpan.TicksPerDay);
        int skippedRestore = Engine.RestoreProject(del, lost, gone);
        Check(skippedRestore == 1 && del.Projects.Any(q => q.Id == d0) && del.Sessions.Count(x => x.ProjectId == d0) == 1, "undo restores the project and skips activities that now overlap");
        Store.Validate(del); Check(true, "state stays valid after undo");
        var named = AppState.Sample(); var twin = new Project { Id = "x", Name = named.Projects[0].Name, Color = "#0F766E" };
        Engine.RestoreProject(named, twin, new Session[0]);
        Check(named.Projects.Count(q => q.Name.StartsWith(twin.Name)) == 2 && named.Projects.Last().Name != twin.Name, "undo renames a project whose name was reused");
        var last = AppState.Sample(); foreach (var q in last.Projects.ToList()) Engine.DeleteProject(last, q.Id, t);
        Store.Validate(last); Check(last.Projects.Count == 0 && Engine.LastProjectId(last) == null, "all projects can be deleted");
        // ---- first run starts empty
        var first = AppState.Fresh();
        Check(first.Projects.Count == 0 && first.Sessions.Count == 0 && first.Active == null && Engine.LastProjectId(first) == null, "first run has no default projects");
        var emptyPath = Path.Combine(Path.GetTempPath(), "rooznegar-empty-" + Guid.NewGuid().ToString("N"));
        try
        {
            var emptyStore = new Store(emptyPath); var loaded = emptyStore.Load(); emptyStore.Save(loaded);
            Check(loaded.Projects.Count == 0 && new Store(emptyPath).Load().Projects.Count == 0, "an empty project list saves and loads");
        }
        finally { if (Directory.Exists(emptyPath)) Directory.Delete(emptyPath, true); }
        Console.WriteLine("ALL " + count + " TESTS PASSED");
    }
}
