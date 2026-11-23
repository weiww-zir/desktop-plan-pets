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

        static void Legacy(string folder)
        {
            Directory.CreateDirectory(folder);
            var day = DateTime.Today.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
            var tasks = new XElement("Tasks",new XElement("Task",new XAttribute("id","reading"),
                new XAttribute("day",day),new XAttribute("done",false),
                new XElement("Title",title),new XElement("Notes",first+"\n\n"+second)));
            for (int i = 1; i <= 16; i++)
                tasks.Add(new XElement("Task",new XAttribute("id","note"+i),new XAttribute("day",day),
                    new XAttribute("done",false),new XElement("Title","校对第"+i+"页摘录"), new XElement("Notes","")));
            new XDocument(new XElement("Planner",new XAttribute("version","1"),tasks,new XElement("Weeks"),
                new XElement("Size",new XAttribute("plannerWidth",310),new XAttribute("plannerHeight",310))))
                .Save(Path.Combine(folder,"planner.xml"));
        }

        static TaskItem Read(string folder)
        {
            return new Store(folder).Tasks.Single(x=>x.Id=="reading");
        }

        static void AddStep(Planner planner, string step)
        {
            Click(planner,"添加子任务："+title);
            Settle();
            var box=Find<TextBox>(planner).Single(x=>AutomationProperties.GetName(x)=="子任务名称");
            Check(planner.OwnedWindows.Count==0 && box.IsKeyboardFocused,
                "add-subtask focuses an inline field in the same planner");
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

        static bool AllowsHover(Border row)
        {
            // WPF keeps this event-args constructor internal. Raise the actual tooltip-service
            // preview event without moving the system pointer or synthesizing mouse input.
            var args = (ToolTipEventArgs)Activator.CreateInstance(typeof(ToolTipEventArgs),
                System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,
                new object[]{true},CultureInfo.InvariantCulture);
            row.RaiseEvent(args);
            return !args.Handled;
        }

        static void Hover(Planner planner, string path)
        {
            var row = Find<Border>(planner).Single(x=>AutomationProperties.GetName(x)=="任务详情："+title);
            row.BringIntoView();
            Settle();
            var tip = row.ToolTip as ToolTip;
            var shortRow = Find<Border>(planner).Single(x=>AutomationProperties.GetName(x)=="任务详情：校对第1页摘录");
            shortRow.BringIntoView();
            Settle();
            bool shortOpens = AllowsHover(shortRow);
            row.BringIntoView();
            Settle();
            Check(tip != null && ToolTipService.GetIsEnabled(row) && AllowsHover(row) && !shortOpens,
                "tooltip opens for truncated information and stays closed for short rows");
            Check(!Find<Button>(row).Any(x=>Convert.ToString(x.ToolTip).StartsWith("编辑任务")),
                "editable row does not advertise an edit-action tooltip");
            tip.PlacementTarget = row;
            tip.IsOpen = true;
            Settle(200);
            var source = PresentationSource.FromVisual(tip) as HwndSource;
            Check(source != null && source.Handle != IntPtr.Zero, "details tooltip opens in its own popup");
            var detail = String.Join("\n",Find<TextBlock>(tip).Select(x=>x.Text));
            Check(new[]{title,first,second,third,fourth}.All(detail.Contains),"hover reveals full task and all four step texts");
            Check(detail.Contains("☑ "+first) && detail.Contains("☐ "+second),
                "expanded details reflect current checked and unchecked states");
            var viewport = Find<ScrollViewer>(planner).First(x => x.ScrollableHeight > 0);
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
            tip.IsOpen = false;
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
                var folder = Path.Combine(args[0],"interaction-"+Guid.NewGuid().ToString("N"));
                Legacy(folder);
                Check(Read(folder).Steps.Select(x=>x.Title).SequenceEqual(new[]{first,second}),
                    "existing multiline plan migrates to ordered steps");
                var sessionDay = DateTime.Today.AddHours(10);
                var pet = new DesktopPet(new Store(folder,delegate { return sessionDay; })) {ShowActivated=false};
                pet.Show(); pet.ShowPlanner(); Settle();
                var planner = pet.OwnedWindows.OfType<Planner>().Single();
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
                Click(planner,"日历计划");
                Check(Find<CheckBox>(planner).Single(x=>AutomationProperties.GetName(x)=="完成子任务："+second)
                    .IsChecked==false,"weekly step change appears in daily view");
                FitAndScroll(planner,pet,folder);
                Click(planner,"＋ 新建任务");
                var titleBox=Find<TextBox>(planner).Single(x=>AutomationProperties.GetName(x)=="任务名称");
                const string draft = "明天核对摘录出处";
                titleBox.Text=draft;
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
