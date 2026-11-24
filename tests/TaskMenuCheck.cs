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
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Xiaohei
{
    internal static class TaskMenuCheck
    {
        private static void Require(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
        }

        private static IEnumerable<FrameworkElement> Children(DependencyObject parent)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<FrameworkElement>())
            {
                if (child.Visibility != Visibility.Visible) continue;
                yield return child;
                foreach (var other in Children(child)) yield return other;
            }
        }

        private static void Flush()
        {
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(delegate {}));
        }

        private static Button Button(Window window, string name)
        {
            return Children(window).OfType<Button>().Single(x =>
                AutomationProperties.GetName(x) == name || x.Content as string == name);
        }

        private static void Click(Button button)
        {
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Flush();
        }

        private static ContextMenu Open(Planner planner, string title)
        {
            var trigger = Button(planner, "任务操作：" + title);
            var card = Children(planner).OfType<Border>().Single(x => AutomationProperties.GetName(x) == "任务详情：" + title);
            trigger.BringIntoView();
            planner.UpdateLayout();
            var center = trigger.TranslatePoint(new Point(trigger.ActualWidth / 2, trigger.ActualHeight / 2), card);
            Require(Math.Abs(center.Y - card.ActualHeight / 2) <= 1 && center.X > card.ActualWidth * .75,
                "The dots must be at the right, centered across the entire task card");
            Click(trigger);
            var menu = trigger.ContextMenu;
            Require(menu.IsOpen && menu.IsVisible && planner.IsVisible && menu.PlacementTarget == trigger &&
                PresentationSource.FromVisual(menu) is HwndSource, "Task menu did not open over the live planner");
            Require(menu.Items.Cast<MenuItem>().Select(i => (string)i.Header)
                .SequenceEqual(new[] { "添加子任务", "编辑", "删除", "转长期" }), "Unexpected task commands");
            return menu;
        }

        private static void Choose(ContextMenu menu, string command)
        {
            var item = menu.Items.Cast<MenuItem>().Single(i => (string)i.Header == command);
            Require(item.IsEnabled, "Unavailable command " + command);
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Flush();
            Require(!menu.IsOpen, "Menu remained open after " + command);
        }

        private static IEnumerable<T> Visuals<T>(DependencyObject element) where T : DependencyObject
        {
            if (element is T) yield return (T)element;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
                foreach (var child in Visuals<T>(VisualTreeHelper.GetChild(element, i))) yield return child;
        }

        private static bool HasRoundedSurface(DependencyObject element)
        {
            return Visuals<Border>(element).Select(border => border.CornerRadius).Any(corners =>
                corners.TopLeft > 0 && corners.TopRight > 0 && corners.BottomLeft > 0 && corners.BottomRight > 0);
        }

        private static Color SheetColor(Window window)
        {
            var sheet = Children(window).OfType<Border>().Where(b => b.Background is SolidColorBrush &&
                ((SolidColorBrush)b.Background).Color.A > 0).OrderByDescending(b => b.ActualWidth * b.ActualHeight).First();
            return ((SolidColorBrush)sheet.Background).Color;
        }

        private static Rect Ink(TextBlock text, Visual relativeTo)
        {
            var glyphs = new FormattedText(text.Text, CultureInfo.GetCultureInfo("zh-CN"), text.FlowDirection,
                new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize,
                text.Foreground, VisualTreeHelper.GetDpi(text).PixelsPerDip) { TextAlignment = text.TextAlignment };
            if (text.TextWrapping != TextWrapping.NoWrap) glyphs.MaxTextWidth = text.ActualWidth;
            return text.TransformToAncestor(relativeTo).TransformBounds(glyphs.BuildGeometry(new Point()).Bounds);
        }

        private static void CheckPromptLayout(Window dialog, Window owner, bool error, List<Button> buttons)
        {
            var sheet = (FrameworkElement)dialog.Content;
            var area = new Rect(sheet.RenderSize);
            area.Inflate(1, 1); // layout rounding on fractional DPI
            if (!error)
            {
                Require(dialog.ActualWidth <= 236.01 && dialog.ActualHeight <= 132.01, "Confirmation exceeds its compact card size");
                Require(SheetColor(dialog) != SheetColor(owner), "Confirmation needs a distinct surface from its owner");
            }
            var title = Children(dialog).OfType<TextBlock>().Single(t => t.Text == dialog.Title);
            var body = Children(dialog).OfType<TextBlock>().Single(t => error ? t.Text.Contains("保存失败") : t.Text.EndsWith("？"));
            var regions = new List<Rect> { Ink(title, sheet) };
            if (error)
            {
                var scroll = Visuals<ScrollViewer>(sheet).Single(s => s.IsAncestorOf(body));
                Require(body.TextWrapping == TextWrapping.Wrap && body.ActualHeight > body.FontSize * 1.5,
                    "Save failure needs a readable multiline message");
                regions.Add(scroll.TransformToAncestor(sheet).TransformBounds(new Rect(scroll.RenderSize)));
                var original = body.Text;
                try
                {
                    body.Text += String.Concat(Enumerable.Repeat("\n保存文件时发生共享冲突，释放占用后可以重试。", 14));
                    dialog.UpdateLayout();
                    scroll.ScrollToEnd();
                    dialog.UpdateLayout();
                    Require(scroll.ScrollableHeight > 0 && Math.Abs(scroll.VerticalOffset - scroll.ScrollableHeight) < 1.5,
                        "Long error details must scroll to their last line");
                }
                finally { body.Text = original; scroll.ScrollToTop(); dialog.UpdateLayout(); }
            }
            else
            {
                var bodyInk = Ink(body, sheet);
                var viewport = Visuals<ScrollViewer>(sheet).FirstOrDefault(s => s.IsAncestorOf(body));
                if (viewport != null)
                {
                    var bounds = viewport.TransformToAncestor(sheet).TransformBounds(new Rect(viewport.RenderSize));
                    bounds.Inflate(1, 1);
                    Require(bounds.Contains(bodyInk), "The short confirmation message is clipped");
                }
                regions.Add(bodyInk);
            }
            foreach (var button in buttons)
            {
                var bounds = button.TransformToAncestor(sheet).TransformBounds(new Rect(button.RenderSize));
                regions.Add(bounds);
                bounds.Inflate(1, 1);
                foreach (var label in Visuals<TextBlock>(button).Where(t => !String.IsNullOrEmpty(t.Text)))
                    Require(bounds.Contains(Ink(label, sheet)), "Clipped button text: " + label.Text);
            }
            for (int i = 0; i < regions.Count; i++)
            {
                Require(!regions[i].IsEmpty && area.Contains(regions[i]), "Content extends beyond the dialog surface");
                for (int j = 0; j < i; j++)
                {
                    var overlap = Rect.Intersect(regions[i], regions[j]);
                    Require(overlap.IsEmpty || overlap.Width < 1 || overlap.Height < 1, "Overlapping confirmation text or controls");
                }
            }
        }

        private enum Reply { No, Close, Yes, OK }

        private static void AnswerConfirmation(Window owner, Action open, Reply reply,
            string caption = "删除任务", string prompt = "删除这项任务？")
        {
            var priorWindows = Application.Current.Windows.OfType<Window>().ToList();
            Window dialog = null;
            Exception failure = null;
            owner.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate
            {
                try
                {
                    dialog = owner.OwnedWindows.OfType<Window>().Single(w => w.IsVisible && w.Title == caption);
                    Require(dialog.Owner == owner && ComponentDispatcher.IsThreadModal,
                        "Confirmation must be modal and owned by the window that requested it");
                    dialog.UpdateLayout();
                    var text = Children(dialog).OfType<TextBlock>().Select(block => block.Text).ToList();
                    var buttons = Children(dialog).OfType<Button>().ToList();
                    CheckPromptLayout(dialog, owner, reply == Reply.OK, buttons);
                    if (reply == Reply.OK)
                    {
                        Require(text.Any(line => line.Contains("保存失败")) && text.Any(line => line.Contains("重试")),
                            "Save failure must explain that the save failed and can be retried");
                        var ok = buttons.Single(b => AutomationProperties.GetAutomationId(b) == "ConfirmOK");
                        Require(HasRoundedSurface(ok), "Save failure acknowledgment must use a rounded button");
                        Click(ok);
                    }
                    else
                    {
                        Require(text.Contains(prompt), "Missing confirmation prompt: " + prompt);
                        var yes = buttons.Single(b => AutomationProperties.GetAutomationId(b) == "ConfirmYes");
                        var no = buttons.Single(b => AutomationProperties.GetAutomationId(b) == "ConfirmNo");
                        Require(no.IsDefault && no.IsCancel && no.IsKeyboardFocused && !yes.IsDefault && !yes.IsCancel,
                            "No must have initial keyboard focus and be the default and cancel action");
                        Require(HasRoundedSurface(yes) && HasRoundedSurface(no), "Both confirmation choices must render rounded buttons");
                        var close = buttons.Single(b => b != yes && b != no);
                        Click(reply == Reply.Close ? close : (reply == Reply.Yes ? yes : no));
                    }
                    Require(!dialog.IsVisible, "The selected action did not dismiss the confirmation");
                }
                catch (Exception error) { failure = error; }
                finally
                {
                    if (failure != null)
                        foreach (var opened in Application.Current.Windows.OfType<Window>().Except(priorWindows).ToArray())
                            if (opened.IsVisible) opened.Close();
                }
            }));
            open();
            if (failure != null) throw new InvalidOperationException(caption + ": " + failure.Message, failure);
            Require(dialog != null && !dialog.IsVisible, "Missing confirmation: " + caption);
        }

        private static void EditWeek(Planner planner, string action, Action<Window, TextBox> edit)
        {
            Exception failure = null;
            planner.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate
            {
                var editor = planner.OwnedWindows.OfType<Window>().FirstOrDefault(w => w.IsVisible);
                TextBox body = null;
                string original = null;
                try
                {
                    Require(editor != null && editor.Title == "编辑周计划", "Missing weekly plan editor");
                    body = Children(editor).OfType<TextBox>().Single(b => AutomationProperties.GetName(b) == "本周计划");
                    original = body.Text;
                    edit(editor, body);
                }
                catch (Exception error) { failure = error; }
                finally
                {
                    if (editor != null && editor.IsVisible)
                    {
                        if (body != null) body.Text = original;
                        editor.Close();
                    }
                }
            }));
            Click(Button(planner, action));
            if (failure != null) throw new InvalidOperationException("Weekly plan: " + failure.Message, failure);
        }

        private static void CheckWeekConfirmation(Planner planner, string folder, DateTime now)
        {
            const string saved = "周五整理读书笔记";
            const string draft = "先把访谈提纲写完";
            const string prompt = "放弃未保存的修改？";
            Click(Button(planner, "每周汇总"));
            EditWeek(planner, "写周计划", delegate(Window editor, TextBox body)
            {
                body.Text = saved;
                using (File.Open(Path.Combine(folder, "planner.xml"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    AnswerConfirmation(editor, delegate { Click(Button(editor, "保存")); }, Reply.OK, "小黑手帐");
                }
                Require(editor.IsVisible && body.Text == saved && String.IsNullOrEmpty(new Store(folder, () => now).WeekNote(now)),
                    "A failed save must keep the draft open and leave the prior saved plan intact");
                Click(Button(editor, "保存"));
                Require(!editor.IsVisible, "Saving should dismiss the weekly plan editor");
            });
            Require(new Store(folder, () => now).WeekNote(now) == saved, "Saving a weekly plan did not persist its text");
            EditWeek(planner, "编辑周计划", delegate(Window editor, TextBox body)
            {
                Require(body.Text == saved, "Reopening the weekly plan lost the saved text");
                body.Text = draft;
                Action close = delegate { Click(Button(editor, "关闭编辑窗口")); };
                AnswerConfirmation(editor, close, Reply.No, "编辑周计划", prompt);
                Require(editor.IsVisible && body.Text == draft && new Store(folder, () => now).WeekNote(now) == saved,
                    "Keeping the weekly draft must preserve both the open editor and the prior saved plan");
                AnswerConfirmation(editor, close, Reply.Yes, "编辑周计划", prompt);
                Require(!editor.IsVisible, "Discarding the weekly draft should dismiss its editor");
            });
            Require(new Store(folder, () => now).WeekNote(now) == saved, "Discarding a weekly draft changed the saved plan");
        }

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var folder = Path.Combine(args.Length > 0 ? args[0] : Path.GetTempPath(), "task-menu-" + Guid.NewGuid().ToString("N"));
                var now = new DateTime(2026, 11, 24, 10, 0, 0);
                var store = new Store(folder, () => now);
                var task = new TaskItem { Id="reading", Day="2026-11-24", Title="阅读计划", Notes="读第一章\r\n读第二章" };
                task.Steps[0].Done = true;
                store.Tasks.Add(task);
                store.Save();
                var planner = new Planner(store) { Width=320, Height=460, ShowInTaskbar=false };
                planner.Show(); planner.Activate(); Flush();
                var menu = Open(planner, task.Title);
                menu.IsOpen = false; Flush();
                Require(planner.IsVisible && Button(planner, "任务操作：" + task.Title).IsKeyboardFocused, "Dismiss did not return focus to the dots (planner active=" +
                    planner.IsActive + ", visible=" + planner.IsVisible + ", focus=" + Keyboard.FocusedElement + ")");
                Choose(Open(planner, task.Title), "添加子任务");
                var input = Children(planner).OfType<TextBox>().Single(b => AutomationProperties.GetName(b) == "子任务名称");
                Require(input.IsKeyboardFocused && planner.OwnedWindows.Count == 0, "Add should focus an inline input");
                input.Text = "写读后感"; Click(Button(planner, "保存子任务"));
                menu = Open(planner, task.Title);
                Exception editorFailure = null;
                planner.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate
                {
                    var editor = planner.OwnedWindows.Cast<Window>().Single();
                    Require(editor.Title == "编辑任务", "Wrong editor owner or title");
                    var title = Children(editor).OfType<TextBox>().Single(b => AutomationProperties.GetName(b) == "任务名称");
                    title.Text = "阅读记录";
                    try
                    {
                        AnswerConfirmation(editor, delegate { Click(Button(editor, "删除任务")); }, Reply.Close);
                        Require(editor.IsVisible && title.Text == "阅读记录" && new Store(folder, () => now).Tasks.Single().Title == task.Title,
                            "Closing the delete confirmation must preserve unsaved editor text and the saved task");
                    }
                    catch (Exception error)
                    {
                        editorFailure = error;
                        title.Text = task.Title;
                        editor.Close();
                        return;
                    }
                    Click(Button(editor, "保存"));
                }));
                Choose(menu, "编辑");
                if (editorFailure != null) throw editorFailure;
                Require(task.Title == "阅读记录" && task.Steps.Count == 3, "Edit command lost task data");
                Choose(Open(planner, task.Title), "转长期");
                var saved = new Store(folder, () => now).Tasks.Single();
                Require(saved.IsLongTerm && saved.Day == null && saved.Steps.Count == 3 && saved.Steps[0].Done, "Conversion lost state");
                Click(Button(planner, "展开长期任务"));
                menu = Open(planner, task.Title);
                Require(!menu.Items.Cast<MenuItem>().Single(i => (string)i.Header == "转长期").IsEnabled, "Long-term conversion should be disabled");
                menu.IsOpen = false; Flush();
                menu = Open(planner, task.Title);
                AnswerConfirmation(planner, delegate { Choose(menu, "删除"); }, Reply.No);
                Require(store.Tasks.Count == 1 && new Store(folder, () => now).Tasks.Count == 1, "Cancel deleted the task");
                menu = Open(planner, task.Title);
                AnswerConfirmation(planner, delegate { Choose(menu, "删除"); }, Reply.Yes);
                Require(store.Tasks.Count == 0 && new Store(folder, () => now).Tasks.Count == 0, "Confirmed delete was lost");
                CheckWeekConfirmation(planner, folder, now);
                planner.Close(); app.Shutdown();
                Console.WriteLine("Passed native task-menu layout, focus, inline addition, editing, conversion, delete confirmation, weekly plan save, keep and discard.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
        }
    }
}
