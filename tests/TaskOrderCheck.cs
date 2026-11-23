using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Xiaohei
{
    internal static class TaskOrderCheck
    {
        private static IEnumerable<FrameworkElement> Tree(DependencyObject root)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<FrameworkElement>())
            {
                yield return child;
                foreach (var descendant in Tree(child)) yield return descendant;
            }
        }

        private static TaskItem Add(Store store, string title, DateTime date, bool red)
        {
            var task = new TaskItem { Id = title, Title = title, Day = date.ToString("yyyy-MM-dd") };
            task.Schedule.Alert = red;
            store.Tasks.Add(task);
            return task;
        }

        private static void Order(Planner planner, params string[] expected)
        {
            var actual = Tree(planner).OfType<Border>().Select(AutomationProperties.GetName)
                .Where(name => name.StartsWith("任务详情：")).Select(name => name.Substring(5));
            if (!actual.SequenceEqual(expected)) throw new Exception("Unexpected order: " + String.Join(", ", actual));
        }

        private static void Click(Planner planner, string name)
        {
            Tree(planner).OfType<Button>().Single(b => AutomationProperties.GetName(b) == name || (b.Content as string) == name)
                .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }

        [STAThread]
        private static void Main(string[] args)
        {
            var now = new DateTime(2026, 11, 23, 9, 0, 0);
            var store = new Store(Path.Combine(args[0], Guid.NewGuid().ToString("N")), () => now);
            Add(store, "n1", now, false);
            Add(store, "r3", now.AddDays(2), true);
            Add(store, "r1", now, true);
            Add(store, "n2", now, false);
            Add(store, "r2", now, true);
            Add(store, "n3", now.AddDays(1), false);
            var planner = new Planner(store);
            Order(planner, "r1", "r2", "n1", "n2");
            Click(planner, "每周汇总");
            Order(planner, "r1", "r2", "r3", "n1", "n2", "n3");
            var dates = Tree(planner).OfType<TextBlock>().Select(x => x.Text)
                .Where(x => x.Contains("星期")).ToArray();
            var expectedDates = new[]{0, 2, 0, 1}.Select(i => now.AddDays(i).ToString("M月d日 dddd", CultureInfo.GetCultureInfo("zh-CN")));
            if (!dates.SequenceEqual(expectedDates)) throw new Exception("Date headings: " + String.Join(", ", dates));
            Click(planner, "添加子任务：r1");
            var editor = Tree(planner).OfType<TextBox>().Single();
            editor.Text = "unfinished";
            var check = Tree(planner).OfType<CheckBox>().Single(b => AutomationProperties.GetName(b) == "完成任务：r1");
            check.IsChecked = true;
            check.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Order(planner, "r2", "r3", "n1", "r1", "n2", "n3");
            if (Tree(planner).OfType<TextBox>().Single() != editor || editor.Text != "unfinished")
                throw new Exception("Reordering replaced the active inline draft");
            Click(planner, "取消子任务");
            Click(planner, "日历计划");
            Order(planner, "r2", "n1", "r1", "n2");
            now = new DateTime(2026, 11, 29, 18, 0, 0);
            var sunday = new Store(Path.Combine(args[0], Guid.NewGuid().ToString("N")), () => now);
            var ordinary = Add(sunday, "done", now, false); ordinary.Done = true;
            var carry = Add(sunday, "carry", now.AddDays(1), true); carry.Schedule.CarriedFrom = "2026-11-29";
            var weekEnd = new Planner(sunday);
            Order(weekEnd, "carry", "done");
            Click(weekEnd, "每周汇总"); Order(weekEnd, "carry", "done");
            Click(weekEnd, "下一周"); Order(weekEnd, "carry");
            Console.WriteLine("Passed stable day/week priority, date headings, completion reorder, live inline draft and Sunday/Monday carry dedup.");
        }
    }
}
