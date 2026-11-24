using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
namespace Xiaohei
{
    internal class StoreCheck
    {
        static void Check(bool ok) { if (!ok) throw new Exception("Check failed"); }

        static void CheckStepMigration(string directory)
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "planner.xml");
            var task = new XElement("Task", new XAttribute("id", "legacy"), new XAttribute("day", "2026-11-23"),
                new XAttribute("done", true), new XElement("Title", "老任务"),
                new XElement("Notes", "  阅读 🐈  \n\n复核\n\t \n复核"));
            var other = new XElement(task);
            other.SetAttributeValue("id", "other");
            other.SetAttributeValue("done", false);
            new XDocument(new XElement("Planner", new XAttribute("version", "1"),
                new XElement("Tasks", task, other), new XElement("Weeks"))).Save(path);
            var store = new Store(directory);
            var item = store.Tasks[0];
            Check(!item.IsLongTerm && item.Steps.Count == 3 && item.Steps.All(s => s.Done));
            Check(store.Tasks[1].Steps.All(s => !s.Done) && item.Steps[0].Title == "  阅读 🐈  ");
            Check(item.Notes == "  阅读 🐈  \r\n复核\r\n复核");
            item.Steps[1].Done = false;
            item.SetPlan("复核\r\n  阅读 🐈  \r复核\n新增");
            Check(item.Steps.Select(s => s.Done).SequenceEqual(new[] { false, true, true, false }));
            item.SetPlan("复核\n  阅读 🐈  \n二次复核\n新增");
            Check(item.Steps[2].Title == "二次复核" && item.Steps[2].Done);
            item.Done = false;
            store.Save();
            Check((string)XDocument.Load(path).Root.Attribute("version") == "3");
            Check((string)XDocument.Load(path + ".bak").Root.Attribute("version") == "1");
            var saved = new Store(directory).Tasks[0];
            Check(!saved.Done && saved.Notes == item.Notes);
            Check(saved.Steps.Select(s => s.Done).SequenceEqual(new[] { false, true, true, false }));
            var corrupt = XDocument.Load(path);
            corrupt.Descendants("Step").First().SetAttributeValue("done", "???");
            corrupt.Save(path);
            var recovered = new Store(directory);
            Check(recovered.RecoveredFromBackup && recovered.Tasks[0].Steps.All(s => s.Done));
        }

        static void CheckLongTerm(string directory)
        {
            DateTime now = new DateTime(2026, 11, 26, 17, 0, 0);
            var store = new Store(directory, () => now);
            var task = new TaskItem { Id = "converted", Day = "2026-11-26", Title = "读书计划", Notes = "选书\n阅读" };
            task.Steps[0].Done = true;
            store.Tasks.Add(task);
            store.Save();
            now = new DateTime(2026, 11, 27, 18, 0, 0);
            store.ApplySchedule(now);
            Check(store.IsAlert(task) && store.PendingCount(now) == 1 && task.CarriedFrom != null);
            var original = task.Schedule;
            string path = Path.Combine(directory, "planner.xml");
            task.IsLongTerm = true;
            using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                try { store.Save(); throw new Exception("Expected sharing violation"); } catch (IOException) { }
            }
            task.IsLongTerm = false;
            Check(task.Schedule.Equals(original) && store.IsAlert(task) && store.IsOnDay(task, now, now));

            task.IsLongTerm = true;
            var finished = new TaskItem { Id = "undated", IsLongTerm = true, Title = "已读书目", Done = true };
            var dated = new TaskItem { Id = "dated", Day = "2026-12-01", Title = "日历任务" };
            store.Tasks.Add(finished);
            store.Tasks.Add(dated);
            store.Save();
            Check(task.Schedule.Equals(default(TaskSchedule)) && task.Day == null && !task.Done);
            Check(!store.IsOnDay(task, now, now) && !store.IsOnDay(task, now.AddDays(1), now));
            Check(!store.IsAlert(task) && store.PendingCount(now) == 0 && store.WeekDisplayDay(task, now, now) == null);
            var file = XDocument.Load(path);
            Check((string)file.Root.Attribute("version") == "4");
            Check(file.Descendants("LongTermTask").Count() == 2 && file.Descendants("LongTermTask").All(
                item => item.Attributes().All(a => a.Name == "id" || a.Name == "done")));
            var loaded = new Store(directory, () => now);
            var restored = loaded.Tasks.Single(t => t.Id == task.Id);
            Check(restored.IsLongTerm && restored.Day == null && restored.Title == task.Title && !restored.Done);
            Check(restored.Steps.Select(s => s.Done).SequenceEqual(new[] { true, false }) && restored.Notes == task.Notes);
            Check(loaded.Tasks.Single(t => t.Id == finished.Id).IsLongTerm && loaded.Tasks.Single(t => t.Id == finished.Id).Done);
            var regular = loaded.Tasks.Single(t => t.Id == dated.Id);
            Check(!regular.IsLongTerm && regular.Day == dated.Day && loaded.IsAlert(regular));
            now = now.AddDays(21);
            Check(loaded.ApplySchedule(now) && restored.Schedule.Equals(default(TaskSchedule)) && restored.Day == null);
            Check(loaded.PendingCount(now) == 1 && !loaded.IsAlert(restored) && !loaded.ApplySchedule(now));
        }

        static void Main()
        {
            string directory = Path.Combine("work", "store-check-" + Guid.NewGuid().ToString("N"));
            CheckStepMigration(Path.Combine(directory, "legacy-steps"));
            CheckLongTerm(Path.Combine(directory, "long-term"));
            var a = new Store(directory);
            var friday = new DateTime(2027,1,1);
            Check(a.PetWidth == 92 && a.PlannerWidth == 356 && a.PlannerHeight == 500);
            Check(!a.CalendarCollapsed && !a.DayListCollapsed && a.LongTermListCollapsed);
            a.CalendarCollapsed = true; a.DayListCollapsed = true; a.LongTermListCollapsed = false;
            a.PetWidth = 109.5; a.PlannerWidth = 370; a.PlannerHeight = 521.25;
            a.SetWeekNote(friday, "跨年计划\r\n复查");
            a.PetLeft = -1050.25; a.PetTop = 56.5;
            a.Tasks.Add(new TaskItem { Id = "x", Day = "2027-01-01", Title = "今天 🐈", Notes = "中文 & <换行>\r\n下一步" });
            a.Save();
            var b = new Store(directory);
            Check(b.CalendarCollapsed && b.DayListCollapsed && !b.LongTermListCollapsed);
            b.CalendarCollapsed = false;
            Check(b.PetWidth == a.PetWidth && b.PlannerWidth == a.PlannerWidth && b.PlannerHeight == a.PlannerHeight);
            Check(b.WeekNote(new DateTime(2026,12,28)) == "跨年计划\r\n复查");
            Check(b.WeekNote(new DateTime(2027,1,3)) == "跨年计划\r\n复查");
            Check(b.WeekNote(new DateTime(2027,1,4)) == "");
            Check(b.PetLeft == a.PetLeft && b.PetTop == a.PetTop && b.Tasks[0].Title == a.Tasks[0].Title && b.Tasks[0].Notes == a.Tasks[0].Notes);
            b.Tasks[0].Done = true; b.Save();
            var expanded = new Store(directory);
            Check(expanded.Tasks[0].Done && !expanded.CalendarCollapsed && expanded.DayListCollapsed && !expanded.LongTermListCollapsed);
            string path = Path.Combine(directory, "planner.xml");
            File.WriteAllText(path, "broken");
            var c = new Store(directory);
            Check(c.RecoveredFromBackup && !c.Tasks[0].Done);
            c.Save();
            Check(File.ReadAllText(Directory.GetFiles(directory, "*.damaged-*").Single()) == "broken");
            Check(!new Store(directory).RecoveredFromBackup);
            var xml = XDocument.Load(path);
            var size = xml.Root.Element("Size");
            size.Attribute("calendarCollapsed").Remove();
            size.Attribute("longTermListCollapsed").Remove();
            size.Attribute("dayListCollapsed").Remove();
            xml.Save(path);
            var legacy = new Store(directory);
            Check(!legacy.CalendarCollapsed && !legacy.DayListCollapsed && legacy.LongTermListCollapsed);
            size.Remove();
            xml.Save(path);
            var old = new Store(directory);
            Check(old.PetWidth == 92 && old.PlannerWidth == 356 && old.PlannerHeight == 500);
            Check(!old.RecoveredFromBackup && old.Tasks.Single().Title == a.Tasks[0].Title && old.PetLeft == a.PetLeft);
            xml.Root.Add(size);
            size.SetAttributeValue("petWidth", "NaN");
            size.SetAttributeValue("plannerHeight", "not-a-number");
            xml.Save(path);
            var partial = new Store(directory);
            Check(!partial.RecoveredFromBackup && partial.PetWidth == 92 && partial.PlannerWidth == 370 && partial.PlannerHeight == 500);
            Check(partial.Tasks.Single().Title == a.Tasks[0].Title && partial.WeekNote(friday).StartsWith("跨年计划"));
            size.SetAttributeValue("petWidth", 700);
            size.SetAttributeValue("plannerWidth", 1);
            size.SetAttributeValue("plannerHeight", 99999);
            xml.Save(path);
            var clamped = new Store(directory);
            Check(clamped.PetWidth == 240 && clamped.PlannerWidth == 300 && clamped.PlannerHeight == 4096);
            File.WriteAllText(path, "<Planner version='5' />");
            try { new Store(directory); throw new Exception("Unknown version loaded"); } catch(NotSupportedException) { }
            File.WriteAllText(path, "invalid");
            File.WriteAllText(path + ".bak", "invalid2");
            try { new Store(directory); throw new Exception("Corruption missed"); } catch(InvalidDataException) { }
            Check(File.ReadAllText(path) == "invalid" && File.ReadAllText(path + ".bak") == "invalid2");
            Console.WriteLine("Passed legacy step migration, duplicate/renamed/reordered step editing, mixed completion roundtrip, backup recovery, Unicode, week boundaries and size preferences.");
        }
    }
}
