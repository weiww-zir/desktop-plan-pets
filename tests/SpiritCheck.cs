using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Xiaohei
{
    internal static class SpiritCheck
    {
        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                if (args[0] == "preview") Frames(args[1]); else PlayMenu(args[1]);
                return 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        }

        static void Frames(string dir)
        {
            Directory.CreateDirectory(dir);
            var anchor = new FrameworkElement();
            var view = (Viewbox)PetArt.Draw(false,PetPose.SpiritOrbit,anchor);
            var scene = (PetArt.SpiritOrbit)((Canvas)view.Child).Children[0];
            var box = new Border { Background = new SolidColorBrush(Color.FromRgb(68,92,73)), Child=view };
            int size = 236;
            box.Measure(new Size(size,size)); box.Arrange(new Rect(0,0,size,size));
            box.UpdateLayout();
            var ear = anchor.TranslatePoint(new Point(),view);
            if (ear.X < 185 || ear.X > 230 || ear.Y < 85 || ear.Y > 144) throw new Exception("Ear anchor: " + ear);
            double previousPawFlightX = double.PositiveInfinity;
            for (int i=0;i<90;i++)
            {
                scene.DrawAt(i*.08);
                var v = scene.Children.OfType<Canvas>().Skip(1).ToArray();
                if (v.Length != 6 || !v.Any(x => Panel.GetZIndex(x)<0) || !v.Any(x => Panel.GetZIndex(x)>0))
                    throw new Exception("Incorrect front/back layering");
                var butterfly = v[5].RenderTransform.Transform(new Point());
                if (i == 0 && !new Rect(167,394,12,12).Contains(butterfly))
                    throw new Exception("Butterfly must start below and to the left of the paw");
                if (Panel.GetZIndex(v[5]) <= 0 || butterfly.X >= previousPawFlightX)
                    throw new Exception("Butterfly must fly leftwards in front of the cat at frame "+i);
                if (i == 89 && !new Rect(35,280,10,19).Contains(butterfly))
                    throw new Exception("Butterfly did not reach the cat's left side");
                previousPawFlightX = butterfly.X;
                foreach (var sprite in v)
                {
                    var bounds = sprite.TransformToAncestor(scene).TransformBounds(VisualTreeHelper.GetDescendantBounds(sprite));
                    if (!new Rect(0,0,480,455).Contains(bounds)) throw new Exception("Spirit clipped: "+bounds);
                }
                var frame = new RenderTargetBitmap(size,size,96,96,PixelFormats.Pbgra32);
                frame.Render(box);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(frame));
                using (var f = File.Create(Path.Combine(dir,i.ToString("D3")+".png"))) png.Save(f);
            }
            scene.DrawAt(2.8);
            box.Background=Brushes.White;
            box.Measure(new Size(92,92)); box.Arrange(new Rect(0,0,92,92)); box.UpdateLayout();
            var small=new RenderTargetBitmap(92,92,96,96,PixelFormats.Pbgra32);
            small.Render(box);
            var white=new PngBitmapEncoder(); white.Frames.Add(BitmapFrame.Create(small));
            using(var f=File.Create(Path.Combine(dir,"white-small.png"))) white.Save(f);
            Console.WriteLine("PASS 90 frames, 6 in-bounds spirits, front/back layering, right-ear badge anchor");
        }

        static void Until(Func<bool> done,int timeout)
        {
            var elapsed=Stopwatch.StartNew();
            while(!done()) {
                if (elapsed.ElapsedMilliseconds>timeout) throw new TimeoutException();
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(delegate {}));
                Thread.Sleep(15);
            }
        }

        static void PlayMenu(string dir)
        {
            var pet = new DesktopPet(new Store(dir));
            pet.ShowActivated=false; pet.Show(); pet.Left=-9000; pet.Top=-9000;
            var actor = (PetActor)pet.Content;
            actor.ShowPendingCount(3);
            var item = pet.ContextMenu.Items.OfType<MenuItem>().Single(x=>(string)x.Header=="白色小精灵围绕");
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            if (actor.Pose!=PetPose.SpiritOrbit) throw new Exception("Menu did not start requested action");
            var timer=(DispatcherTimer)typeof(PetActor).GetField("tailTimer",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(actor);
            timer.Interval=TimeSpan.FromMilliseconds(150);
            var elapsed=Stopwatch.StartNew();
            Until(delegate {return elapsed.ElapsedMilliseconds>450;},1500);
            if(actor.Pose!=PetPose.SpiritOrbit) throw new Exception("Automatic action cut the spirits short");
            timer.Interval=TimeSpan.FromSeconds(30);
            Until(delegate {return actor.Pose==PetPose.TailKittens;},7800);
            Console.WriteLine("PASS menu and queued 30s tail action; spirit animation lasted "+elapsed.Elapsed.TotalSeconds.ToString("F2")+" seconds");
            pet.Close();
        }
    }
}
