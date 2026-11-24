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
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;

namespace Xiaohei
{
    internal static class InteractionCheck
    {
        const string title = "整理阅读小组讨论记录，把三位同学对漫画结尾的不同理解写进本周的读书笔记";
        const string first = "按发言顺序核对记录";
        const string second = "选两处原文补进读书笔记，并写下自己对结尾的理解";
        const string third = "和同学确认引用的内容";
        const string fourth = "周末一起读完下一章";
        const string longChild = "依次核对每一处引文的页码和译文，再将出处补充到书目末尾";
        static void Check(bool result, string description)
        {
            if (!result) throw new InvalidOperationException(description);
            Console.WriteLine("PASS " + description);
        }

        static IEnumerable<T> Find<T>(DependencyObject root) where T : FrameworkElement
        {
            var node = root as T;
            if (node != null) yield return node;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in Find<T>(VisualTreeHelper.GetChild(root,i))) yield return child;
        }

        static void Click(Window window, string label)
        {
            window.UpdateLayout();
            var button = Find<Button>(window).Single(x => AutomationProperties.GetName(x) == label || (x.Content as string) == label);
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            window.UpdateLayout();
        }

        static void Settle(int ms = 100)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(delegate {}));
        }

        static void CheckEditingMenu(TextBox input)
        {
            input.Focus();
            foreach (int length in new[] { 2, 0 })
            {
                input.Select(0, length);
                // Send WPF's keyboard context-menu event directly to the field without moving the pointer.
                var request = (ContextMenuEventArgs)Activator.CreateInstance(typeof(ContextMenuEventArgs),
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null,
                    new object[] { input, true }, CultureInfo.InvariantCulture);
                input.RaiseEvent(request);
                Settle();
                var menu = PresentationSource.CurrentSources.OfType<HwndSource>().Where(source => source.RootVisual != null)
                    .SelectMany(source => Find<ContextMenu>(source.RootVisual))
                    .Single(candidate => candidate.IsOpen && candidate.PlacementTarget == input);
                try
                {
                    var surface = menu.Template.FindName("MenuSurface", menu) as Border;
                    Check(surface != null && surface.Background == PetMenu.PopupFill && surface.BorderBrush == PetMenu.PopupEdge &&
                        menu.Foreground == PetMenu.PopupInk, "default text editing menu uses the shared popup colors");
                    foreach (var command in new[] { ApplicationCommands.Cut, ApplicationCommands.Copy, ApplicationCommands.Paste })
                    {
                        var item = menu.Items.OfType<MenuItem>().Single(entry => entry.Command == command);
                        bool enabled = command == ApplicationCommands.Paste ? command.CanExecute(null, input) : length > 0;
                        Check(item.CommandTarget == input && item.IsEnabled == enabled,
                            command.Name + " keeps its target and availability for selection length " + length);
                    }
                }
                finally { menu.IsOpen = false; Settle(); }
            }
        }

        static void Legacy(string folder)
        {
            Directory.CreateDirectory(folder);
            var day = DateTime.Today.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
            var tasks = new XElement("Tasks",new XElement("Task",new XAttribute("id","reading"),
                new XAttribute("day",day),new XAttribute("done",false),
                new XElement("Title",title),new XElement("Notes",first+"\n\n"+second)));
            for (int i = 1; i <= 16; i++)
                tasks.Add(new XElement("Task",new XAttribute("id","note"+i),new XAttribute("day",day),
                    new XAttribute("done",false),new XElement("Title","校对第"+i+"页摘录"), new XElement("Notes",i == 1 ? longChild : "")));
            new XDocument(new XElement("Planner",new XAttribute("version","1"),tasks,new XElement("Weeks"),
                new XElement("Size",new XAttribute("plannerWidth",310),new XAttribute("plannerHeight",310))))
                .Save(Path.Combine(folder,"planner.xml"));
        }

        static TaskItem Read(string folder)
        {
            return new Store(folder).Tasks.Single(x=>x.Id=="reading");
        }

        static void CheckDraftOutline(Planner planner, TextBox field)
        {
            var inputFrame = (Border)field.Template.FindName("field", field);
            DependencyObject parent = VisualTreeHelper.GetParent(field);
            while (parent != null && !(parent is Border)) parent = VisualTreeHelper.GetParent(parent);
            var outer = parent as Border;
            Check(field.IsKeyboardFocused && inputFrame.BorderThickness == new Thickness(0) &&
                field.FocusVisualStyle == null && outer != null && outer.BorderThickness == new Thickness(1) &&
                outer.CornerRadius.TopLeft > 0, "focused subtask editor has one rounded outer outline");
            var active = outer.BorderBrush;
            Find<Button>(planner).Single(b => (b.Content as string) == "日历计划").Focus();
            Check(!outer.IsKeyboardFocusWithin && !Equals(outer.BorderBrush, active) && inputFrame.BorderThickness == new Thickness(0),
                "keyboard focus changes the existing outline without adding an inner one");
            field.Focus();
        }

        static void AddStep(Planner planner, string step)
        {
            var more = Find<Button>(planner).Single(b => AutomationProperties.GetName(b) == "任务操作：" + title);
            more.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            more.ContextMenu.Items.OfType<MenuItem>().Single(item => (string)item.Header == "添加子任务")
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            planner.UpdateLayout();
            Settle();
            var box=Find<TextBox>(planner).Single(x=>AutomationProperties.GetName(x)=="子任务名称");
            Check(planner.OwnedWindows.Count==0 && box.IsKeyboardFocused,
                "add-subtask focuses an inline field in the same planner");
            CheckDraftOutline(planner, box);
            box.Text=step;
            Click(planner,"保存子任务");
            Settle();
        }

        static void Toggle(Planner planner, string label, bool done)
        {
            var checkbox = Find<CheckBox>(planner).Single(x=>AutomationProperties.GetName(x) == label);
            checkbox.IsChecked = done;
            checkbox.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            planner.UpdateLayout();
        }

        static RenderTargetBitmap Render(FrameworkElement view)
        {
            view.UpdateLayout();
            var pixels = new RenderTargetBitmap((int)Math.Ceiling(2*view.ActualWidth),
                (int)Math.Ceiling(2*view.ActualHeight),192,192,PixelFormats.Pbgra32);
            var copy = new DrawingVisual();
            var bounds = new Rect(0,0,view.ActualWidth,view.ActualHeight);
            var brush = new VisualBrush(view) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = bounds };
            using (var dc = copy.RenderOpen()) dc.DrawRectangle(brush,null,bounds);
            pixels.Render(copy);
            return pixels;
        }

        static bool AllowsHover(FrameworkElement row)
        {
            // WPF keeps this event-args constructor internal. Raise the actual tooltip-service
            // preview event without moving the system pointer or synthesizing mouse input.
            var args = (ToolTipEventArgs)Activator.CreateInstance(typeof(ToolTipEventArgs),
                System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,
                new object[]{true},CultureInfo.InvariantCulture);
            row.RaiseEvent(args);
            return row.ToolTip is ToolTip && ToolTipService.GetIsEnabled(row) && !args.Handled;
        }

        static FrameworkElement HoverOwner(FrameworkElement element)
        {
            for (DependencyObject node = element; node != null; node = VisualTreeHelper.GetParent(node))
            {
                var owner = node as FrameworkElement;
                if (owner != null && owner.ToolTip != null) return owner;
            }
            return null;
        }

        static void Hover(Planner planner, string path)
        {
            var row = Find<Border>(planner).Single(x=>AutomationProperties.GetName(x)=="任务详情："+title);
            row.BringIntoView();
            Settle();
            var parent = Find<Button>(row).Single(b => AutomationProperties.GetName(b) == "编辑任务：" + title);
            var shortChild = HoverOwner(Find<TextBlock>(row).Single(t => t.Text == first));
            var child = HoverOwner(Find<TextBlock>(row).Single(t => t.Text == second));
            var other = Find<Border>(planner).Single(b => AutomationProperties.GetName(b) == "任务详情：校对第1页摘录");
            var shortParent = Find<Button>(other).Single(b => AutomationProperties.GetName(b) == "编辑任务：校对第1页摘录");
            var otherChild = HoverOwner(Find<TextBlock>(other).Single(t => t.Text == longChild));
            Check(AllowsHover(parent) && AllowsHover(child) && !AllowsHover(shortChild) &&
                !AllowsHover(shortParent) && AllowsHover(otherChild), "each parent or child checks only its own title for ellipsis");
            Check(row.ToolTip == null && Find<CheckBox>(row).All(c => c.ToolTip == null) &&
                Find<Button>(row).Single(b => AutomationProperties.GetName(b) == "任务操作：" + title).ToolTip == null,
                "card, checkboxes and overflow button do not show task details");
            foreach (var target in new FrameworkElement[] { parent, child })
            {
                target.BringIntoView();
                Settle();
                var text = Find<TextBlock>(target).Single();
                double textTop = text.TranslatePoint(new Point(), target).Y;
                // The center of the padding above the text belongs to the row;
                // a point near its upper-right corner can be outside its rounded button.
                var point = new Point(target.ActualWidth / 2, textTop / 2);
                var hit = planner.InputHitTest(target.TranslatePoint(point, planner)) as FrameworkElement;
                var owner = hit == null ? null : HoverOwner(hit);
                int delay = ToolTipService.GetInitialShowDelay(target);
                Check(textTop > 0 && owner == target && delay == 700, String.Format(CultureInfo.InvariantCulture,
                    "central blank text area hits its delayed tooltip: target={0}, size={1}, local={2}, textTop={3:F2}, hit={4}, owner={5}, delay={6}",
                    target.GetType().Name, target.RenderSize, point, textTop, hit == null ? "none" : hit.GetType().Name,
                    owner == null ? "none" : owner.GetType().Name, delay));
            }
            var viewport = Find<ScrollViewer>(planner).First(x => x.ScrollableHeight > 0);
            viewport.ScrollToEnd();
            Settle();
            Check(other.TranslatePoint(new Point(), viewport).Y < 0 && !AllowsHover(shortParent),
                "scrolling a short title outside the viewport never makes it eligible");
            row.BringIntoView();
            Settle();
            var parentTip = (ToolTip)parent.ToolTip;
            parentTip.IsOpen = true;
            Settle();
            Check(Find<TextBlock>(parentTip).Select(t => t.Text).SequenceEqual(new[] { title }),
                "parent tooltip displays only its full title");
            double narrow = planner.Width;
            planner.Width = 760;
            Settle();
            Check(!parentTip.IsOpen && !AllowsHover(parent) && !AllowsHover(child), "widening closes a full-width title's tip and prevents reopening");
            planner.Width = narrow;
            Settle();
            Check(AllowsHover(child), "narrowing makes the truncated child eligible again");
            var tip = (ToolTip)child.ToolTip;
            tip.IsOpen = true;
            Settle(200);
            var source = PresentationSource.FromVisual(tip) as HwndSource;
            Check(source != null && source.Handle != IntPtr.Zero && tip.PlacementTarget == child &&
                tip.Placement == PlacementMode.Custom && tip.CustomPopupPlacementCallback != null,
                "the child uses a real popup anchored to its own text region with custom placement");
            Check(Find<TextBlock>(tip).Select(t => t.Text).SequenceEqual(new[] { second }),
                "child tooltip displays only its full title without completion marks or sibling text");
            var tipFrame = Find<Border>(tip).First(b => b.Background is SolidColorBrush);
            var edge = tipFrame.BorderBrush as SolidColorBrush;
            Check(((SolidColorBrush)tipFrame.Background).Color == Color.FromRgb(0xD6, 0xDF, 0xD4) &&
                edge != null && edge.Color == Color.FromRgb(0x8D, 0xA3, 0x8F) && tipFrame.BorderThickness.Top > 0,
                "the rendered title tooltip uses the shared gray-green fill and edge");
            viewport.ScrollToVerticalOffset(viewport.VerticalOffset + row.TranslatePoint(new Point(),viewport).Y - 3);
            Settle();
            var surface = (FrameworkElement)planner.Content;
            var page = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth * 2),
                (int)Math.Ceiling(surface.ActualHeight * 2),192,192,PixelFormats.Pbgra32);
            page.Render(surface);
            var box = row.TransformToAncestor(surface).TransformBounds(new Rect(row.RenderSize));
            var visible = viewport.TransformToAncestor(surface).TransformBounds(new Rect(viewport.RenderSize));
            box.Intersect(visible);
            if (box.IsEmpty || box.Height < 10) throw new Exception("Task row is outside its scrolling viewport");
            var rows = new CroppedBitmap(page,new Int32Rect((int)(2*box.Left),(int)(2*box.Top),
                (int)(2*box.Width),(int)(2*box.Height)));
            var details = Render((FrameworkElement)source.RootVisual);
            double width = Math.Max(rows.Width,details.Width)+28, height = rows.Height+details.Height+42;
            var visual = new DrawingVisual();
            using (var c = visual.RenderOpen())
            {
                c.DrawRectangle(new SolidColorBrush(Color.FromRgb(229,237,226)),null,new Rect(0,0,width,height));
                c.DrawRectangle(Brushes.White,null,new Rect(14,14,rows.Width,rows.Height));
                c.DrawImage(rows,new Rect(14,14,rows.Width,rows.Height));
                c.DrawImage(details,new Rect(14,28+rows.Height,details.Width,details.Height));
            }
            var image = new RenderTargetBitmap((int)Math.Ceiling(width*2),(int)Math.Ceiling(height*2),
                192,192,PixelFormats.Pbgra32);
            image.Render(visual);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(image));
            using(var stream=File.Create(path)) png.Save(stream);
            Click(planner,"日历计划");
            Settle();
            Check(!tip.IsOpen && PresentationSource.FromVisual(child) == null &&
                tip.CustomPopupPlacementCallback(new Size(160, 35), child.RenderSize, new Point()).Length > 0,
                "changing views dismisses the old title tooltip and late placement tolerates a detached target");
        }

        static void FitAndScroll(Planner planner, DesktopPet pet, string folder)
        {
            var scroll = Find<ScrollViewer>(planner).First(x=>x.ScrollableHeight>0);
            scroll.ScrollToHome();
            Settle();
            Check(scroll.VerticalScrollBarVisibility==ScrollBarVisibility.Hidden &&
                !Find<ScrollBar>(scroll).Any(x=>x.Orientation==Orientation.Vertical && x.IsVisible),
                "long list hides its vertical scrollbar");
            scroll.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120)
                { RoutedEvent=Mouse.MouseWheelEvent });
            Settle();
            Check(scroll.VerticalOffset>0, "mouse wheel still scrolls hidden-bar task list");
            var header = Find<TextBlock>(planner).Single(x=>x.Text=="小黑手帐");
            var args = new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left)
                { RoutedEvent=Control.PreviewMouseDoubleClickEvent, Source=header };
            // The crowded list can already occupy all available height on open.
            planner.Height = planner.MinHeight;
            Settle();
            var oldHeight = planner.ActualHeight;
            planner.RaiseEvent(args);
            Settle();
            var frame = new Rect(planner.Left,planner.Top,planner.ActualWidth,planner.ActualHeight);
            var petFrame = new Rect(pet.Left,pet.Top,pet.ActualWidth,pet.ActualHeight);
            var overlap = Rect.Intersect(frame,petFrame);
            var work = SystemParameters.WorkArea; work.Inflate(1,1);
            Check(planner.ActualHeight>oldHeight && work.Contains(frame) &&
                (overlap.IsEmpty || overlap.Width*overlap.Height==0),
                "double-click fits crowded planner on-screen beside pet");
            var saved = new Store(folder);
            Check(Math.Abs(saved.PlannerWidth-planner.Width)<1 && Math.Abs(saved.PlannerHeight-planner.Height)<1,
                "fitted planner size persists to disk");
        }

        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                var app = new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
                PetMenu.StyleTextEditingMenus();
                var folder = Path.Combine(args[0],"interaction-"+Guid.NewGuid().ToString("N"));
                Legacy(folder);
                Check(Read(folder).Steps.Select(x=>x.Title).SequenceEqual(new[]{first,second}),
                    "existing multiline plan migrates to ordered steps");
                var sessionDay = DateTime.Today.AddHours(10);
                var pet = new DesktopPet(new Store(folder,delegate { return sessionDay; })) {ShowActivated=false};
                pet.Show(); pet.ShowPlanner(); Settle();
                var planner = pet.OwnedWindows.OfType<Planner>().Single();
                Click(planner,"＋ 新建任务");
                Click(planner,"添加子任务");
                var notes = Find<TextBox>(planner).Single(x => AutomationProperties.GetName(x) == "子任务（每行一项）");
                CheckDraftOutline(planner, notes);
                Click(planner,"取消");
                AddStep(planner,third);
                Toggle(planner,"完成子任务："+first,true);
                var task = Read(folder);
                Check(task.Steps.Count==3 && task.Steps[0].Done && !task.Done,"daily add and individual check survive reload");
                Toggle(planner,"完成任务："+title,true);
                Check(Read(folder).Steps.All(x=>x.Done),"parent check completes all steps");
                Click(planner,"每周汇总");
                AddStep(planner,fourth);
                task=Read(folder);
                Check(!task.Done && task.Steps.Count==4 && task.Steps.Take(3).All(x=>x.Done) &&
                    !task.Steps.Last().Done,"weekly add reopens completed task while preserving three checks");
                Toggle(planner,"完成子任务："+fourth,true);
                Check(Read(folder).Done,"checking last weekly step completes its parent");
                Toggle(planner,"完成子任务："+second,false);
                Check(!Read(folder).Done,"reopening a weekly step reopens its parent");
                Hover(planner,Path.Combine(args[0],"NewInteractions.png"));
                Check(Find<CheckBox>(planner).Single(x=>AutomationProperties.GetName(x)=="完成子任务："+second)
                    .IsChecked==false,"weekly step change appears in daily view");
                FitAndScroll(planner,pet,folder);
                Click(planner,"＋ 新建任务");
                var titleBox=Find<TextBox>(planner).Single(x=>AutomationProperties.GetName(x)=="任务名称");
                const string draft = "明天核对摘录出处";
                titleBox.Text=draft;
                CheckEditingMenu(titleBox);
                var outside = new Window { Width=40,Height=40,ShowInTaskbar=false,
                    WindowStyle=WindowStyle.None,Left=SystemParameters.WorkArea.Left+2,Top=SystemParameters.WorkArea.Top+2 };
                outside.Show(); outside.Activate(); Settle(500);
                Check(!planner.IsVisible && pet.IsVisible && new Store(folder).Tasks.Count(x=>x.Title==draft)==1,
                    "activating an unrelated test window dismisses planner and saves its draft once");
                outside.Close(); pet.Close(); app.Shutdown();
                return 0;
            }
            catch(Exception error) {Console.Error.WriteLine(error); return 1;}
        }
    }
}
