using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Xiaohei
{
    internal static class LongTermTaskCheck
    {
        private const string TodayTask = "归还图书馆的书", Converted = "准备下次读书会", Direct = "读完《山海经》";
        private static readonly DateTime Today = new DateTime(2026, 11, 24, 10, 0, 0);

        private static IEnumerable<T> Controls<T>(DependencyObject parent) where T : FrameworkElement
        {
            foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<FrameworkElement>())
            {
                if (child.Visibility != Visibility.Visible) continue;
                if (child is T) yield return (T)child;
                foreach (var field in Controls<T>(child)) yield return field;
            }
        }

        private static void Check(bool ok, string detail)
        {
            if (!ok) throw new InvalidOperationException(detail);
            Console.WriteLine("PASS " + detail);
        }

        private static Button Button(Window window, string name)
        {
            return Controls<Button>(window.Content as FrameworkElement).SingleOrDefault(x =>
                AutomationProperties.GetName(x) == name || (x.Content as string) == name);
        }

        private static void Click(Window window, string name)
        {
            var button = Button(window, name);
            if (button == null || !button.IsEnabled) throw new InvalidOperationException("Unavailable button: " + name);
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            window.UpdateLayout();
        }

        private static void Fill(Window window, string name, string value)
        {
            Controls<TextBox>((FrameworkElement)window.Content).Single(x => AutomationProperties.GetName(x) == name).Text = value;
        }

        private static void Toggle(Window window, string name, bool done)
        {
            var check = Controls<CheckBox>((FrameworkElement)window.Content).Single(x => AutomationProperties.GetName(x) == name);
            check.IsChecked = done;
            check.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            window.UpdateLayout();
        }

        private static void AddTask(Planner planner, string title, bool longTerm, string steps = null)
        {
            Click(planner, longTerm ? "＋ 新建长期任务" : "＋ 新建任务");
            Fill(planner, "任务名称", title);
            if (steps != null) { Click(planner, "添加子任务"); Fill(planner, "子任务（每行一项）", steps); }
            Click(planner, "保存任务");
        }

        private static void AddStep(Planner planner, string parent, string name)
        {
            var more = Button(planner, "任务操作：" + parent);
            more.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            more.ContextMenu.Items.OfType<MenuItem>().Single(item => (string)item.Header == "添加子任务")
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            planner.UpdateLayout();
            Fill(planner, "子任务名称", name);
            Click(planner, "保存子任务");
        }

        private static void ConvertTask(Planner planner, string title)
        {
            Exception failure = null;
            planner.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate
            {
                var dialog = planner.OwnedWindows.Cast<Window>().Single();
                CheckBox convert = null;
                try
                {
                    convert = Controls<CheckBox>((FrameworkElement)dialog.Content).Single(x =>
                        AutomationProperties.GetName(x) == "转为长期任务" || (x.Content as string) == "转为长期任务");
                    convert.IsChecked = true;
                    Click(dialog, "保存");
                }
                catch (Exception e)
                {
                    failure = e;
                    if (convert != null) convert.IsChecked = false;
                    dialog.Close();
                }
            }));
            Click(planner, "编辑任务：" + title);
            if (failure != null) throw failure;
        }

        private static Planner Open(Store store)
        {
            var planner = new Planner(store) { Width = 356, Height = 778, WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -24000, Top = -24000, ShowActivated = false, ShowInTaskbar = false };
            planner.Show(); planner.UpdateLayout();
            return planner;
        }

        private static void Preview(Planner planner, string output)
        {
            var page = (FrameworkElement)planner.Content;
            Controls<ScrollViewer>(page).First().ScrollToTop();
            page.UpdateLayout();
            var bmp = new RenderTargetBitmap((int)Math.Ceiling(page.ActualWidth * 2), (int)Math.Ceiling(page.ActualHeight * 2),
                192, 192, PixelFormats.Pbgra32);
            bmp.Render(page);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using (var stream = File.Create(Path.Combine(output, "LongTermTasksPreview.png"))) encoder.Save(stream);
        }

        [STAThread] private static int Main(string[] args)
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var output = args.Length > 0 ? args[0] : Path.GetTempPath();
                var dir = Path.Combine(output, "long-term-ui-" + Guid.NewGuid().ToString("N"));
                var store = new Store(dir, () => Today);
                var planner = Open(store);
                Check(Button(planner, "展开长期任务") != null && Button(planner, "收起当日任务") != null &&
                    Button(planner, "＋ 新建长期任务") != null, "first launch folds only long-term entries and keeps creation available");
                AddTask(planner, TodayTask, false);
                AddTask(planner, Converted, false, "选定书目");
                AddStep(planner, Converted, "整理讨论提纲");
                Toggle(planner, "完成子任务：选定书目", true);
                var id = store.Tasks.Single(t => t.Title == Converted).Id;
                AddTask(planner, Direct, true, "读完第一章");
                Check(Button(planner, "收起长期任务") != null && Button(planner, "编辑任务：" + TodayTask) != null,
                    "adding a long-term task unfolds its entries and keeps dated tasks visible");
                AddStep(planner, Direct, "整理摘录");
                Toggle(planner, "完成子任务：读完第一章", true);
                Check(store.PendingCount(Today) == 2, "direct long-term task has steps and does not count toward today's badge");
                Click(planner, "收起长期任务");
                Check(Button(planner, "编辑任务：" + Direct) == null && Button(planner, "编辑任务：" + TodayTask) != null,
                    "folding the long-term list leaves the selected day visible");
                ConvertTask(planner, Converted);
                var saved = new Store(dir, () => Today);
                var converted = saved.Tasks.Single(t => t.Id == id);
                Check(saved.Tasks.Count == 3 && converted.IsLongTerm && !converted.Done &&
                    converted.Steps.Select(s => s.Title).SequenceEqual(new[] { "选定书目", "整理讨论提纲" }) &&
                    converted.Steps[0].Done && !converted.Steps[1].Done, "conversion preserves identity and subtask progress on disk");
                Check(store.PendingCount(Today) == 1, "conversion removes only the converted parent from today's badge");
                Click(planner, "展开长期任务");
                Preview(planner, output);
                Click(planner, "每周汇总");
                Check(Button(planner, "编辑任务：" + TodayTask) != null && Button(planner, "编辑任务：" + Converted) == null &&
                    Button(planner, "编辑任务：" + Direct) == null, "weekly task list excludes both long-term entries");
                Click(planner, "日历计划"); Click(planner, "下个月");
                Check(Button(planner, "编辑任务：" + TodayTask) == null && Button(planner, "编辑任务：" + Converted) != null &&
                    Button(planner, "编辑任务：" + Direct) != null, "moving dates preserves the long-term section");
                Toggle(planner, "完成子任务：整理摘录", true);
                Check(store.Tasks.Single(t => t.Title == Direct).Done, "completing final long-term step completes the parent");
                Click(planner, "今天");
                Toggle(planner, "完成任务：" + TodayTask, true);
                var completedId = store.Tasks.Single(t => t.Title == TodayTask).Id;
                ConvertTask(planner, TodayTask);
                var completed = new Store(dir, () => Today).Tasks.Single(t => t.Id == completedId);
                Check(completed.IsLongTerm && completed.Done, "converting a completed daily task preserves completion");
                Click(planner, "收起当日任务");
                planner.Close(); planner = Open(new Store(dir, () => Today));
                Check(Button(planner, "展开当日任务") != null && Button(planner, "收起长期任务") != null,
                    "both preferences survive reopening");
                var reopened = Controls<CheckBox>((FrameworkElement)planner.Content).Single(x =>
                    AutomationProperties.GetName(x) == "完成任务：" + Direct);
                Check(reopened.IsChecked == true && Button(planner, "编辑任务：" + Converted) != null,
                    "reopening restores long-term tasks and their completion states");
                Click(planner, "＋ 新建长期任务");
                Fill(planner, "任务名称", "整理阅读清单");
                var draft = Controls<TextBox>((FrameworkElement)planner.Content).Single(x => AutomationProperties.GetName(x) == "任务名称");
                draft.Select(1, 2);
                Click(planner, "下个月");
                Click(planner, "上个月");
                Check(Controls<TextBox>((FrameworkElement)planner.Content).Contains(draft) && draft.SelectionStart == 1 &&
                    draft.SelectionLength == 2 && !new Store(dir, () => Today).Tasks.Any(t => t.Title == draft.Text),
                    "browsing months preserves the live long-term draft and its selection");
                Click(planner, "展开当日任务");
                Click(planner, "收起当日任务");
                Click(planner, "收起长期任务");
                Check(Button(planner, "展开当日任务") != null && Button(planner, "展开长期任务") != null,
                    "both lists can be folded together");
                Click(planner, "展开长期任务");
                Check(new Store(dir, () => Today).Tasks.Count(t => t.IsLongTerm && t.Title == "整理阅读清单") == 1 &&
                    Button(planner, "编辑任务：整理阅读清单") != null, "folding saves a named draft once in its original list");
                Click(planner, Today.ToString("M月d日 dddd", System.Globalization.CultureInfo.GetCultureInfo("zh-CN")));
                Check(Button(planner, "收起当日任务") != null && Button(planner, "编辑任务：整理阅读清单") != null,
                    "selecting the current date opens the daily list and preserves the long-term list");
                Click(planner, "收起当日任务");
                Click(planner, "今天");
                Check(Button(planner, "收起当日任务") != null && Button(planner, "收起长期任务") != null,
                    "Today opens only the day section");
                planner.Close(); app.Shutdown(); return 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        }
    }
}
