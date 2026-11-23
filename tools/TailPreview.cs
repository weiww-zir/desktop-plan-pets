using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShapePath = System.Windows.Shapes.Path;

namespace Xiaohei
{
    internal static class TailPreview
    {
        private static ShapePath Body(Viewbox view)
        {
            var figure=(Canvas)((Canvas)view.Child).Children[0];
            var head=(Canvas)figure.Children[2];
            return head.Children.OfType<ShapePath>().First();
        }

        private static void CheckFullCycle(Viewbox view,PetArt.TailLitter litter)
        {
            var rest=(Viewbox)PetArt.Draw(false,PetPose.Rest);
            var basis=new Size(480,455);
            rest.Measure(basis);
            rest.Arrange(new Rect(basis));
            rest.UpdateLayout();
            var normal=Body(rest);
            var expected=normal.TransformToAncestor(rest.Child).TransformBounds(normal.Data.Bounds);
            var main=Body(view);
            var area=new Rect(basis);
            // At 5 ms intervals this covers the complete forward/return path and the
            // overshoot from each kitten's rotation and squash, including the tail tips.
            for(int ms=0;ms<=PetArt.TailLitter.DurationMs;ms+=5)
            {
                litter.DrawAt(ms/1000.0);
                var actual=main.TransformToAncestor(view.Child).TransformBounds(main.Data.Bounds);
                if(actual!=expected)
                    throw new Exception("Parent silhouette moved or resized at "+ms+"ms: "+actual+" vs "+expected);
                foreach(Canvas kitten in litter.Children.OfType<Canvas>().Skip(1))
                {
                    if(kitten.Visibility!=Visibility.Visible) continue;
                    var bounds=kitten.TransformToAncestor(view.Child).TransformBounds(VisualTreeHelper.GetDescendantBounds(kitten));
                    if(bounds.IsEmpty || !area.Contains(bounds))
                        throw new Exception("Kitten exceeds the existing window at "+ms+"ms: "+bounds);
                }
            }
            Console.WriteLine("PASS 1,471 samples: main silhouette matches resting bounds; all painted kittens inside existing frame");
        }

        private static byte[] PixelData(FrameworkElement view,int width,int height)
        {
            var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
            bitmap.Render(view);
            var bytes=new byte[width*height*4];
            bitmap.CopyPixels(bytes,width*4,0);
            return bytes;
        }
        private static void SetFrameDelays(byte[] gif, int block)
        {
            int frames = 0;
            while (gif[block] != 0x3b)
            {
                if (gif[block] == 0x21)
                {
                    if (gif[block+1] == 0xf9)
                    {
                        gif[block+4] = 6;
                        gif[block+5] = 0;
                        frames++;
                    }
                    block += 2;
                }
                else if (gif[block] == 0x2c)
                {
                    int flags = gif[block+9];
                    block += 10;
                    if ((flags & 128) != 0) block += 3 * (1 << ((flags & 7)+1));
                    block++;
                }
                else throw new InvalidDataException("Unexpected GIF block");
                while (gif[block] > 0) block += 1+gif[block];
                block++;
            }
            if (frames != 124) throw new InvalidDataException("Missing frame timing");
        }

        [STAThread]
        static int Main(string[] args)
        {
            try { Run(args); return 0; }
            catch (Exception error) { Console.Error.WriteLine(error.GetType().Name+" "+error.Message); return 1; }
        }

        static void Run(string[] args)
        {
            var view = (Viewbox)PetArt.Draw(false,PetPose.TailKittens);
            var drawing = (Canvas)((Canvas)view.Child).Children[0];
            var litter = drawing.Children.OfType<PetArt.TailLitter>().Single();
            var rightTail = (ScaleTransform)drawing.Children[0].RenderTransform;
            var leftTip = (ScaleTransform)drawing.Children[1].RenderTransform;
            var panel = new Border { Child = view, Padding = new Thickness(6),
                Background = new SolidColorBrush(Color.FromRgb(245,248,241)) };
            int width = 202, height = 194;
            panel.Measure(new Size(width,height));
            panel.Arrange(new Rect(0,0,width,height));
            panel.UpdateLayout();
            litter.DrawAt(0);
            var startPixels=PixelData(panel,width,height);
            CheckFullCycle(view,litter);
            if (!startPixels.SequenceEqual(PixelData(panel,width,height)))
                throw new Exception("The final animation frame did not restore its original pixels");
            Console.WriteLine("PASS pixel-identical first and final rendered frames");
            var stamps = new[] { 0.0,1.46,2.5,3.58,5.85,6.5,7.35 };
            var counts = new[] { 0,1,2,3,2,1,0 };
            for (int i = 0; i < stamps.Length; i++)
            {
                litter.DrawAt(stamps[i]);
                int visible = litter.Children.Cast<UIElement>().Skip(1).Count(x => x.Visibility == Visibility.Visible);
                if (visible != counts[i]) throw new Exception("Kitten count at " + stamps[i] + " was " + visible);
                double expectedRight = 1 - Math.Max(0,counts[i]-1)*.5;
                if (Math.Abs(leftTip.ScaleX - (counts[i] == 0 ? 1 : 0)) > .0001 ||
                    Math.Abs(rightTail.ScaleX - expectedRight) > .0001) throw new Exception("Tail length at " + stamps[i]);
            }
            var expectedX = new[] {265.0,621.0,535.0};
            for (int i = 0; i < expectedX.Length; i++)
            {
                litter.DrawAt(.5+1.03*i-.001);
                if (i > 0 && Math.Abs(rightTail.Transform(new Point(621,417)).X - expectedX[i]) > .01)
                    throw new Exception("Kitten must start at the remaining outer tip");
                litter.DrawAt(.51+1.03*i);
                var origin = litter.Children[i+1].RenderTransform.Value;
                if (Math.Abs(origin.OffsetX - expectedX[i]) > 1 || Math.Abs(origin.OffsetY - 418) > 2)
                    throw new Exception("Wrong launch point for kitten " + i);
            }
            litter.DrawAt(.71);
            if (leftTip.ScaleX <= 0 || leftTip.ScaleX >= 1 || rightTail.ScaleX != 1)
                throw new Exception("The first kitten must use only the left tip");
            if (litter.Children[1].RenderTransform.Value.OffsetX != 265 || litter.Children[1].RenderTransform.Value.OffsetY != 418)
                throw new Exception("The first kitten must take shape on the tip before jumping");
            litter.DrawAt(1.74);
            double shrinkingTip = rightTail.Transform(new Point(621,417)).X;
            if (rightTail.Transform(new Point(449,417)).X != 449 || shrinkingTip <= 535 || shrinkingTip >= 621)
                throw new Exception("Right tail must shorten from its far right, with the root fixed");
            Console.WriteLine("PASS left tip first, then both right kittens start at the current outer tip; tail restored");
            var gif = new GifBitmapEncoder();
            for (int frame = 0; frame <= 123; frame++)
            {
                litter.DrawAt(frame*.06);
                panel.UpdateLayout();
                var bmp = new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
                bmp.Render(panel);
                var metadata = new BitmapMetadata("gif");
                metadata.SetQuery("/grctlext/Delay", (ushort)6);
                gif.Frames.Add(BitmapFrame.Create(bmp,null,metadata,null));
                if (frame == 70)
                {
                    var png = new PngBitmapEncoder();
                    png.Frames.Add(BitmapFrame.Create(bmp));
                    using (var file = File.Create(Path.Combine(args[0],"TailPortrait.png"))) png.Save(file);
                }
            }
            using (var data = new MemoryStream())
            {
                gif.Save(data);
                byte[] bytes = data.ToArray();
                int header = 13;
                if ((bytes[10] & 128) != 0) header += 3*(1 << ((bytes[10] & 7)+1));
                SetFrameDelays(bytes,header);
                using (var file = File.Create(Path.Combine(args[0],"尾巴变小猫.gif")))
                {
                    file.Write(bytes,0,header);
                    byte[] loop = {33,255,11,78,69,84,83,67,65,80,69,50,46,48,3,1,0,0,0};
                    file.Write(loop,0,loop.Length);
                    file.Write(bytes,header,bytes.Length-header);
                }
            }
            Console.WriteLine("Rendered 124 frames");
        }
    }
}
