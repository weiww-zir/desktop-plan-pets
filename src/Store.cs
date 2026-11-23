using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Xiaohei
{
    internal sealed class PlanStep
    {
        public string Title { get; set; }
        public bool Done { get; set; }
    }

    internal struct TaskSchedule
    {
        internal string Day, CarriedFrom, ReviewedFriday;
        internal bool Alert;
    }

    internal sealed class TaskItem
    {
        internal TaskItem() { Steps = new List<PlanStep>(); }

        public string Id { get; set; }
        internal TaskSchedule Schedule;
        internal bool PreviouslyDone;
        public string Day
        {
            get { return Schedule.Day; }
            set
            {
                if (value != Schedule.Day) Schedule.CarriedFrom = null;
                Schedule.Day = value;
            }
        }
        internal string CarriedFrom { get { return Schedule.CarriedFrom; } }
        public string Title { get; set; }
        public List<PlanStep> Steps { get; private set; }
        public string Notes
        {
            get { return String.Join(Environment.NewLine, Steps.Select(step => step.Title)); }
            set { SetPlan(value); }
        }
        public bool Done { get; set; }

        internal void SetPlan(string text)
        {
            var titles = (text ?? "").Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
                .Where(line => !String.IsNullOrWhiteSpace(line)).ToList();
            var byTitle = Steps.GroupBy(step => step.Title)
                .ToDictionary(group => group.Key, group => new Queue<PlanStep>(group));
            var next = new PlanStep[titles.Count];
            var matched = new HashSet<PlanStep>();
            for (int i = 0; i < titles.Count; i++)
            {
                Queue<PlanStep> candidates;
                if (byTitle.TryGetValue(titles[i], out candidates) && candidates.Count != 0)
                {
                    next[i] = candidates.Dequeue();
                    matched.Add(next[i]);
                }
            }
            for (int i = 0; i < titles.Count; i++)
            {
                if (next[i] != null) continue;
                // When the editor kept the same lines, an unmatched title in
                // the same slot is a rename. Never borrow a moved line's flag.
                bool renamed = titles.Count == Steps.Count && !matched.Contains(Steps[i]);
                next[i] = new PlanStep { Title = titles[i], Done = renamed && Steps[i].Done };
            }
            Steps.Clear();
            Steps.AddRange(next);
        }
    }

    internal sealed class Store
    {
        private const string DayFormat = "yyyy-MM-dd";
        private readonly string file;
        private readonly Func<DateTime> clock;
        private bool saving;
        private readonly Dictionary<string, string> weeks = new Dictionary<string, string>();
        private bool preserveDamagedFile;
        internal readonly List<TaskItem> Tasks = new List<TaskItem>();
        public double PetLeft { get; set; }
        public double PetTop { get; set; }
        internal double PetWidth = 92;
        internal double PlannerWidth = 356;
        internal double PlannerHeight = 500;
        internal bool RecoveredFromBackup { get; private set; }
        internal DateTime Now { get { return clock(); } }
        internal event Action Changed;

        internal Store() : this(Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "XiaoheiPlanner")) { }

        internal Store(string directory) : this(directory, () => DateTime.Now) { }

        internal Store(string directory, Func<DateTime> clock)
        {
            this.clock = clock;
            file = Path.Combine(directory, "planner.xml");
            PetLeft = PetTop = double.NaN;
            if (File.Exists(file))
            {
                try { Load(file); return; }
                catch (InvalidDataException)
                {
                    if (!File.Exists(file + ".bak")) throw;
                    preserveDamagedFile = true;
                }
            }
            if (File.Exists(file + ".bak"))
            {
                Load(file + ".bak");
                RecoveredFromBackup = true;
            }
        }

        private static string MondayKey(DateTime day)
        {
            int daysSinceMonday = ((int)day.DayOfWeek + 6) % 7;
            return day.Date.AddDays(-daysSinceMonday).ToString(DayFormat, CultureInfo.InvariantCulture);
        }

        private static string DateKey(DateTime day)
        {
            return day.ToString(DayFormat, CultureInfo.InvariantCulture);
        }

        private static DateTime LastFriday(DateTime now)
        {
            int offset = ((int)now.DayOfWeek - (int)DayOfWeek.Friday + 7) % 7;
            if (offset == 0 && now.Hour < 18) offset = 7;
            if (now.Date < DateTime.MinValue.AddDays(offset)) return new DateTime(1, 1, 5);
            return now.Date.AddDays(-offset);
        }

        internal bool IsAlert(TaskItem item) { return !item.Done && item.Schedule.Alert; }

        internal bool IsOnDay(TaskItem item, DateTime day, DateTime now)
        {
            string key = DateKey(day);
            return item.Day == key || (now.Hour >= 18 && day.Date == now.Date && item.CarriedFrom == key);
        }

        internal int PendingCount(DateTime now)
        {
            return Tasks.Count(item => !item.Done && IsOnDay(item, now, now));
        }

        internal DateTime? WeekDisplayDay(TaskItem item, DateTime dayInWeek, DateTime now)
        {
            DateTime assigned;
            if (ParseDate(item.Day, out assigned) && MondayKey(assigned) == MondayKey(dayInWeek)) return assigned;
            if (IsOnDay(item, now, now) && MondayKey(now) == MondayKey(dayInWeek)) return now.Date;
            return null;
        }

        internal string WeekNote(DateTime date)
        {
            string note;
            return weeks.TryGetValue(MondayKey(date), out note) ? note : "";
        }

        internal void SetWeekNote(DateTime date, string text)
        {
            string day = MondayKey(date);
            if (String.IsNullOrEmpty(text)) weeks.Remove(day);
            else weeks[day] = text;
        }

        private void Load(string source)
        {
            try
            {
                XDocument document;
                using (var reader = XmlReader.Create(source, new XmlReaderSettings {
                    DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null
                })) document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
                var root = document.Root;
                if (root == null || root.Name != "Planner" || root.Attribute("version") == null)
                    throw new FormatException();
                string version = (string)root.Attribute("version");
                if (version != "1" && version != "2" && version != "3")
                    throw new NotSupportedException("计划文件来自其他版本，请使用相应版本打开。" + Environment.NewLine + source);
                var tasks = root.Element("Tasks");
                var weekly = root.Element("Weeks");
                if (tasks == null || weekly == null) throw new FormatException();
                var items = new List<TaskItem>();
                var ids = new HashSet<string>();
                var lastWrite = File.GetLastWriteTime(source);
                foreach (var element in tasks.Elements())
                {
                    var item = new TaskItem {
                        Id = (string)element.Attribute("id"), Day = (string)element.Attribute("day"),
                        Title = (string)element.Element("Title")
                    };
                    bool done;
                    DateTime date;
                    if (element.Name != "Task" || String.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id)
                        || String.IsNullOrWhiteSpace(item.Title) || !ParseDate(item.Day, out date)
                        || !Boolean.TryParse((string)element.Attribute("done"), out done))
                        throw new FormatException();
                    item.PreviouslyDone = item.Done = done;
                    if (version == "3")
                    {
                        string reviewed = (string)element.Attribute("reviewedFriday");
                        string carried = (string)element.Attribute("carriedFrom");
                        DateTime friday, previous;
                        bool alert;
                        if (!ParseDate(reviewed, out friday) || friday.DayOfWeek != DayOfWeek.Friday
                            || !Boolean.TryParse((string)element.Attribute("alert"), out alert)
                            || (carried != null && (!ParseDate(carried, out previous) || (date - previous).Days != 1)))
                            throw new FormatException();
                        item.Schedule.ReviewedFriday = reviewed;
                        item.Schedule.CarriedFrom = carried;
                        item.Schedule.Alert = alert && !done;
                    }
                    else
                    {
                        // Older files have no creation timestamp. The last save
                        // and the original planned day are the available anchors.
                        var anchor = new[] { lastWrite.Date, date.Date, Now.Date }.Min();
                        item.Schedule.ReviewedFriday = DateKey(LastFriday(anchor));
                    }
                    if (version == "1")
                    {
                        item.SetPlan((string)element.Element("Notes"));
                        foreach (var step in item.Steps) step.Done = done;
                    }
                    else
                    {
                        var steps = element.Element("Steps");
                        if (steps == null) throw new FormatException();
                        foreach (var step in steps.Elements())
                        {
                            bool completed;
                            if (step.Name != "Step" || step.HasElements || String.IsNullOrWhiteSpace(step.Value)
                                || step.Value.IndexOfAny(new[] { '\r', '\n' }) >= 0
                                || !Boolean.TryParse((string)step.Attribute("done"), out completed))
                                throw new FormatException();
                            item.Steps.Add(new PlanStep { Title = step.Value, Done = completed });
                        }
                    }
                    items.Add(item);
                }
                var notes = new Dictionary<string, string>();
                foreach (var element in weekly.Elements())
                {
                    string key = (string)element.Attribute("monday");
                    DateTime date;
                    if (element.Name != "Week" || !ParseDate(key, out date) || date.DayOfWeek != DayOfWeek.Monday
                        || notes.ContainsKey(key)) throw new FormatException();
                    notes.Add(key, element.Value);
                }
                var position = root.Element("Position");
                if (position != null)
                {
                    double left, top;
                    if (ReadNumber(position, "left", out left)) PetLeft = left;
                    if (ReadNumber(position, "top", out top)) PetTop = top;
                }
                var size = root.Element("Size");
                if (size != null)
                {
                    PetWidth = ReadSize(size, "petWidth", PetWidth, 50, 240);
                    PlannerWidth = ReadSize(size, "plannerWidth", PlannerWidth, 300, 4096);
                    PlannerHeight = ReadSize(size, "plannerHeight", PlannerHeight, 270, 4096);
                }
                Tasks.AddRange(items);
                foreach (var note in notes) weeks.Add(note.Key, note.Value);
            }
            catch (XmlException ex) { throw InvalidFile(source, ex); }
            catch (FormatException ex) { throw InvalidFile(source, ex); }
        }

        private static bool ParseDate(string value, out DateTime day)
        {
            return DateTime.TryParseExact(value, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
        }

        private static double ReadSize(XElement size, string name, double fallback, double min, double max)
        {
            double number;
            return ReadNumber(size, name, out number) ? Math.Max(min, Math.Min(max, number)) : fallback;
        }

        private static bool ReadNumber(XElement element, string name, out double value)
        {
            return Double.TryParse((string)element.Attribute(name), NumberStyles.Float,
                CultureInfo.InvariantCulture, out value) && !Double.IsInfinity(value) && !Double.IsNaN(value);
        }

        private static InvalidDataException InvalidFile(string path, Exception cause)
        {
            return new InvalidDataException("计划文件内容损坏，请检查文件或备份。" + Environment.NewLine + path, cause);
        }

        internal void Save()
        {
            if (saving) throw new InvalidOperationException("手帐正在保存，请稍后重试。");
            Commit(Now, true);
        }

        internal bool ApplySchedule(DateTime now)
        {
            return !saving && Commit(now, false);
        }

        private bool Commit(DateTime now, bool forceSave)
        {
            string friday = DateKey(LastFriday(now));
            bool fridayEvening = now.DayOfWeek == DayOfWeek.Friday && now.Hour >= 18;
            DateTime target = now.Date;
            if (now.Hour >= 18 && target < DateTime.MaxValue.Date) target = target.AddDays(1);
            string targetKey = DateKey(target);
            string previousKey = target == DateTime.MinValue ? null : DateKey(target.AddDays(-1));
            var states = new List<TaskSchedule>(Tasks.Count);
            bool changed = false;
            foreach (var item in Tasks)
            {
                var before = item.Schedule;
                var next = before;
                bool missedFriday = before.ReviewedFriday != null
                    && String.CompareOrdinal(before.ReviewedFriday, friday) < 0;
                if (before.ReviewedFriday == null || missedFriday) next.ReviewedFriday = friday;
                next.Alert = !item.Done && (before.Alert || fridayEvening || (missedFriday && !item.PreviouslyDone));
                if (!item.Done && String.CompareOrdinal(before.Day, targetKey) < 0)
                {
                    next.Day = targetKey;
                    next.CarriedFrom = previousKey;
                    next.Alert = true;
                }
                else if (!item.Done && next.CarriedFrom != null && next.Day == targetKey)
                    next.Alert = true;
                if (!next.Equals(before)) changed = true;
                states.Add(next);
            }
            if (!changed && !forceSave) return false;
            saving = true;
            try
            {
                // Commit derived dates and alerts in memory only after the
                // replacement succeeds. A failed write can be retried intact.
                WriteFile(states);
                for (int i = 0; i < Tasks.Count; i++)
                {
                    Tasks[i].Schedule = states[i];
                    Tasks[i].PreviouslyDone = Tasks[i].Done;
                }
                var handlers = Changed;
                if (handlers != null)
                    foreach (Action notify in handlers.GetInvocationList())
                    {
                        try { notify(); }
                        catch (Exception ex) { System.Diagnostics.Trace.TraceError(ex.ToString()); }
                    }
                return changed;
            }
            finally { saving = false; }
        }

        private void WriteFile(List<TaskSchedule> states)
        {
            var root = new XElement("Planner", new XAttribute("version", "3"),
                new XElement("Size", new XAttribute("petWidth", PetWidth),
                    new XAttribute("plannerWidth", PlannerWidth), new XAttribute("plannerHeight", PlannerHeight)),
                new XElement("Tasks", Tasks.Select((item, i) => new XElement("Task",
                    new XAttribute("id", item.Id), new XAttribute("day", states[i].Day), new XAttribute("done", item.Done),
                    new XAttribute("reviewedFriday", states[i].ReviewedFriday), new XAttribute("alert", states[i].Alert),
                    states[i].CarriedFrom == null ? null : new XAttribute("carriedFrom", states[i].CarriedFrom),
                    new XElement("Title", item.Title), new XElement("Steps", item.Steps.Select(step =>
                        new XElement("Step", new XAttribute("done", step.Done), step.Title)))))),
                new XElement("Weeks", weeks.Select(note => new XElement("Week",
                    new XAttribute("monday", note.Key), note.Value))));
            if (!Double.IsNaN(PetLeft) && !Double.IsNaN(PetTop) && !Double.IsInfinity(PetLeft) && !Double.IsInfinity(PetTop))
                root.Add(new XElement("Position", new XAttribute("left", PetLeft), new XAttribute("top", PetTop)));
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    using (var writer = XmlWriter.Create(stream, new XmlWriterSettings {
                        Indent = true, NewLineHandling = NewLineHandling.Entitize
                    })) new XDocument(root).Save(writer);
                    stream.Flush(true);
                }
                if (File.Exists(file))
                {
                    string backup = preserveDamagedFile ? file + ".damaged-" + Guid.NewGuid().ToString("N") : file + ".bak";
                    File.Replace(temp, file, backup);
                }
                else File.Move(temp, file);
                preserveDamagedFile = false;
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
    }
}
