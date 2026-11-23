using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Xiaohei
{
    internal static partial class PetArt
    {
        // One timeline drives both the disappearing tail and the three kittens.
        internal sealed class TailLitter : Canvas
        {
            internal const int DurationMs = 7350;
            private const double RightRootX = 449, RightTipX = 621;
            private const double RightPieceWidth = (RightTipX-RightRootX)/2;
            private static readonly Point[] Origins = { new Point(265,418), new Point(RightTipX,417), new Point(RightTipX-RightPieceWidth,417) };
            private static readonly Point[] Landings = { new Point(373,85), new Point(225,453), new Point(522,448) };
            private static readonly Brush WarmLine = ColorBrush(91,58,42);
            private static readonly Brush BlueMouth = ColorBrush(134,175,177);
            private readonly ScaleTransform leftTipLength = new ScaleTransform(1,1,320,418);
            private readonly ScaleTransform rightTailLength = new ScaleTransform(1,1,RightRootX,418);
            private readonly TranslateTransform gaze = new TranslateTransform();
            private readonly Canvas[] kittens = new Canvas[3], faces = new Canvas[3];
            private readonly MatrixTransform[] positions = new MatrixTransform[3];
            private readonly Canvas mouth = new Canvas();
            private readonly bool reducedMotion;
            private static readonly DependencyProperty TimeProperty = DependencyProperty.Register("Time", typeof(double),
                typeof(TailLitter), new PropertyMetadata(0.0, delegate(DependencyObject d, DependencyPropertyChangedEventArgs e)
                { ((TailLitter)d).DrawAt((double)e.NewValue); }));

            internal TailLitter(Canvas leftTip, Canvas rightTail, Canvas eyes, bool animate)
            {
                IsHitTestVisible = false;
                reducedMotion = animate && !SystemParameters.ClientAreaAnimation;
                leftTip.RenderTransform = leftTipLength;
                rightTail.RenderTransform = rightTailLength;
                eyes.Children[2].RenderTransform = eyes.Children[3].RenderTransform = gaze;
                Children.Add(mouth);
                Shape(mouth,"M 354,332 Q 359,314 365,309 Q 370,306 378,320 L 382,331 Q 372,336 355,335 Z",BlueMouth,5,WarmLine);
                for (int i = 0; i < kittens.Length; i++) AddKitten(i);
                DrawAt(animate ? 0 : 4);
                if (!animate) return;
                Loaded += delegate
                {
                    var duration = TimeSpan.FromMilliseconds(DurationMs);
                    var play = new DoubleAnimation(0, duration.TotalSeconds, duration);
                    Timeline.SetDesiredFrameRate(play, reducedMotion ? 8 : 30);
                    BeginAnimation(TimeProperty,play);
                };
                Unloaded += delegate { BeginAnimation(TimeProperty,null); };
            }

            private void AddKitten(int index)
            {
                var kitten = kittens[index] = new Canvas();
                positions[index] = new MatrixTransform();
                kitten.RenderTransform = positions[index];
                Children.Add(kitten);
                Shape(kitten,"M -54,8 C -54,-4 -48,-15 -39,-23 Q -40,-39 -31,-42 Q -23,-44 -14,-33 Q 0,-37 15,-33 Q 26,-45 34,-42 Q 43,-41 40,-24 C 52,-14 57,0 54,15 Q 53,33 32,37 Q 9,42 -23,39 C -44,37 -54,29 -54,8 Z",Ink,7,WarmLine);
                var face = faces[index] = new Canvas();
                kitten.Children.Add(face);
                if (index == 0)
                {
                    Shape(face,"M -42,6 C -42,-13 -26,-22 -15,-17 C -2,-12 -2,13 -13,23 C -25,32 -42,26 -42,6 Z M 5,5 C 5,-14 20,-22 32,-17 C 46,-12 46,12 35,23 C 20,32 5,25 5,5 Z",Ivory,4,WarmLine);
                    Shape(face,"M -32,7 C -33,-5 -25,-13 -18,-10 C -8,-6 -10,12 -17,17 C -25,23 -32,18 -32,7 Z M 15,6 C 15,-6 23,-14 30,-10 C 39,-5 38,10 32,17 C 23,22 15,17 15,6 Z",Ink,0);
                }
                else if (index == 1)
                    Shape(face,"M -47,5 Q -32,0 -14,6 M 6,6 Q 23,-2 46,1",null,7,WarmLine);
                else
                {
                    Shape(face,"M -47,-2 Q -31,-2 -13,5 M 4,5 Q 23,-5 47,-5",null,7,WarmLine);
                    Shape(face,"M -14,12 L 3,12 L -2,30 Z",BlueMouth,4,WarmLine);
                }
            }

            private static double Unit(double t) { return Math.Max(0,Math.Min(t,1)); }
            private static double Smooth(double t) { t = Unit(t); return t*t*(3-2*t); }

            internal void DrawAt(double seconds)
            {
                // Rewind the same paths after the group portrait so every kitten rejoins its own piece.
                double time = seconds <= 4.9 ? seconds : Math.Max(0,(7.3-seconds)*1.5);
                if (reducedMotion)
                    time = time < .5 ? 0 : time < 1.53 ? 1.49 : time < 2.56 ? 2.52 : 3.6;
                double rightPieces = 0;
                for (int i = 0; i < kittens.Length; i++)
                {
                    double progress = Unit((time - (.50 + 1.03*i)) / .91);
                    double separated = Smooth(progress / .48);
                    if (i == 0) leftTipLength.ScaleX = 1 - separated;
                    else rightPieces += separated;
                    kittens[i].Visibility = progress == 0 ? Visibility.Hidden : Visibility.Visible;
                    if (progress == 0) continue;
                    // Let the kitten take shape on its own segment before it jumps away.
                    double t = Smooth((progress-.30)/.70);
                    Point origin = Origins[i];
                    var end = Landings[i];
                    // Keep the growing kitten against the retreating tip until it jumps.
                    double birthX = i == 0 ? origin.X : origin.X - (RightPieceWidth - 51)*separated;
                    double arc = Math.Sin(Math.PI*t);
                    double x = birthX + (end.X-birthX)*t - (i == 0 ? 92 : 0)*arc;
                    double y = origin.Y + (end.Y-origin.Y)*t - (i == 0 ? 82 : i == 1 ? 110 : 71)*arc;
                    // The top kitten settles between the ears; leave enough room
                    // above the forehead without scaling or translating the parent cat.
                    double size = (i == 0 ? .72 : .86)*separated;
                    double squash = .15*Math.Sin(Math.PI * Unit((progress-.81)/.19));
                    var transform = Matrix.Identity;
                    transform.Scale(size*(1+squash),size*(1-squash));
                    transform.Rotate((i == 1 ? -20 : 13)*arc);
                    transform.Translate(x,y);
                    positions[i].Matrix = transform;
                    faces[i].Opacity = Smooth((progress-.08)/.27);
                }
                rightTailLength.ScaleX = 1 - rightPieces * .5;
                double look = Smooth(time-.58);
                gaze.X = 3*look;
                gaze.Y = -15*look;
                mouth.Opacity = look;
            }
        }
    }
}
