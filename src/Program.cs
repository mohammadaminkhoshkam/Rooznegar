using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Rooznegar")]
[assembly: AssemblyDescription("Persian project time tracker")]
[assembly: AssemblyCompany("Rooznegar")]
[assembly: AssemblyProduct("Rooznegar")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

namespace Rooznegar
{
    internal static class Program
    {
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
        [STAThread] public static int Main(string[] args)
        {
            bool preview = args.Contains("--preview") || args.Contains("--smoke");
            if (Environment.OSVersion.Platform == PlatformID.Win32NT) try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Ui.InitScale(); Fonts.Init();
            // Last line of defence: unexpected errors are logged and reported instead of killing the timer.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                ErrorLog.Write("UI", e.Exception);
                if (preview) { Console.Error.WriteLine(e.Exception); return; }
                try { Dialogs.Message(null, "خطای پیش‌بینی‌نشده", "مشکلی پیش آمد، اما داده‌ها و تایمر حفظ شده‌اند. جزئیات در فایل errors.log در پوشهٔ داده‌ها ثبت شد.\n\n" + e.Exception.Message, true); } catch { }
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => { var ex = e.ExceptionObject as Exception; if (ex != null) ErrorLog.Write("Fatal", ex); };
            bool first; using (var mutex = new Mutex(true, "Rooznegar-1-" + Environment.UserName, out first))
            {
                if (!first && !preview) { Palette.Apply(false); Dialogs.Message(null, "روزنگار در حال اجراست", "نشان روزنگار را کنار ساعت ویندوز پیدا کنید و روی آن کلیک کنید."); return 0; }
                try
                {
                    string root = preview ? Path.Combine(Path.GetTempPath(), "rooznegar-preview-" + Guid.NewGuid().ToString("N")) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rooznegar");
                    if (preview) ErrorLog.Directory = root;
                    var store = new Store(root); var state = preview ? Demo() : store.Load();
                    if (!preview && state.Active == null) store.Save(state);
                    using (var form = new MainForm(store, state, preview))
                    {
                        if (preview) form.Shown += (s, e) => form.BeginInvoke(new Action(() => { try { if (args.Contains("--smoke")) { form.Smoke(); Console.WriteLine("UI SMOKE PASS"); } else form.CapturePreviews(args.Length > 1 ? args[1] : "preview"); } catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; } finally { form.Close(); } }));
                        Application.Run(form);
                    }
                    if (preview && Directory.Exists(root)) Directory.Delete(root, true);
                    return Environment.ExitCode;
                }
                catch (Exception e) { ErrorLog.Write("Startup", e); if (preview) Console.Error.WriteLine(e); else MessageBox.Show("برنامه اجرا نشد. داده‌های موجود حفظ شده‌اند.\n\n" + e.Message, "روزنگار", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
                finally { if (first) mutex.ReleaseMutex(); }
            }
        }
        private static AppState Demo()
        {
            var s = AppState.Sample(); DateTime day = DateTime.Today; var zone = TimeZoneInfo.Local;
            s.Projects.Add(new Project { Id = Guid.NewGuid().ToString("N"), Name = "پایان‌نامه", Color = "#6457D6", WeeklyGoalMinutes = 600 });
            s.Projects.Add(new Project { Id = Guid.NewGuid().ToString("N"), Name = "ورزش و سلامتی", Color = "#2F9D63" });
            s.Projects.Add(new Project { Id = Guid.NewGuid().ToString("N"), Name = "پروژهٔ قدیمی", Color = "#5D6B82", Archived = true });
            s.Projects[0].WeeklyGoalMinutes = 300;
            string[] notes = { "تمرین و مطالعه", "تحلیل داده‌ها و تهیهٔ گزارش", "برنامه‌ریزی هفتگی", "نوشتن فصل سوم", "پیاده‌روی" };
            var rnd = new Random(7);
            for (int ago = 120; ago >= 1; ago--)
            {
                DateTime d = day.AddDays(-ago);
                if (rnd.Next(10) < (d.DayOfWeek == DayOfWeek.Friday ? 6 : 1)) continue;
                DateTime t = d.AddHours(8).AddMinutes(rnd.Next(40));
                for (int p = 0; p < 5; p++)
                {
                    if (rnd.Next(10) < 3) continue;
                    int minutes = 20 + rnd.Next(p == 3 ? 150 : 100);
                    DateTime e = t.AddMinutes(minutes);
                    s.Sessions.Add(new Session { Id = Guid.NewGuid().ToString("N"), ProjectId = s.Projects[p].Id, StartUtc = Dates.Utc(t, zone), EndUtc = Dates.Utc(e, zone), Note = notes[p], Manual = rnd.Next(8) == 0 });
                    t = e.AddMinutes(10 + rnd.Next(40));
                }
            }
            long now = DateTime.UtcNow.Ticks;
            // Today's activities stay in the past even when screenshots run early in the day.
            for (int p = 0; p < 3; p++)
            {
                long from = now - TimeSpan.TicksPerHour * (7 - 2 * p);
                if (Dates.Local(from, zone).Date == day)
                    s.Sessions.Add(new Session { Id = Guid.NewGuid().ToString("N"), ProjectId = s.Projects[p == 2 ? 3 : p].Id, StartUtc = from, EndUtc = from + TimeSpan.TicksPerMinute * (67 + p * 17), Note = p == 0 ? "مرور واژگان و تمرین شنیداری" : p == 1 ? "تحلیل داده‌ها و تهیهٔ گزارش" : "", Manual = p == 2 });
            }
            s.Active = new RunningSession { ProjectId = s.Projects[0].Id, StartUtc = now - TimeSpan.TicksPerMinute * 25 - TimeSpan.TicksPerSecond * 17, LastSeenUtc = now, Note = "تمرین مکالمه با پادکست" };
            s.Settings.FocusMinutes = 50;
            return s;
        }
    }
}
