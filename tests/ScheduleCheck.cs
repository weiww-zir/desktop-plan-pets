using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Xiaohei
{
    internal static class ScheduleCheck
    {
        private static void Check(bool value, string message)
        {
            if (!value) throw new Exception(message);
        }

        private static TaskItem Add(Store store, string id, string day, bool done)
        {
            var task = new TaskItem { Id = id, Title = id, Day = day, Done = done };
            store.Tasks.Add(task);
            return task;
        }

        private static void Main()
        {
            string dir = Path.Combine("work", "schedule-check-" + Guid.NewGuid().ToString("N"));
            DateTime now = new DateTime(2026, 12, 31, 17, 59, 59); // Thursday
            var weekday = new Store(Path.Combine(dir, "weekday"), () => now);
            var december = Add(weekday, "dec", "2026-12-31", false);
            var later = Add(weekday, "tomorrow", "2027-01-01", false);
            weekday.Save();
            Check(!weekday.IsAlert(december) && !weekday.IsAlert(later), "Thursday before cutoff");
            now = now.AddSeconds(1);
            Check(weekday.ApplySchedule(now) && weekday.IsAlert(december) && december.Day == "2027-01-01"
                && !weekday.IsAlert(later), "daily year-end rollover marks only carried task");
            december.Done = true;
            weekday.Save();
            Check(!weekday.IsAlert(december) && weekday.PendingCount(now) == 0, "daily completion");
            december.Done = false;
            weekday.Save();
            Check(weekday.IsAlert(december) && weekday.PendingCount(now) == 1, "reopened tonight's carried item");
            now = new DateTime(2027, 1, 1, 9, 0, 0);
            weekday = new Store(Path.Combine(dir, "weekday"), () => now);
            Check(!weekday.ApplySchedule(now) && weekday.IsAlert(weekday.Tasks[0]) && !weekday.IsAlert(weekday.Tasks[1])
                && weekday.PendingCount(now) == 2, "Friday morning red persists across restart");
            now = new DateTime(2027, 1, 1, 17, 59, 59);
            var store = new Store(dir, () => now);
            var due = Add(store, "due", "2027-01-01", false);
            due.SetPlan("一\n二");
            due.Steps[0].Done = true;
            var future = Add(store, "future", "2027-02-03", false);
            var complete = Add(store, "completed", "2027-01-01", true);
            int events = 0;
            store.Changed += delegate { events++; Check(!store.ApplySchedule(now), "reentry"); };
            store.Save();
            Check(events == 1 && store.Now == now && store.PendingCount(now) == 1, "17:59 totals");
            Check(!store.IsAlert(due) && !store.ApplySchedule(now), "no premature flags");
            now = now.AddSeconds(1);
            Check(store.ApplySchedule(now) && events == 2 && !store.ApplySchedule(now), "once at 18:00");
            Check(due.Day == "2027-01-02" && due.CarriedFrom == "2027-01-01"
                && due.Steps[0].Done && !due.Steps[1].Done, "roll whole partial task once");
            Check(store.IsAlert(due) && store.IsAlert(future) && future.Day == "2027-02-03", "Friday includes future");
            Check(!store.IsAlert(complete) && complete.Day == "2027-01-01", "completed stays");
            Check(store.IsOnDay(due, now.Date, now) && store.IsOnDay(due, now.AddDays(1), now)
                && store.PendingCount(now) == 1, "tonight and tomorrow");
            Check(store.WeekDisplayDay(due, now, now) == now.Date.AddDays(1), "same-week dedup");
            var fridayNew = Add(store, "friday evening", "2027-02-04", false);
            store.Save();
            Check(store.IsAlert(fridayNew), "Friday evening new task");
            future.Done = true;
            store.Save();
            Check(!store.IsAlert(future), "completion clears alert");
            future.Done = false;
            store.Save();
            Check(store.IsAlert(future), "reopen same Friday evening");
            future.Done = true;
            store.Save();
            now = new DateTime(2027, 1, 2);
            future.Done = false;
            var saturdayNew = Add(store, "sat-new", "2027-03-01", false);
            store.Save();
            Check(!store.IsAlert(future) && !store.IsAlert(saturdayNew), "Saturday no stale red");
            Check(!store.IsOnDay(due, now.AddDays(-1), now) && store.IsOnDay(due, now, now)
                && store.PendingCount(now) == 1, "midnight alias expires");
            store = new Store(dir, () => now);
            Check(!store.ApplySchedule(now) && store.Tasks.Count == 5, "restart idempotent");
            due = store.Tasks.Single(t => t.Id == "due");
            now = new DateTime(2027, 1, 3, 18, 0, 0);
            Check(store.ApplySchedule(now) && due.Day == "2027-01-04", "weekend roll and catch up");
            Check(store.WeekDisplayDay(due, now, now) == now.Date
                && store.WeekDisplayDay(due, now.AddDays(1), now) == now.Date.AddDays(1), "Sunday week crossover");
            now = new DateTime(2027, 1, 25, 9, 30, 0);
            store = new Store(dir, () => now);
            due = store.Tasks.Single(t => t.Id == "due");
            Check(store.ApplySchedule(now) && !store.ApplySchedule(now) && due.Day == "2027-01-25"
                && due.Steps[0].Done && store.Tasks.Count == 5, "offline several weeks");
            Check(store.IsAlert(store.Tasks.Single(t => t.Id == "sat-new")), "missed Friday catches future task");

            int beforeFailure = 0;
            store.Changed += delegate { beforeFailure++; };
            now = now.Date.AddHours(18);
            using (File.Open(Path.Combine(dir, "planner.xml"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                try { store.ApplySchedule(now); throw new Exception("expected file lock"); }
                catch (IOException) { }
            }
            Check(due.Day == "2027-01-25" && beforeFailure == 0, "failed write rollback");
            Check(store.ApplySchedule(now) && due.Day == "2027-01-26" && beforeFailure == 1, "retry");
            var reload = new Store(dir, () => now);
            Check(!reload.ApplySchedule(now) && reload.PendingCount(now) == 1, "durable evening alias");

            string legacy = Path.Combine(dir, "v2");
            Directory.CreateDirectory(legacy);
            string path = Path.Combine(legacy, "planner.xml");
            File.WriteAllText(path, "<Planner version='2'><Tasks><Task id='legacy' day='2027-03-01' done='false'><Title>旧手帐</Title><Steps><Step done='true'>已读</Step></Steps></Task></Tasks><Weeks/></Planner>");
            File.SetLastWriteTime(path, new DateTime(2027, 1, 21, 9, 0, 0));
            var migrated = new Store(legacy, () => now);
            Check(migrated.ApplySchedule(now) && migrated.IsAlert(migrated.Tasks[0]) && migrated.Tasks[0].Steps[0].Done
                && migrated.Tasks[0].Day == "2027-03-01" && (string)XDocument.Load(path).Root.Attribute("version") == "3",
                "pre-Friday legacy future task and its step");
            Console.WriteLine("Passed cutoff, Jan/year rollover, badge, Friday late tasks, weekend/offline catch-up, weekly dedup, red lifecycle, migration, persistence and failed-write retry.");
        }
    }
}
