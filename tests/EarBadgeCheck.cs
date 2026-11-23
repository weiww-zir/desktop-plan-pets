using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Xiaohei
{
    internal static class EarBadgeCheck
    {
        const int density = 3;
        static readonly MethodInfo frameCallback=typeof(PetActor).GetMethod("FollowEar",BindingFlags.NonPublic|BindingFlags.Instance);
        static T Descendant<T>(DependencyObject item) where T:DependencyObject
        {
            if(item is T)return (T)item;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(item);i++)
            {
                var found=Descendant<T>(VisualTreeHelper.GetChild(item,i));
                if(found!=null)return found;
            }
            return null;
        }
        static void Require(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
        }

        static void Layout(PetActor cat, Size size)
        {
            cat.Measure(size);
            cat.Arrange(new Rect(size));
            cat.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(delegate {}));
        }

        static RenderTargetBitmap Render(PetActor cat)
        {
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(cat.ActualWidth*density),
                (int)Math.Ceiling(cat.ActualHeight*density),96*density,96*density,PixelFormats.Pbgra32);
            bitmap.Render(cat);
            return bitmap;
        }

        static byte[] Bytes(BitmapSource bitmap)
        {
            var data=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];
            bitmap.CopyPixels(data,bitmap.PixelWidth*4,0);
            return data;
        }

        static bool IsEar(byte[] picture,int n)
        {
            return picture[n+3]>245 && Math.Abs(picture[n]-150)<6 &&
                Math.Abs(picture[n+1]-193)<6 && Math.Abs(picture[n+2]-171)<6;
        }

        static bool IsRed(byte[] picture,int n)
        {
            return picture[n+3]>245 && picture[n+2]>180 && picture[n+1]<115 && picture[n]<120;
        }

        static double CheckEar(PetActor cat,Border badge,RenderTargetBitmap combined,string scenario)
        {
            var composite=Bytes(combined);
            badge.Opacity=0;
            var underlying=Bytes(Render(cat));
            badge.Opacity=1;
            int w=combined.PixelWidth,h=combined.PixelHeight;
            int earMin=w,earMax=-1, redMin=w,redMax=-1;
            var left=Enumerable.Repeat(w,h).ToArray();
            var right=Enumerable.Repeat(-1,h).ToArray();
            for(int y=0;y<h;y++)
                for(int x=0;x<w;x++)
                {
                    int n=(y*w+x)*4;
                    if(IsEar(underlying,n)) {earMin=Math.Min(earMin,x);earMax=Math.Max(earMax,x);}
                    if(!IsRed(composite,n)) continue;
                    left[y]=Math.Min(left[y],x); right[y]=Math.Max(right[y],x);
                    redMin=Math.Min(redMin,x);redMax=Math.Max(redMax,x);
                }
            Require(earMax>=earMin && redMax>=redMin,"Missing ear or badge pixels: "+scenario);
            int rightEarStart=(earMin+earMax)/2;
            Require((redMin+redMax)*0.5>=rightEarStart,"Badge is on the wrong side of the head: "+scenario);
            int greenTop=h,greenBottom=-1,greenLeft=w;
            for(int y=0;y<h;y++)for(int x=rightEarStart;x<=earMax;x++)
                if(IsEar(underlying,(y*w+x)*4))
                { greenTop=Math.Min(greenTop,y); greenBottom=Math.Max(greenBottom,y); greenLeft=Math.Min(greenLeft,x); }
            double nearest=double.PositiveInfinity,nearestGreen=double.PositiveInfinity;
            var occupiedRows=Enumerable.Range(0,h).Where(y=>right[y]>=0).ToArray();
            // Each horizontal red scanline is a span; a white digit inside it does not
            // increase the visual distance between the badge shape and the ear.
            // The Meow pose has a drooping black lobe above its smaller green inner ear.
            // Measure the painted outer silhouette in the local ear area as well as the green patch.
            for(int y=Math.Max(0,greenTop-w/10);y<=Math.Min(h-1,greenBottom+w/32);y++)
                for(int x=Math.Max(rightEarStart,greenLeft-w/32);x<=Math.Min(w-1,earMax+w/16);x++)
                {
                    int n=(y*w+x)*4;
                    if(underlying[n+3]<240)continue;
                    bool green=IsEar(underlying,n);
                    foreach(int row in occupiedRows)
                    {
                        int dx=Math.Max(0,Math.Max(left[row]-x,x-right[row]));
                        int dy=y-row;
                        double d=dx*dx+dy*dy;
                        nearest=Math.Min(nearest,d);
                        if(green) nearestGreen=Math.Min(nearestGreen,d);
                    }
                }
            double dip=Math.Sqrt(nearest)/density;
            Require(dip<=Math.Max(3,cat.ActualWidth*.025) && Math.Sqrt(nearestGreen)/density<=cat.ActualWidth*.14,
                String.Format(CultureInfo.InvariantCulture,"Badge is {0:F2} DIPs from right ear: {1}",dip,scenario));
            return dip;
        }

        static void Save(RenderTargetBitmap badgeCat,string path)
        {
            var png=new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(badgeCat));
            using(var output=File.Create(path))png.Save(output);
        }

        [STAThread] static int Main(string[] args)
        {
            try
            {
                new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
                var setPose=typeof(PetActor).GetMethod("SetPose",BindingFlags.NonPublic|BindingFlags.Instance);
                foreach(int width in new[]{64,92,192})
                {
                    var cat=new PetActor();
                    var size=new Size(width,width*148.0/152);
                    Layout(cat,size);
                    var badge=cat.Children.OfType<Border>().Single(x=>AutomationProperties.GetAutomationId(x)=="PendingBadge");
                    var label=(TextBlock)badge.Child;
                    foreach(PetPose pose in new[]{PetPose.Rest,PetPose.Tilt,PetPose.Nap,PetPose.Stretch,PetPose.Meow,PetPose.Wave,PetPose.Hop,PetPose.TailKittens})
                    {
                        setPose.Invoke(cat,new object[]{pose});
                        foreach(int count in new[]{1,108})
                        {
                            cat.ShowPendingCount(count);
                            Layout(cat,size);
                            bool moving=pose==PetPose.Hop || pose==PetPose.Tilt || pose==PetPose.TailKittens;
                            if(pose==PetPose.TailKittens && count>99)
                                Descendant<PetArt.TailLitter>(cat).DrawAt(4);
                            // A detached visual gets no screen CompositionTarget.Rendering event.
                            // Run the actual production frame handler at the sampled animation time.
                            if(moving) frameCallback.Invoke(cat,new object[]{null,EventArgs.Empty});
                            var rect=badge.TransformToAncestor(cat).TransformBounds(new Rect(badge.RenderSize));
                            var area=new Rect(size); area.Inflate(.02,.02);
                            string scenario=width+" "+pose+" "+label.Text;
                            Require(area.Contains(rect) && !badge.IsHitTestVisible,"Badge escapes the pet window or intercepts clicks: "+scenario);
                            Require(label.Text==(count==1?"1":"99+") && label.ActualWidth+1>=label.DesiredSize.Width &&
                                badge.Visibility==Visibility.Visible,"Count is clipped or missing: "+scenario);
                            var image=Render(cat);
                            double gap=CheckEar(cat,badge,image,scenario);
                            Console.WriteLine("PASS "+scenario+"; ear gap "+gap.ToString("F2",CultureInfo.InvariantCulture)+" DIP");
                            if(width==92 && pose==PetPose.Rest && count==1)
                                Save(image,Path.Combine(args[0],"EarBadgePreview.png"));
                        }
                    }
                    cat.ShowPendingCount(0);
                    Layout(cat,size);
                    var clean=Bytes(Render(cat));
                    Require(badge.Visibility==Visibility.Collapsed &&
                        !Enumerable.Range(0,clean.Length/4).Any(i=>IsRed(clean,i*4)),"Empty badge still present");
                    Console.WriteLine("PASS "+width+" empty badge collapsed");
                }
                return 0;
            }
            catch(Exception e){Console.Error.WriteLine(e);return 1;}
        }
    }
}
