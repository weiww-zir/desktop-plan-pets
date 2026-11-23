using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Xiaohei
{
    internal static class FrameResizeCheck
    {
        [StructLayout(LayoutKind.Sequential)] struct Pixel { internal int X,Y; }
        [DllImport("user32.dll")] static extern bool GetCursorPos(out Pixel point);
        [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Pixel point);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint process);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr info);
        static Pixel original, last;
        static bool moved, held;
        static uint releaseFlag;
        static readonly uint processId=(uint)Process.GetCurrentProcess().Id;
        static void Check(bool ok,string message)
        {
            if(!ok) throw new InvalidOperationException(message);
            Console.WriteLine("PASS "+message);
        }
        static void Pump(int ms=36)
        {
            var frame=new DispatcherFrame();
            var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(ms)};
            timer.Tick+=delegate {timer.Stop();frame.Continue=false;};
            timer.Start();Dispatcher.PushFrame(frame);
        }
        static Pixel Screen(FrameworkElement el,double x,double y)
        {
            Point p=el.PointToScreen(new Point(x,y));
            return new Pixel {X=(int)Math.Round(p.X),Y=(int)Math.Round(p.Y)};
        }
        static void Verify(Pixel p,FrameworkElement el)
        {
            var source=PresentationSource.FromVisual(el) as HwndSource;
            var hit=WindowFromPoint(p);
            uint pid;
            GetWindowThreadProcessId(hit,out pid);
            if(source==null || hit!=source.Handle || pid!=processId)
                throw new InvalidOperationException("Point is outside its intended test HWND. No click sent.");
        }
        static void Move(Pixel p)
        {
            Pixel cursor;
            if(moved && GetCursorPos(out cursor) && (Math.Abs(cursor.X-last.X)>2 || Math.Abs(cursor.Y-last.Y)>2))
                throw new InvalidOperationException("Pointer moved independently; stopping.");
            if(!SetCursorPos(p.X,p.Y)) throw new InvalidOperationException("Mouse cursor is unavailable");
            moved=true; last=p;
            Pump(30);
        }
        static void Press(FrameworkElement el,bool right=false)
        {
            Pixel p;
            if(!GetCursorPos(out p) || p.X!=last.X || p.Y!=last.Y)
                throw new InvalidOperationException("Unexpected pointer position");
            Verify(p,el);
            if((GetAsyncKeyState(1)&0x8000)!=0 || (GetAsyncKeyState(2)&0x8000)!=0)
                throw new InvalidOperationException("Another mouse press is in progress");
            releaseFlag=right?16u:4u; held=true;
            mouse_event(right?8u:2u,0,0,0,UIntPtr.Zero);Pump(45);
        }
        static void Release()
        {
            if(!held)return;
            mouse_event(releaseFlag,0,0,0,UIntPtr.Zero);held=false;Pump(55);
        }
        static Rect Bounds(Window w){return new Rect(w.Left,w.Top,w.ActualWidth,w.ActualHeight);}
        static bool Near(double a,double b){return Math.Abs(a-b)<2.2;}
        static void Clear(Planner w,DesktopPet pet)
        {
            var overlap=Rect.Intersect(Bounds(w),Bounds(pet));
            Check(overlap.IsEmpty || overlap.Width*overlap.Height==0,"planner stays clear of visible pet");
        }
        static Thumb GripAt(Planner w,int horizontal,int vertical,out Pixel start)
        {
            double x=horizontal<0?7:horizontal>0?w.ActualWidth-7:w.ActualWidth/2;
            double y=vertical<0?7:vertical>0?w.ActualHeight-7:w.ActualHeight/2;
            if(horizontal==0) y=vertical<0?3:w.ActualHeight-3;
            if(vertical==0) x=horizontal<0?3:w.ActualWidth-3;
            var target=w.InputHitTest(new Point(x,y)) as DependencyObject;
            while(target!=null && !(target is Thumb))target=VisualTreeHelper.GetParent(target);
            Check(target!=null,"real border coordinate hits a Thumb ("+horizontal+","+vertical+")");
            start=Screen(w,x,y);
            Verify(start,w);
            return (Thumb)target;
        }
        static void Drag(Planner w,int h,int v,double dx,double dy)
        {
            Pixel p;var thumb=GripAt(w,h,v,out p);
            var px=PresentationSource.FromVisual(w).CompositionTarget.TransformToDevice;
            var shift=px.Transform(new Vector(dx,dy));
            Move(p);Press(w);
            Check(thumb.IsDragging && thumb.IsMouseCaptured,"OS button-down starts actual WPF thumb capture");
            for(int i=1;i<=3;i++)
            {
                if(!thumb.IsMouseCaptured)throw new Exception("Test thumb lost capture mid-drag");
                Move(new Pixel {X=p.X+(int)Math.Round(shift.X*i/3),Y=p.Y+(int)Math.Round(shift.Y*i/3)});
            }
            Release();w.UpdateLayout();Pump();
            Check(!thumb.IsDragging && !thumb.IsMouseCaptured,"release completes captured drag");
        }
        static void Position(Planner w,Rect work)
        {
            w.Width=Math.Min(372,work.Width-24);w.Height=Math.Min(425,work.Height-30);
            w.Left=work.Left+12;w.Top=work.Top+15;w.Activate();w.UpdateLayout();Pump(65);
        }
        static void SizeMenu(DesktopPet pet,string folder)
        {
            pet.Activate();Pump();
            var hotspot=Screen(pet,pet.ActualWidth*.45,pet.ActualHeight*.45);
            Verify(hotspot,pet);Move(hotspot);Press(pet,true);Release();Pump(180);
            var root=pet.ContextMenu;
            Check(root.IsOpen,"actual pet right click opens the native menu");
            var sizes=root.Items.OfType<MenuItem>().Single(x=>(string)x.Header=="桌宠大小");
            Move(Screen(sizes,sizes.ActualWidth*.5,sizes.ActualHeight*.5));Pump(650);
            Check(sizes.IsSubmenuOpen,"real hover opens size submenu");
            var choice=sizes.Items.OfType<MenuItem>().Single(x=>(string)x.Header=="适中（120）");
            var popup=PresentationSource.FromVisual(choice) as HwndSource;
            if(popup==null)throw new InvalidOperationException("Size submenu HWND is missing");
            var left=Screen(choice,0,0);
            var a=last;
            var entry=Screen(choice,choice.ActualWidth*.5,choice.ActualHeight*.5);
            bool opensLeft=left.X<Screen(sizes,0,0).X;
            int inside=opensLeft?Screen(choice,choice.ActualWidth-6,0).X:Screen(choice,6,0).X;
            for(int i=1;i<=7;i++)
                Move(new Pixel{X=a.X+(inside-a.X)*i/7,Y=a.Y});
            Check(sizes.IsSubmenuOpen,"submenu survives traversing its boundary with the pointer");
            Move(entry);Press(choice);Release();Pump(100);
            Check(Near(pet.Width,120) && Near(new Store(folder).PetWidth,120),
                "native submenu click applies 120-DIP size and persists it"+(opensLeft?" in leftward cascade":""));
        }
        [STAThread] static int Main(string[] args)
        {
            if(!GetCursorPos(out original))return 2;
            if((GetAsyncKeyState(1)&0x8000)!=0 || (GetAsyncKeyState(2)&0x8000)!=0)return 3;
            try
            {
                var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
                var work=SystemParameters.WorkArea;
                if(work.Width<510 || work.Height<430)throw new Exception("Test needs a 510×430 DIP work area");
                var folder=Path.Combine(args[0],"frame-resize-"+Guid.NewGuid().ToString("N"));
                var store=new Store(folder);
                store.PetLeft=work.Right-100;store.PetTop=work.Top+Math.Round(work.Height*.58);
                for(int i=1;i<=15;i++)store.Tasks.Add(new TaskItem {Id="page"+i,
                    Day=DateTime.Today.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),Title="校对第"+i+"页"});
                store.Save();var pet=new DesktopPet(store);pet.Show();pet.ShowPlanner();Pump();
                var w=pet.OwnedWindows.OfType<Planner>().Single();
                foreach(var side in new[]{new[]{-1,0},new[]{1,0},new[]{0,-1},new[]{0,1},new[]{-1,-1},new[]{1,-1},new[]{-1,1},new[]{1,1}})
                {
                    int h=side[0],v=side[1];Position(w,work);
                    var before=Bounds(w);double dx=-h*20,dy=-v*17;
                    Drag(w,h,v,dx,dy);
                    var b=Bounds(w);
                    Check(Near(b.Left,before.Left+(h<0?dx:0)) && Near(b.Right,before.Right+(h>0?dx:0)) &&
                        Near(b.Top,before.Top+(v<0?dy:0)) && Near(b.Bottom,before.Bottom+(v>0?dy:0)),
                        "drag moves only the active edge(s) "+h+","+v+" : "+b);
                    var saved=new Store(folder);
                    Check(Near(saved.PlannerWidth,b.Width) && Near(saved.PlannerHeight,b.Height),"drag size committed on release");
                    Clear(w,pet);
                }
                var previous=Bounds(w);w.Close();pet.ShowPlanner();w=pet.OwnedWindows.OfType<Planner>().Single();Pump();
                Check(Near(w.ActualWidth,previous.Width) && Near(w.ActualHeight,previous.Height),"reopened planner restores last dragged size");
                Position(w,work);
                pet.Left=w.Left+w.ActualWidth+24;pet.Top=w.Top+w.ActualHeight*.4;Pump();
                Drag(w,1,0,75,0);Clear(w,pet);
                Position(w,work);w.Width=310;w.Height=282;w.UpdateLayout();Pump(80);
                Pixel p;GripAt(w,-1,0,out p);Move(p);
                Press(w);Release();Press(w);Release();Pump(110);
                Check(w.ActualHeight>300,"actual border double-click fits the long task list");
                Clear(w,pet);
                var fitted=new Store(folder);
                Check(Near(fitted.PlannerWidth,w.Width) && Near(fitted.PlannerHeight,w.Height),"fitted size survives disk reload");
                w.Close();pet.Left=work.Right-pet.Width-1;pet.Top=work.Top+work.Height*.5;Pump();
                SizeMenu(pet,folder);
                pet.Close();pet=new DesktopPet(new Store(folder));pet.Show();Pump();
                Check(Near(pet.ActualWidth,120),"pet is 120 DIPs after reopening saved XML");
                pet.Close();app.Shutdown();return 0;
            }
            catch(Exception error){Console.Error.WriteLine(error);return 1;}
            finally
            {
                if(held)mouse_event(releaseFlag,0,0,0,UIntPtr.Zero);
                Pixel cursor;
                if(moved && GetCursorPos(out cursor) && Math.Abs(cursor.X-last.X)<=2 && Math.Abs(cursor.Y-last.Y)<=2)
                    SetCursorPos(original.X,original.Y);
            }
        }
    }
}
