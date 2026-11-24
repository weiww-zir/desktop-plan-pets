using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Xiaohei
{
    internal static class CalendarFoldCheck
    {
        private static readonly Size NarrowWindow = new Size(300, 500);
        private static readonly CultureInfo Zh = CultureInfo.GetCultureInfo("zh-CN");

        private static IEnumerable<FrameworkElement> VisibleChildren(DependencyObject root)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<FrameworkElement>())
            {
                if (child.Visibility != Visibility.Visible) continue;
                yield return child;
                foreach (var descendant in VisibleChildren(child)) yield return descendant;
            }
        }

        private static Button FindButton(Planner planner, string name)
        {
            return VisibleChildren((FrameworkElement)planner.Content).OfType<Button>().SingleOrDefault(button =>
                AutomationProperties.GetName(button) == name || (button.Content as string) == name);
        }

        private static void Require(bool result, string message)
        {
            if (!result) throw new InvalidOperationException(message);
        }

        private static void Layout(Planner planner, Size? requestedSize = null)
        {
            var page = (FrameworkElement)planner.Content;
            var size = requestedSize ?? NarrowWindow;
            page.Measure(size);
            page.Arrange(new Rect(size));
            page.UpdateLayout();
        }

        private static void Click(Planner planner, string name)
        {
            var button = FindButton(planner, name);
            Require(button != null && button.IsEnabled, "Missing or disabled button: " + name);
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Layout(planner);
        }

        private static Rect Bounds(Planner planner, string name)
        {
            var button = FindButton(planner, name);
            Require(button != null, "Missing button: " + name);
            return new Rect(button.TranslatePoint(new Point(), (FrameworkElement)planner.Content), button.RenderSize);
        }

        private static TextBox Field(Planner planner, string name)
        {
            return VisibleChildren((FrameworkElement)planner.Content).OfType<TextBox>()
                .Single(box => AutomationProperties.GetName(box) == name);
        }

        private static void CheckNarrowHeader(Planner planner)
        {
            var buttons = new[] { "收起月历", "上个月", "今天", "下个月" }.Select(name => Bounds(planner, name)).ToArray();
            foreach (var button in buttons)
                Require(button.Width > 0 && button.Height > 0 && button.Left >= -0.5 && button.Right <= NarrowWindow.Width + 0.5,
                    "A calendar control is clipped at 300 DIP");
            for (int i = 0; i < buttons.Length; i++)
                for (int j = i + 1; j < buttons.Length; j++)
                {
                    var overlap = Rect.Intersect(buttons[i], buttons[j]);
                    Require(overlap.IsEmpty || overlap.Width < 0.5 || overlap.Height < 0.5, "Overlapping calendar buttons");
                }
        }

        private static void CheckCalendarDate(Planner planner, DateTime expected)
        {
            var header = FindButton(planner, "收起月历") ?? FindButton(planner, "展开月历");
            var labels = VisibleChildren(header).OfType<TextBlock>().ToArray();
            var date = labels.Single(label => label.Text == expected.ToString("yyyy年M月d日", Zh));
            var weekday = labels.Single(label => label.Text == expected.ToString("dddd", Zh));
            var area = new Rect(header.RenderSize);
            area.Inflate(0.5, 0.5);
            var dateBounds = date.TransformToAncestor(header).TransformBounds(new Rect(date.RenderSize));
            var weekBounds = weekday.TransformToAncestor(header).TransformBounds(new Rect(weekday.RenderSize));
            Require(area.Contains(dateBounds) && area.Contains(weekBounds) && weekBounds.Left >= dateBounds.Right &&
                weekBounds.Width > 28, "Date or weekday is clipped in the month header");
        }

        private static void CheckShortWindowNavigation(string folder)
        {
            var size = new Size(300, 270);
            var today = new DateTime(2026, 11, 30, 10, 0, 0); // sixth calendar row
            var store = new Store(folder, () => today);
            store.LongTermListCollapsed = false;
            for (int i = 0; i < 125; i++)
            {
                store.Tasks.Add(new TaskItem { Id = "d" + i, Day = "2026-11-30", Title = "摘录第" + i + "页" });
                store.Tasks.Add(new TaskItem { Id = "l" + i, Title = "整理第" + i + "章", IsLongTerm = true });
            }
            store.Tasks.Add(new TaskItem { Id = "next", Day = "2026-12-01", Title = "归还图书" });
            var planner = new Planner(store) { Width = size.Width, Height = size.Height };
            Layout(planner, size);
            var root = (FrameworkElement)planner.Content;
            var scrolls = VisibleChildren(root).OfType<ScrollViewer>();
            var tasks = scrolls.Single(s => AutomationProperties.GetName(s) == "任务清单");
            var dates = scrolls.Single(s => AutomationProperties.GetName(s) == "月历日期");
            Func<FrameworkElement, Button, bool> visible = (view, button) => {
                var bounds = new Rect(view.RenderSize);
                bounds.Inflate(0.7, 0.7);
                return button != null && bounds.Contains(button.TransformToAncestor(view).TransformBounds(new Rect(button.RenderSize)));
            };
            Func<DateTime, Button> day = d => VisibleChildren(root).OfType<Button>().Single(b =>
                AutomationProperties.GetName(b).Split('，')[0] == d.ToString("M月d日 dddd", Zh));
            Require(tasks.VerticalOffset < 1 && visible(tasks, FindButton(planner, "收起长期任务")),
                "Opening a short planner must preserve the beginning of the task list");
            for (int i = 0; i < 7; i++)
                Require(visible(dates, day(today.AddDays(i))), "The last week is clipped at day " + i);
            foreach (var sample in new[] {
                new { Date = new DateTime(2026, 10, 26), Text = "十七" },
                new { Date = new DateTime(2026, 11, 24), Text = "十六" },
                new { Date = new DateTime(2026, 12, 1), Text = "廿三" }
            })
            {
                var cell = day(sample.Date);
                var caption = ((Grid)cell.Content).Children.OfType<TextBlock>().Single(t => t.Text == sample.Text);
                Require(new Rect(cell.RenderSize).Contains(InkBounds(caption, cell)) &&
                    AutomationProperties.GetHelpText(cell) == "农历" + sample.Text, "Lunar caption is missing or clipped");
            }
            Require(tasks.ScrollableHeight > 0 && dates.ScrollableHeight > 0, "Both long lists support scrolling");
            dates.ScrollToTop();
            Layout(planner, size);
            Require(visible(dates, day(new DateTime(2026, 10, 26))) && tasks.VerticalOffset < 1,
                "The first week is reachable without moving the task list");
            dates.ScrollToEnd();
            Layout(planner, size);
            Require(visible(dates, day(today)) && tasks.VerticalOffset < 1, "The last week is reachable");
            foreach (var name in new[] { "下个月", "下个月", "上个月", "上个月" })
            {
                var button = FindButton(planner, name);
                Require(visible(root, button), "Month navigation left the window: " + name);
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Layout(planner, size);
            }
            var target = day(today.AddDays(1));
            Require(visible(dates, target), "Returning to this month should reveal the selected week");
            target.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Layout(planner, size);
            Require(tasks.VerticalOffset > 0 && visible(tasks, FindButton(planner, "收起当日任务")),
                "Picking a date should reveal its daily section after the long-term list");
            FindButton(planner, "收起月历").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Layout(planner, size);
            double foldedTaskSpace = tasks.ActualHeight;
            FindButton(planner, "展开月历").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Layout(planner, size);
            Require(foldedTaskSpace > tasks.ActualHeight && visible(dates, day(today.AddDays(1))),
                "Folding releases task space; expanding reveals the chosen week");
            var festival = new DateTime(2027, 2, 6);
            var quiet = new Planner(new Store(Path.Combine(folder, "festival"), () => festival));
            foreach (var capacity in new[] { size, new Size(520, 800) })
            {
                Layout(quiet, capacity);
                var calendar = VisibleChildren((FrameworkElement)quiet.Content).OfType<ScrollViewer>()
                    .Single(s => AutomationProperties.GetName(s) == "月历日期");
                var cells = VisibleChildren((FrameworkElement)calendar.Content).OfType<Button>();
                for (int i = -5; i <= 1; i++)
                {
                    var date = festival.AddDays(i);
                    Require(visible(calendar, cells.Single(b => AutomationProperties.GetName(b).Split('，')[0] ==
                        date.ToString("M月d日 dddd", Zh))), "A holiday week is clipped");
                }
                if (capacity.Height > 500)
                {
                    var taskView = VisibleChildren((FrameworkElement)quiet.Content).OfType<ScrollViewer>()
                        .Single(s => AutomationProperties.GetName(s) == "任务清单");
                    double bottom = taskView.TranslatePoint(new Point(0, taskView.ActualHeight), (FrameworkElement)quiet.Content).Y;
                    double gap = Bounds(quiet, "收起月历").Top - bottom;
                    Require(calendar.ScrollableHeight <= 1 && gap >= 0 && gap < 15,
                        "A roomy planner should show the whole month immediately after its tasks");
                }
            }
            quiet.Close();
            planner.Close();
        }

        private static Rect InkBounds(TextBlock label, Button cell)
        {
            var glyphs = new FormattedText(label.Text, Zh, label.FlowDirection,
                new Typeface(label.FontFamily, label.FontStyle, label.FontWeight, label.FontStretch),
                label.FontSize, label.Foreground, VisualTreeHelper.GetDpi(label).PixelsPerDip)
                { MaxTextWidth = label.ActualWidth, TextAlignment = label.TextAlignment };
            return label.TransformToAncestor(cell).TransformBounds(glyphs.BuildGeometry(new Point()).Bounds);
        }

        private static void CheckHolidayData()
        {
            var lunarDates = new Dictionary<string, string> {
                { "20250217", "二十" }, { "20250625", "六月" }, { "20250725", "闰六月" }, { "20250823", "七月" },
                { "20260119", "腊月" }, { "20260217", "正月" }, { "20260319", "二月" }, { "20261109", "十月" },
                { "20261118", "初十" }, { "20261124", "十六" }, { "20261128", "二十" }, { "20261129", "廿一" },
                { "20261208", "三十" }, { "20261209", "冬月" }
            };
            foreach (var sample in lunarDates)
            {
                var date = DateTime.ParseExact(sample.Key, "yyyyMMdd", CultureInfo.InvariantCulture);
                Require(HolidayCalendar.LunarLabel(date) == sample.Value, "Incorrect lunar date on " + sample.Key);
            }
            var lunar = new ChineseLunisolarCalendar();
            Require(HolidayCalendar.LunarLabel(lunar.MinSupportedDateTime) != null &&
                HolidayCalendar.LunarLabel(lunar.MaxSupportedDateTime) != null &&
                HolidayCalendar.LunarLabel(lunar.MinSupportedDateTime.AddDays(-1)) == null &&
                HolidayCalendar.LunarLabel(lunar.MaxSupportedDateTime.AddDays(1)) == null,
                "Dates outside the conversion range must keep their Gregorian calendar cell");
            string[] names = { "元旦", "除夕", "春节", "清明节", "劳动节", "端午节", "中秋节", "国庆节" };
            string[] rows = {
                "20250101 20250128 20250129 20250404 20250501 20250531 20251006 20251001",
                "20260101 20260216 20260217 20260405 20260501 20260619 20260925 20261001",
                "20270101 20270205 20270206 20270405 20270501 20270609 20270915 20271001"
            };
            var named = new Dictionary<string, string>();
            foreach (string row in rows)
            {
                string[] dates = row.Split(' ');
                for (int i = 0; i < dates.Length; i++) named.Add(dates[i], names[i]);
            }
            var makeup = new HashSet<string>(("20250126 20250208 20250427 20250928 20251011 " +
                "20260104 20260214 20260228 20260509 20260920 20261010 " +
                "20270131 20270214 20270508 20270926 20271009").Split(' '));
            for (var date = new DateTime(2024, 1, 1); date < new DateTime(2029, 1, 1); date = date.AddDays(1))
            {
                string key = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                string expected;
                named.TryGetValue(key, out expected);
                Require(HolidayCalendar.FestivalName(date) == expected, "Incorrect festival name on " + key);
                Require(HolidayCalendar.IsMakeupWorkday(date) == makeup.Contains(key), "Incorrect makeup workday on " + key);
                if (expected != null) Require(HolidayCalendar.IsHoliday(date), "Named festival should have a rest mark on " + key);
                if (makeup.Contains(key)) Require(!HolidayCalendar.IsHoliday(date), "Rest and work marks conflict on " + key);
            }
        }

        private static void CheckHolidayLayout(string output, string folder)
        {
            CheckHolidayData();
            var now = new DateTime(2025, 10, 6, 10, 0, 0);
            var store = new Store(folder, () => now);
            store.Tasks.Add(new TaskItem { Id = "moon", Day = "2025-10-06", Title = "准备中秋晚餐" });
            var planner = new Planner(store) { Width = NarrowWindow.Width, Height = NarrowWindow.Height };
            Layout(planner);
            foreach (var day in new[] { 1, 6, 7, 11, 12 })
            {
                var date = new DateTime(now.Year, now.Month, day);
                string festival = day == 1 ? "国庆节" : day == 6 ? "中秋节" : null;
                string mark = day <= 7 ? "休" : day == 11 ? "班" : null;
                bool selected = day == now.Day;
                string name = date.ToString("M月d日 dddd", Zh) + (festival == null ? "" : "，" + festival) +
                    (mark == null ? "" : "，" + mark) + (selected ? "，有任务" : "");
                var cell = FindButton(planner, name);
                Require(cell != null && (string)cell.ToolTip == name, "Missing named cell or tooltip: " + name);
                var content = (Grid)cell.Content;
                var labels = content.Children.OfType<TextBlock>().ToList();
                var numeral = labels.Single(x => x.Text == day.ToString(CultureInfo.InvariantCulture));
                Require(numeral.FontSize == 13, "Date numerals must keep their original size");
                var digit = InkBounds(numeral, cell);
                var area = new Rect(cell.RenderSize);
                Require(area.Contains(digit) && labels.Count == 2 + (mark == null ? 0 : 1),
                    "Incorrect contents of date cell " + name);
                var caption = labels.Single(x => x.Text == (festival ?? HolidayCalendar.LunarLabel(date)));
                var title = InkBounds(caption, cell);
                Require(area.Contains(title) && title.Top > digit.Bottom, "Second line overlaps the date or is clipped: " + name);
                if (mark == null) continue;
                var badge = labels.Single(x => x.Text == mark);
                Require(badge.FontSize == 8 && badge.FontWeight == FontWeights.Normal, "Incorrect rest/work badge typography");
                var corner = InkBounds(badge, cell);
                Require(area.Contains(corner) && !digit.IntersectsWith(corner) && corner.Left > digit.Left + digit.Width / 2,
                    "Rest or work mark overlaps the day number: " + name);
                var badgeColor = ((SolidColorBrush)badge.Foreground).Color;
                Require(selected ? badgeColor == Colors.White : badgeColor != Colors.White, "Unclear badge contrast");
                if (festival == null) continue;
                Require(caption.FontSize == 9 && caption.FontWeight == FontWeights.Light, "Incorrect festival caption typography");
                Require(area.Contains(title) && title.Top > digit.Bottom && title.Top > corner.Bottom,
                    "Festival name clipped or overlapping at minimum width: " + name);
                if (!selected) continue;
                var line = content.Children.OfType<Border>().Single();
                var rule = line.TransformToAncestor(cell).TransformBounds(new Rect(line.RenderSize));
                Require(area.Contains(rule) && rule.Top > title.Bottom && ((SolidColorBrush)line.Background).Color == Colors.White &&
                    ((SolidColorBrush)caption.Foreground).Color == Colors.White, "Festival name collides with task marker");
            }
            var page = (FrameworkElement)planner.Content;
            var calendar = VisibleChildren(page).OfType<ScrollViewer>()
                .Single(view => AutomationProperties.GetName(view) == "月历日期");
            planner.Height = Math.Max(565, Math.Ceiling(NarrowWindow.Height + calendar.ScrollableHeight + 2));
            Layout(planner, new Size(planner.Width, planner.Height));
            calendar.ScrollToTop();
            page.UpdateLayout();
            Require(calendar.VerticalOffset < 0.5 && calendar.ScrollableHeight < 0.5, "Preview must contain the entire month");
            var image = new RenderTargetBitmap((int)Math.Ceiling(page.ActualWidth * 2),
                (int)Math.Ceiling(page.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
            image.Render(page);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(image));
            using (var file = File.Create(Path.Combine(output, "CalendarHolidayPreview.png"))) png.Save(file);
            planner.Close();
        }

        [STAThread]
        private static void Main(string[] args)
        {
            string output = args.Length > 0 ? args[0] : Path.GetTempPath();
            var folder = Path.Combine(output, "calendar-fold-" + Guid.NewGuid().ToString("N"));
            CheckHolidayLayout(output, Path.Combine(folder, "holiday"));
            CheckShortWindowNavigation(Path.Combine(folder, "short"));
            var today = new DateTime(2026, 11, 23, 9, 0, 0);
            var tomorrow = today.AddDays(1);
            var store = new Store(folder, () => today);
            store.Tasks.Add(new TaskItem { Id = "fold-check", Day = tomorrow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Title = "整理日程" });
            store.Save();
            var planner = new Planner(store) { Width = NarrowWindow.Width, Height = NarrowWindow.Height };
            Layout(planner);
            CheckNarrowHeader(planner);
            CheckCalendarDate(planner, today);
            var weekdayRow = VisibleChildren((FrameworkElement)planner.Content).OfType<UniformGrid>()
                .Single(grid => grid.Children.OfType<TextBlock>().Count() == 7);
            var weekColumns = weekdayRow.Children.Cast<TextBlock>().Select(t => t.Text);
            Require(weekColumns.SequenceEqual(new[] { "一", "二", "三", "四", "五", "六", "日" }),
                "Weekday columns must match the Monday-first grid");
            Click(planner, "下个月");
            CheckCalendarDate(planner, today.AddMonths(1));
            Require(FindButton(planner, today.AddMonths(1).ToString("M月d日 dddd", Zh)) != null, "Next month did not open");
            Click(planner, "上个月");
            var dateButton = tomorrow.ToString("M月d日 dddd", Zh) + "，有任务";
            Click(planner, dateButton);
            CheckCalendarDate(planner, tomorrow);
            double before = Bounds(planner, "编辑任务：整理日程").Top;
            var dayHeading = Bounds(planner, "收起当日任务");
            var longHeading = Bounds(planner, "展开长期任务");
            Require(longHeading.Bottom <= dayHeading.Top && longHeading.Right <= Bounds(planner, "＋ 新建长期任务").Left &&
                dayHeading.Right <= Bounds(planner, "＋ 新建任务").Left && dayHeading.Bottom <= before &&
                before < Bounds(planner, "收起月历").Top, "Stacked task headers overlap each other or their add buttons at 300 DIP");
            Click(planner, "收起月历");
            CheckCalendarDate(planner, tomorrow);
            Require(FindButton(planner, dateButton) == null &&
                Math.Abs(before - Bounds(planner, "编辑任务：整理日程").Top) < 1,
                "Folding the bottom calendar should keep the active task list in place");

            Click(planner, "＋ 新建任务");
            const string draftText = "尚未保存的任务";
            Field(planner, "任务名称").Text = draftText;
            Click(planner, "展开月历");
            Require(Field(planner, "任务名称").Text == draftText && store.Tasks.Count == 1, "Expanding saved or discarded the task draft");
            Click(planner, "取消");
            Click(planner, "＋ 新建任务");
            Click(planner, "添加子任务");
            Field(planner, "子任务（每行一项）").Text = "已写计划，还没有标题";
            var notes = Field(planner, "子任务（每行一项）");
            notes.Select(1, 2);
            Click(planner, "展开长期任务");
            Click(planner, "收起长期任务");
            Require(Field(planner, "子任务（每行一项）") == notes && notes.SelectionStart == 1 && notes.SelectionLength == 2,
                "Toggling the other list replaced the live draft or changed the selection");
            Click(planner, "收起当日任务");
            Require(FindButton(planner, "收起当日任务") != null &&
                notes.Text == "已写计划，还没有标题", "Folding the draft's own section lost an untitled draft");
            Click(planner, "取消");
            var more = FindButton(planner, "任务操作：整理日程");
            more.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            more.ContextMenu.Items.OfType<MenuItem>().Single(item => (string)item.Header == "添加子任务")
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Layout(planner);
            const string stepText = "尚未保存的子任务";
            Field(planner, "子任务名称").Text = stepText;
            Click(planner, "收起月历");
            Require(Field(planner, "子任务名称").Text == stepText && store.Tasks[0].Steps.Count == 0, "Folding saved or discarded the step draft");
            Click(planner, "取消子任务");

            Click(planner, "每周汇总");
            Require(FindButton(planner, "展开月历") == null && FindButton(planner, "收起月历") == null, "The day-grid toggle leaked into the weekly view");
            Click(planner, "下一周");
            Require(FindButton(planner, "编辑任务：整理日程") == null, "Weekly navigation did not advance");
            Click(planner, "上一周");
            Require(FindButton(planner, "编辑任务：整理日程") != null, "Weekly navigation did not return");
            Click(planner, "日历计划");
            Require(FindButton(planner, "展开月历") != null && FindButton(planner, "编辑任务：整理日程") != null,
                "Returning from the week lost the fold state or selected date");
            planner.Close();

            var reopened = new Planner(new Store(folder, () => today));
            Layout(reopened);
            Click(reopened, "展开月历");
            Click(reopened, dateButton);
            Click(reopened, "今天");
            CheckCalendarDate(reopened, today);
            Require(FindButton(reopened, "编辑任务：整理日程") == null, "Today did not restore the current day");
            reopened.Close();
            var expanded = new Planner(new Store(folder, () => today));
            Layout(expanded);
            Require(FindButton(expanded, "收起月历") != null && FindButton(expanded, dateButton) != null, "Expanded preference did not survive reopening");
            expanded.Close();
            Console.WriteLine("Passed calendar folding, holiday labels, narrow layout, drafts, date selection, weekly navigation and persistence.");
        }
    }
}
