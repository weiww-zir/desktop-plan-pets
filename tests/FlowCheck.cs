using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Xiaohei
{
    internal static class FlowCheck
    {
        static IEnumerable<T> Elements<T>(DependencyObject item) where T : FrameworkElement
        {
            var t = item as T;
            if (t != null) yield return t;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++)
                foreach (var child in Elements<T>(VisualTreeHelper.GetChild(item, i))) yield return child;
        }

        static void Click(Window window, string label)
        {
            window.UpdateLayout();
            var b = Elements<Button>(window).Single(x => AutomationProperties.GetName(x) == label || x.Content as string == label);
            b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            window.UpdateLayout();
        }

        static void Edit(Window parent, string button, string name, string notes)
        {
            Exception failed = null;
            parent.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate
            {
                var dialog = parent.OwnedWindows.Cast<Window>().Single();
                try
                {
                    var boxes = Elements<TextBox>(dialog).ToList();
                    if (name != null)
                        boxes.Single(x => AutomationProperties.GetName(x) == "任务名称").Text = name;
                    boxes.Single(x => x.AcceptsReturn).Text = notes;
                    Click(dialog, "保存");
                }
                catch (Exception e) { failed = e; dialog.Close(); }
            }));
            Click(parent, button);
            if (failed != null) throw failed;
        }

        static void Require(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            Console.WriteLine("PASS " + label);
        }

        static TextBox DraftField(Planner planner, string name)
        {
            return Elements<TextBox>(planner).Single(x => x.IsVisible && AutomationProperties.GetName(x) == name);
        }

        static TextBox StartDraft(Planner planner)
        {
            bool openedDialog = false;
            var watchdog = planner.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate
            {
                var dialog = planner.OwnedWindows.Cast<Window>().FirstOrDefault(w => w.IsVisible);
                if (dialog != null) { openedDialog = true; dialog.Close(); }
            }));
            Click(planner, "＋ 新建任务");
            watchdog.Abort();
            Require(!openedDialog && planner.OwnedWindows.Count == 0, "new task opens inline on the existing planner");
            return DraftField(planner, "任务名称");
        }

        static void PressKey(UIElement control, Key key)
        {
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(control),
                Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            control.RaiseEvent(args);
            if (!args.Handled)
            {
                args.RoutedEvent = Keyboard.KeyDownEvent;
                control.RaiseEvent(args);
            }
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(delegate {}));
        }

        static void InlinePreview(Planner planner, string file)
        {
            var save = Elements<Button>(planner).Single(x => x.Content as string == "保存任务");
            save.BringIntoView();
            planner.UpdateLayout();
            var content = (FrameworkElement)planner.Content;
            var image = Render(content, content.ActualWidth, content.ActualHeight);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var output = File.Create(file)) encoder.Save(output);
        }

        static void WaitFor(PetActor actor, PetPose pose, int timeout = 2200)
        {
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            while (actor.Pose != pose && elapsed.ElapsedMilliseconds < timeout)
            {
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(delegate { }));
                System.Threading.Thread.Sleep(12);
            }
            Require(actor.Pose == pose, "actor reaches " + pose);
        }

        static void CheckExpressions(DesktopPet pet)
        {
            var actor = (PetActor)pet.Content;
            var split = pet.ContextMenu.Items.OfType<MenuItem>().Single(x => (string)x.Header == "尾巴变小猫");
            split.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            WaitFor(actor, PetPose.TailKittens);
            pet.UpdateLayout();
            var litter = Elements<PetArt.TailLitter>(actor).Single();
            for (int expected = 1; expected <= 3; expected++)
            {
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                while (litter.Children.Cast<UIElement>().Skip(1).Count(x => x.IsVisible) != expected)
                {
                    if (elapsed.ElapsedMilliseconds > 2200) throw new Exception("Tail animation stalled before kitten " + expected);
                    Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(delegate { }));
                    System.Threading.Thread.Sleep(15);
                }
                Require(true,"visible kitten count reaches " + expected);
            }
            WaitFor(actor, PetPose.Rest, 8250);
            split.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            WaitFor(actor, PetPose.TailKittens);
            var meow = pet.ContextMenu.Items.OfType<MenuItem>().Single(x => (string)x.Header == "让小黑喵一声");
            meow.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            WaitFor(actor, PetPose.Meow);
            WaitFor(actor, PetPose.Rest);
            actor.Enter();
            WaitFor(actor, PetPose.Tilt);
            WaitFor(actor, PetPose.Rest);
            var idle = (DispatcherTimer)typeof(PetActor).GetField("idle",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(actor);
            var interval = idle.Interval;
            idle.Interval = TimeSpan.FromMilliseconds(80);
            WaitFor(actor, PetPose.Nap);
            idle.Interval = interval;
            actor.Enter();
            WaitFor(actor, PetPose.Stretch);
            WaitFor(actor, PetPose.Rest);
        }

        static void PosePreview(string file)
        {
            var poses = new[] { PetPose.Tilt, PetPose.Wave, PetPose.Nap, PetPose.Stretch, PetPose.Meow };
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.White };
            foreach (var pose in poses)
            {
                var pet = PetArt.Draw(false, pose);
                pet.Width = 120; pet.Height = 120; pet.Margin = new Thickness(4);
                panel.Children.Add(pet);
            }
            var size = new Size(128 * poses.Length, 128);
            panel.Measure(size); panel.Arrange(new Rect(size)); panel.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)size.Width,(int)size.Height,96,96,PixelFormats.Pbgra32);
            bitmap.Render(panel);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(file)) png.Save(output);
        }

        static void SetPetSize(DesktopPet pet, string choice)
        {
            var menu = pet.ContextMenu.Items.OfType<MenuItem>().Single(x => (string)x.Header == "桌宠大小");
            var item = menu.Items.OfType<MenuItem>().Single(x => (string)x.Header == choice);
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
            pet.UpdateLayout();
        }

        static void ResizePlanner(Planner planner, double dx, double dy)
        {
            planner.UpdateLayout();
            var grip = Elements<Thumb>(planner).Single(x => AutomationProperties.GetName(x) == "调整窗口大小");
            grip.RaiseEvent(new DragDeltaEventArgs(dx, dy) { RoutedEvent = Thumb.DragDeltaEvent });
            planner.UpdateLayout();
            grip.RaiseEvent(new DragCompletedEventArgs(dx, dy, false) { RoutedEvent = Thumb.DragCompletedEvent });
        }

        static RenderTargetBitmap Render(FrameworkElement element, double width, double height)
        {
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * 2), (int)Math.Ceiling(height * 2),
                192, 192, PixelFormats.Pbgra32);
            bitmap.Render(element);
            return bitmap;
        }

        static byte AlphaAt(BitmapSource bitmap, int x, int y)
        {
            var sample = new byte[4];
            bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), sample, 4, 0);
            return sample[3];
        }

        static void Screenshot(Planner planner, DesktopPet desktopPet, string file)
        {
            var visual = new DrawingVisual();
            var width = planner.ActualWidth;
            var height = planner.ActualHeight;
            var pet = PetArt.Create(false);
            double petWidth = desktopPet.ActualWidth, petHeight = desktopPet.ActualHeight;
            pet.Measure(new Size(petWidth, petHeight));
            pet.Arrange(new Rect(0, 0, petWidth, petHeight));
            pet.UpdateLayout();
            var paper = Render((FrameworkElement)planner.Content, width, height);
            int lastX = paper.PixelWidth - 1, lastY = paper.PixelHeight - 1;
            Require(AlphaAt(paper, 0, 0) == 0 && AlphaAt(paper, lastX, 0) == 0 &&
                    AlphaAt(paper, 0, lastY) == 0 && AlphaAt(paper, lastX, lastY) == 0 &&
                    AlphaAt(paper, lastX / 2, 14) == 255, "all four rounded window corners render transparent");
            var checks = Elements<CheckBox>(planner).Where(x =>
                AutomationProperties.GetName(x).StartsWith("完成任务：")).ToArray();
            Require(checks.Length == 2 && checks.Count(x => x.IsChecked == true) == 1 && checks.All(check =>
                Elements<Border>(check).Any(border => border.CornerRadius.TopLeft >= 4 &&
                    border.CornerRadius.BottomRight >= 4)), "both task states use rounded checkboxes");
            double sceneWidth = width + petWidth + 50, sceneHeight = height + 32;
            using (var c = visual.RenderOpen())
            {
                c.DrawRectangle(new SolidColorBrush(Color.FromRgb(219,229,220)), null, new Rect(0,0,sceneWidth,sceneHeight));
                c.DrawImage(paper, new Rect(16,16,width,height));
                c.DrawImage(Render(pet, petWidth, petHeight), new Rect(width+32,height+16-petHeight,petWidth,petHeight));
            }
            int w = (int)Math.Ceiling(sceneWidth), h = (int)Math.Ceiling(sceneHeight);
            var bitmap = new RenderTargetBitmap(w * 2, h * 2, 192, 192, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(file)) encoder.Save(stream);
        }

        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var today = DateTime.Today;
                var day = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var dir = Path.Combine(args[0], Guid.NewGuid().ToString("N"));
                var store = new Store(dir,delegate { return today.AddHours(10); });
                var pet = new DesktopPet(store);
                pet.Left = -10000;
                pet.Top = -10000;
                pet.ShowActivated = false;
                pet.Show();
                pet.Left = -10000;
                pet.Top = -10000;
                Require(pet.Width == 92, "default pet is 92 DIPs wide");
                CheckExpressions(pet);
                PosePreview(Path.Combine(args[0], "动作预览.png"));
                SetPetSize(pet, "适中（120）");
                Require(pet.Width == 120 && new Store(dir).PetWidth == 120, "pet size menu updates window and saved size");
                SetPetSize(pet, "小巧（92，默认）");
                Require(pet.Width == 92 && new Store(dir).PetWidth == 92, "default pet size restored via menu");
                pet.ShowPlanner();
                var planner = pet.OwnedWindows.OfType<Planner>().Single();
                planner.Left = -10000;
                planner.Top = -10000;
                pet.ShowPlanner();
                Require(pet.OwnedWindows.Count == 1, "pet opens one reusable planner");
                Require(planner.Width == 356 && planner.Height == 500, "default planner opens at 356 by 500 DIPs");
                ResizePlanner(planner, 24, 20);
                Require(planner.Width == 380 && planner.Height == 520, "resize grip changes both planner dimensions");
                Click(planner, "收起手帐");
                var resized = new Store(dir);
                Require(resized.PlannerWidth == 380 && resized.PlannerHeight == 520, "planner dimensions survive reload");
                pet.ShowPlanner();
                planner = pet.OwnedWindows.OfType<Planner>().Single();
                Require(planner.Width == 380 && planner.Height == 520, "reopening restores planner size");
                planner.Left = -10000;
                planner.Top = -10000;
                ResizePlanner(planner, -24, -20);
                const string first = "读完《小黑漫画》第一章";
                const string second = "整理这周的笔记";
                Click(planner, "下个月");
                Click(planner, "下个月");
                // Explicitly select the 15th, independent of the current day or month length.
                var future = new DateTime(today.Year,today.Month,15).AddMonths(2);
                Click(planner,future.ToString("M月d日 dddd", CultureInfo.GetCultureInfo("zh-CN")));
                var draft = StartDraft(planner);
                draft.Text = "  ";
                PressKey(draft,Key.Enter);
                Require(store.Tasks.Count == 0 && DraftField(planner, "任务名称").IsVisible,
                    "blank title leaves the draft open without creating a task");
                draft = DraftField(planner,"任务名称");
                draft.Text = "复盘两个月的阅读记录";
                var repeat = Elements<Button>(planner).FirstOrDefault(x => x.IsVisible && x.IsEnabled && x.Content as string == "＋ 新建任务");
                if (repeat != null) Click(planner,"＋ 新建任务");
                draft = DraftField(planner,"任务名称");
                Require(draft.Text == "复盘两个月的阅读记录" && store.Tasks.Count == 0,
                    "repeated add keeps one draft and preserves typed text");
                PressKey(draft,Key.Enter);
                var futureKey = future.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
                var futureTask = new Store(dir).Tasks.Single();
                Require(futureTask.Day == futureKey && futureTask.Title == "复盘两个月的阅读记录" &&
                    String.IsNullOrEmpty(futureTask.Notes), "Enter saves a title-only task under the selected date");
                Click(planner,"今天");
                draft = StartDraft(planner);
                draft.Text = "未保存的草稿";
                PressKey(draft,Key.Escape);
                Require(planner.IsVisible && store.Tasks.Count == 1 &&
                    !Elements<TextBox>(planner).Any(x => x.IsVisible && AutomationProperties.GetName(x) == "任务名称"),
                    "Escape cancels the inline draft and keeps the planner open");
                draft = StartDraft(planner);
                Click(planner,"添加子任务");
                DraftField(planner,"子任务（每行一项）").Text = "先记下想做的事";
                Click(planner,"下个月");
                Require(DraftField(planner,"子任务（每行一项）").Text == "先记下想做的事" &&
                    store.Tasks.Count == 1, "notes-only draft survives attempted navigation");
                Click(planner, "取消");
                Require(planner.IsVisible && new Store(dir).Tasks.Count == 1,"cancel button discards an inline draft");
                draft = StartDraft(planner);
                draft.Text = first;
                Click(planner, "添加子任务");
                var notes = DraftField(planner, "子任务（每行一项）");
                Require(notes.AcceptsReturn, "optional task plan accepts multiple lines");
                notes.Text = "晚饭后留出半小时。\n记下最喜欢的一幕。";
                InlinePreview(planner, Path.Combine(args[0],"InlinePreview.png"));
                Click(planner, "保存任务");
                Require(new Store(dir).Tasks.Single(x => x.Title == first).Notes.Replace("\r\n","\n") ==
                    "晚饭后留出半小时。\n记下最喜欢的一幕。",
                    "save button persists optional multiline plan");
                draft = StartDraft(planner);
                draft.Text = second;
                Click(planner, "下个月");
                Require(new Store(dir).Tasks.Single(x => x.Title == second).Day == day,
                    "switching dates saves a titled draft against its original date");
                Click(planner, "今天");
                Require(store.Tasks.Count == 3 && store.Tasks.Count(x => x.Day == day) == 2,
                    "two tasks created for today and the future task keeps its own date");
                Edit(planner, "编辑任务：" + second, second, "挑出三条想继续研究的问题。");
                var check = Elements<CheckBox>(planner).First(x => AutomationProperties.GetName(x).Contains(first));
                check.IsChecked = true;
                check.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                planner.UpdateLayout();
                Require(store.Tasks.Single(x => x.Title == first).Done, "completion persists in model");
                WaitFor((PetActor)pet.Content,PetPose.Wave);
                WaitFor((PetActor)pet.Content,PetPose.Rest);
                Require(Elements<TextBlock>(planner).Any(x => x.Text == "已完成 1 / 2"), "daily count updates");
                Click(planner, "上个月");
                Require(Elements<TextBlock>(planner).Any(x => x.Text == "当天还没有任务"), "month navigation changes selected day");
                Click(planner, "今天");
                Click(planner, "每周汇总");
                Require(Elements<TextBlock>(planner).Any(x => x.Text == "已完成 1 / 2 项"), "weekly view counts completed and open tasks");
                Edit(planner, "写周计划", null, "读完两章，周末整理读书笔记。");
                Screenshot(planner, pet, Path.Combine(args[0], "Preview.png"));
                var last = Elements<CheckBox>(planner).Single(x => AutomationProperties.GetName(x) == "完成任务：" + second);
                last.IsChecked = true;
                last.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                WaitFor((PetActor)pet.Content,PetPose.Wave);
                WaitFor((PetActor)pet.Content,PetPose.Hop);
                WaitFor((PetActor)pet.Content,PetPose.Rest);
                planner.UpdateLayout();
                last = Elements<CheckBox>(planner).Single(x => AutomationProperties.GetName(x).Contains(second));
                last.IsChecked = false;
                last.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Require(((PetActor)pet.Content).Pose == PetPose.Rest, "unchecking does not celebrate");
                Click(planner, "下一周");
                Require(Elements<TextBlock>(planner).Any(x => x.Text == "本周还没有任务"), "week excludes other weeks");
                Click(planner, "本周");
                Require(Elements<TextBlock>(planner).Any(x => x.Text == "读完两章，周末整理读书笔记。"), "week plan restored when navigating back");
                Click(planner, "收起手帐");
                Require(pet.IsVisible && pet.OwnedWindows.Count == 0, "closing planner keeps pet alive");
                var loaded = new Store(dir);
                Require(loaded.Tasks.Count == 3 && loaded.Tasks.Any(x => x.Day == futureKey) && loaded.Tasks.Single(x => x.Title == first).Done &&
                    loaded.Tasks.Single(x => x.Title == second).Notes.StartsWith("挑出") &&
                    loaded.WeekNote(today).StartsWith("读完两章"), "edits, checkboxes, and weekly plan survive reload");
                pet.ShowPlanner();
                planner = pet.OwnedWindows.OfType<Planner>().Single();
                planner.Left = -10000;
                planner.Top = -10000;
                draft = StartDraft(planner);
                Require(draft.IsKeyboardFocused, "task name receives keyboard focus on creation");
                draft.Text = "整理明天的日程";
                Click(planner, "收起手帐");
                Require(new Store(dir).Tasks.Count(t => t.Day == day && t.Title == "整理明天的日程") == 1,
                    "closing planner saves the titled draft once");
                pet.Close();
                app.Shutdown();
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
    }
}
