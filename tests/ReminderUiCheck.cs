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
    internal static class ReminderUiCheck
    {
        static DateTime now = new DateTime(2026,11,26,17,59,59);
        const string due = "核对读书笔记";
        const string renamed = "核对小组读书笔记";
        const string friday = "整理周五的讨论记录";
        const string saturday = "周末去图书馆";
        static readonly CultureInfo Zh = CultureInfo.GetCultureInfo("zh-CN");

        static void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
            Console.WriteLine("PASS "+message);
        }

        static IEnumerable<T> Find<T>(DependencyObject node) where T : FrameworkElement
        {
            if (node is T) yield return (T)node;
            for (int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)
                foreach(var t in Find<T>(VisualTreeHelper.GetChild(node,i))) yield return t;
        }

        static void Flush()
        {
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(delegate {}));
        }

        static void Click(Window window,string label)
        {
            window.UpdateLayout();
            var button=Find<Button>(window).Single(x=>AutomationProperties.GetName(x)==label || x.Content as string==label);
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); window.UpdateLayout(); Flush();
        }

        static void Day(Planner planner, DateTime date)
        {
            var name=date.ToString("M月d日 dddd",Zh);
            var pick=Find<Button>(planner).Single(x=>AutomationProperties.GetName(x).StartsWith(name,StringComparison.Ordinal));
            pick.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); planner.UpdateLayout(); Flush();
        }

        static void Toggle(Planner planner,string name,bool on)
        {
            var box=Find<CheckBox>(planner).Single(x=>AutomationProperties.GetName(x)==name);
            box.IsChecked=on;
            box.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); planner.UpdateLayout(); Flush();
        }

        static Border Row(Planner planner,string title)
        {
            return Find<Border>(planner).Single(x=>AutomationProperties.GetName(x)=="任务详情："+title);
        }

        static bool Red(Planner planner,string title)
        {
            var row=Row(planner,title);
            var ink=row.BorderBrush as SolidColorBrush;
            return ink!=null && ink.Color.R > 1.35*ink.Color.G && ink.Color.R > 1.35*ink.Color.B &&
                row.BorderThickness.Top>0 && row.BorderThickness.Left>0;
        }

        static void Badge(DesktopPet pet,int count)
        {
            pet.UpdateLayout();
            var badge=Find<Border>(pet).Single(x=>AutomationProperties.GetAutomationId(x)=="PendingBadge");
            string text=Find<TextBlock>(badge).Single().Text;
            Check(count==0 ? badge.Visibility==Visibility.Collapsed : badge.IsVisible &&
                text==(count>99?"99+":count.ToString(CultureInfo.InvariantCulture)),
                "pet badge represents "+count+" unfinished parent tasks at "+now.ToString("MM-dd HH:mm:ss"));
        }

        static TaskItem NewTask(string name,DateTime date)
        {
            return new TaskItem { Id=Guid.NewGuid().ToString("N"),Day=date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),Title=name };
        }

        static void Edit(Planner planner)
        {
            Exception failure=null;
            planner.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(delegate {
                var editor=planner.OwnedWindows.Cast<Window>().Single();
                var name=Find<TextBox>(editor).Single(x=>AutomationProperties.GetName(x)=="任务名称");
                var steps=Find<TextBox>(editor).Single(x=>AutomationProperties.GetName(x)=="子任务（每行一项）");
                var previousSteps=steps.Text;
                try { name.Text=renamed; steps.Text="核对两条引文"; Click(editor,"保存"); }
                catch(Exception error) { failure=error; steps.Text=previousSteps; name.Text=due; editor.Close(); }
            }));
            Click(planner,"编辑任务："+due);
            if(failure!=null) throw failure;
        }

        static void Time(DesktopPet pet,DateTime target)
        {
            now=target; pet.RefreshReminders(); Flush();
        }

        static RenderTargetBitmap Render(FrameworkElement view)
        {
            view.UpdateLayout();
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth*2),
                (int)Math.Ceiling(view.ActualHeight*2),192,192,PixelFormats.Pbgra32);
            bitmap.Render(view);return bitmap;
        }

        static void Preview(Planner planner,DesktopPet pet,string path)
        {
            Row(planner,friday).BringIntoView();
            Flush(); planner.UpdateLayout();
            var page=Render((FrameworkElement)planner.Content);
            var cat=Render((FrameworkElement)pet.Content);
            double width=page.Width+cat.Width+44, height=page.Height+28;
            var drawing=new DrawingVisual();
            using(var dc=drawing.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(219,229,220)),null,new Rect(0,0,width,height));
                dc.DrawImage(page,new Rect(14,14,page.Width,page.Height));
                dc.DrawImage(cat,new Rect(page.Width+28,height-14-cat.Height,cat.Width,cat.Height));
            }
            var result=new RenderTargetBitmap((int)Math.Ceiling(width*2),(int)Math.Ceiling(height*2),192,192,PixelFormats.Pbgra32);
            result.Render(drawing);
            var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(result));
            using(var output=File.Create(path)) png.Save(output);
        }

        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
                var seed=now.Date;
                string folder=Path.Combine(args[0],"reminder-ui-"+Guid.NewGuid().ToString("N"));
                var store=new Store(folder,()=>now);
                var task=NewTask(due,seed); task.SetPlan("核对引文");
                store.Tasks.Add(task); store.Tasks.Add(NewTask(friday,seed.AddDays(1))); store.Tasks.Add(NewTask(saturday,seed.AddDays(2)));
                store.Save();
                var pet=new DesktopPet(store) { ShowActivated=false }; pet.Show(); pet.ShowPlanner(); Flush();
                var planner=pet.OwnedWindows.OfType<Planner>().Single();
                Badge(pet,1);
                Check(!Red(planner,due),"Thursday 17:59:59 has a normal task outline");
                Toggle(planner,"完成任务："+due,true); Badge(pet,0);
                Toggle(planner,"完成子任务：核对引文",false); Badge(pet,1);
                Edit(planner);
                var reloaded=new Store(folder,()=>now).Tasks.Single(x=>x.Id==task.Id);
                Check(reloaded.Title==renamed && reloaded.Steps.Single().Title=="核对两条引文",
                    "parent and child renames survive disk reload");
                var more = Find<Button>(planner).Single(b => AutomationProperties.GetName(b) == "任务操作：" + renamed);
                more.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                more.ContextMenu.Items.OfType<MenuItem>().Single(item => (string)item.Header == "添加子任务")
                    .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                planner.UpdateLayout(); Flush();
                var stepInput=Find<TextBox>(planner).Single(x=>AutomationProperties.GetName(x)=="子任务名称");
                stepInput.Text="补抄讨论记录";
                Time(pet,seed.AddHours(18)); Badge(pet,1);
                Check(stepInput.IsVisible && stepInput.Text=="补抄讨论记录" &&
                    Find<TextBox>(planner).Single(x=>AutomationProperties.GetName(x)=="子任务名称")==stepInput,
                    "18:00 refresh keeps unfinished inline subtask text and its input control");
                Click(planner,"取消子任务");
                Check(task.Steps.Count==1,"cancel discards the unsaved step without adding a duplicate");
                Check(Red(planner,renamed),"daily 18:00 marks the unfinished row on today's screen");
                Day(planner,seed.AddDays(1));
                Check(Red(planner,renamed) && !Red(planner,friday),"carried task is red tomorrow and future task stays normal");
                Click(planner,"今天");
                Time(pet,seed.AddDays(1)); Badge(pet,2);
                Check(Red(planner,renamed) && !Red(planner,friday),"midnight updates selected day and badge");
                Time(pet,seed.AddDays(1).AddHours(18).AddSeconds(-1));
                Day(planner,seed.AddDays(2));
                Check(!Red(planner,saturday),"future Saturday task has no red outline before Friday cutoff");
                Click(planner,"今天");
                Time(pet,seed.AddDays(1).AddHours(18)); Badge(pet,2);
                Check(Red(planner,renamed) && Red(planner,friday),"Friday 18:00 outlines today's pending main tasks");
                Preview(planner,pet,Path.Combine(args[0],"ReminderPreview.png"));
                Day(planner,seed.AddDays(2));
                Check(Red(planner,saturday),"Friday review also marks the outstanding future task");
                Click(planner,"今天");
                Time(pet,seed.AddDays(2)); Badge(pet,3);
                Check(Red(planner,renamed) && Red(planner,friday) && Red(planner,saturday),
                    "Saturday shows two carried tasks and the scheduled Saturday task");
                Toggle(planner,"完成子任务：核对两条引文",true); Badge(pet,2);
                Toggle(planner,"完成任务："+friday,true); Badge(pet,1);
                Toggle(planner,"完成任务："+saturday,true); Badge(pet,0);
                Check(!Red(planner,saturday),"completing a marked task removes its red outline");
                for(int i=0;i<103;i++) store.Tasks.Add(NewTask("清点第"+(i+1)+"项资料",now));
                store.Save(); Flush(); Badge(pet,103);
                pet.Close(); app.Shutdown(); return 0;
            }
            catch(Exception e) { Console.Error.WriteLine(e); return 1; }
        }
    }
}
