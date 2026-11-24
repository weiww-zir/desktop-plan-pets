using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Xiaohei
{
    internal static partial class PetArt
    {
        internal sealed class SpiritOrbit : Canvas
        {
            internal const int DurationMs = 7200;
            private const double Period = DurationMs / 1000.0;
            private const int PawSpirit = 5;
            private static readonly Brush EyeWhite = ColorBrush(249,246,215);
            private static readonly Brush Pearl = ColorBrush(255,255,246);
            private static readonly Brush Rim = ColorBrush(108,126,104);
            private static readonly double[] Phases = { 2.85, 3.80, 4.48, 5.70, .56, 1.62 };
            private static readonly double[] Sizes = { 1.15, .64, 1.03, .79, .66, .97 };
            private static readonly PathGeometry[] Routes = {
                Route("M 65,283 C 86,261 119,202 145,216 C 169,242 115,253 129,210 C 145,156 220,186 243,153 C 267,112 213,113 216,141 C 220,166 278,147 318,115"),
                Route("M 344,108 C 298,56 276,79 252,110 C 217,153 164,132 154,115 C 139,90 166,74 181,94 C 199,124 152,150 106,161 C 66,171 52,194 67,214"),
                Route("M 409,268 C 427,231 424,179 387,173 C 354,168 355,208 383,199 C 416,187 379,150 353,141 C 299,123 277,147 268,179 C 260,205 274,211 281,207"),
                Route("M 165,95 C 189,60 208,62 238,82 C 280,113 313,100 340,86 C 375,68 393,95 368,118 C 337,152 319,147 309,167 C 297,199 332,205 356,183"),
                Route("M 59,179 C 44,217 73,224 108,203 C 146,181 176,196 181,239 C 184,270 218,299 253,286 C 292,274 302,246 289,238 C 271,223 257,247 274,262"),
                Route("M 173,400 C 162,390 150,394 140,386 C 124,370 105,380 88,365 C 72,351 61,352 53,339 C 43,323 47,303 38,289")
            };
            private readonly Canvas[] spirits = new Canvas[Phases.Length];
            private readonly MatrixTransform[] flights = new MatrixTransform[Phases.Length];
            private readonly ScaleTransform[] wings = new ScaleTransform[Phases.Length];
            private readonly TranslateTransform gaze = new TranslateTransform();
            private static readonly DependencyProperty TimeProperty = DependencyProperty.Register("Time", typeof(double),
                typeof(SpiritOrbit),new PropertyMetadata(0.0,delegate(DependencyObject d, DependencyPropertyChangedEventArgs e)
                { ((SpiritOrbit)d).DrawAt((double)e.NewValue); }));

            private static PathGeometry Route(string path)
            {
                var curve = PathGeometry.CreateFromGeometry(Geometry.Parse(path));
                curve.Freeze();
                return curve;
            }

            internal SpiritOrbit(bool motion, FrameworkElement ear)
            {
                Width = 480;
                Height = 455;
                var cat = new Canvas { RenderTransform = new MatrixTransform(.47,0,0,.47,12,89) };
                Children.Add(cat);
                DrawCat(cat);
                MarkEar(cat,ear,903,304);
                for (int i=0;i<spirits.Length;i++) DrawSpirit(i);
                DrawAt(1.2);
                if (!motion) return;
                Loaded += delegate
                {
                    var time = new DoubleAnimation(0,Period,TimeSpan.FromSeconds(Period));
                    Timeline.SetDesiredFrameRate(time,30);
                    BeginAnimation(TimeProperty,time);
                };
                Unloaded += delegate { BeginAnimation(TimeProperty,null); };
            }

            private void DrawCat(Canvas cat)
            {
                Shape(cat,"M 21,611 C 28,509 76,423 170,395 C 258,367 378,388 445,445 L 526,583 Q 481,644 379,655 C 254,673 163,699 68,674 Q 18,668 21,611 Z",Ink,11);
                Shape(cat,"M 48,635 C 89,627 164,624 225,598 Q 301,572 356,601 M 221,505 C 266,505 309,544 313,578",null,8);
                Shape(cat,"M 337,427 C 353,325 400,231 448,178 Q 495,122 523,143 Q 571,167 593,238 C 661,235 722,260 774,305 C 821,287 890,283 919,311 C 948,338 931,440 892,526 C 886,603 815,657 731,665 C 637,677 519,656 453,628 C 368,596 320,538 337,427 Z",Ink,11);
                Shape(cat,"M 362,358 Q 402,237 467,177 Q 493,151 511,150 Q 529,182 513,250 Q 427,284 362,358 Z",Ear,0);
                Shape(cat,"M 850,507 Q 855,424 837,387 Q 873,351 914,338 Q 927,399 885,495 L 869,520 Z",Ear,0);
                Shape(cat,"M 346,396 Q 389,301 505,254 Q 550,236 594,239 M 774,305 Q 817,334 838,385",null,9);
                DrawEye(cat,476,400,119,140,26);
                DrawEye(cat,723,483,99,129,21);
                Shape(cat,"M 352,620 Q 363,577 393,556 Q 425,537 458,565 Q 477,579 487,608 C 470,625 440,632 410,636 Q 374,641 352,630 Z",Ink,8);
                Shape(cat,"M 489,632 Q 503,591 535,584 Q 574,574 600,614 Q 611,630 611,644 Q 565,656 518,649 Q 497,645 489,641 Z",Ink,8);
            }

            private void DrawEye(Canvas cat,double x,double y,double rx,double ry,double angle)
            {
                var eye = new Canvas { RenderTransform = new RotateTransform(angle) };
                Canvas.SetLeft(eye,x); Canvas.SetTop(eye,y);
                cat.Children.Add(eye);
                var sclera = new EllipseGeometry(new Point(),rx,ry);
                sclera.Freeze();
                eye.Children.Add(new Path { Data=sclera,Fill=EyeWhite,Stroke=Outline,StrokeThickness=5 });
                var aperture = new Canvas { Clip=sclera };
                aperture.Children.Add(new Path { Data=new EllipseGeometry(new Point(-9,-16),rx*.75,ry*.76),
                    Fill=Ink,RenderTransform=gaze });
                eye.Children.Add(aperture);
            }

            private void DrawSpirit(int i)
            {
                var sprite = spirits[i] = new Canvas();
                var glow = new RadialGradientBrush();
                glow.GradientStops.Add(new GradientStop(Color.FromArgb(205,255,255,238),0));
                glow.GradientStops.Add(new GradientStop(Color.FromArgb(110,245,255,218),.46));
                glow.GradientStops.Add(new GradientStop(Color.FromArgb(0,234,252,196),1));
                glow.Freeze();
                var halo = new Ellipse { Width=51,Height=47,Fill=glow };
                Canvas.SetLeft(halo,-25.5); Canvas.SetTop(halo,-23.5); sprite.Children.Add(halo);
                var silhouette = new Canvas();
                wings[i] = new ScaleTransform();
                silhouette.RenderTransform = wings[i];
                Shape(silhouette,"M 0,8 C -8,3 -17,-7 -16,-14 Q -14,-21 -8,-16 Q -1,-9 1,-2 Q 6,-14 12,-17 Q 18,-19 17,-11 C 16,-5 7,5 2,9 Q 1,10 0,8 Z",Pearl,3.0,Rim);
                sprite.Children.Add(silhouette);
                var mote = new Ellipse { Width=5.5,Height=5.5,Fill=Pearl,Stroke=Rim,StrokeThickness=.8 };
                Canvas.SetLeft(mote,-17); Canvas.SetTop(mote,14); sprite.Children.Add(mote);
                flights[i] = new MatrixTransform();
                sprite.RenderTransform = flights[i];
                Children.Add(sprite);
            }

            internal void DrawAt(double seconds)
            {
                double progress = Math.Max(0,Math.Min(1,seconds / Period));
                for (int i=0;i<spirits.Length;i++)
                {
                    // Each butterfly glides and lingers at different points along its own curve.
                    double phase = Phases[i];
                    double tempoVariation = i == PawSpirit ? .38 : .87;
                    double travel = progress + tempoVariation*(Math.Sin(2*Math.PI*progress+phase)-Math.Sin(phase))/(2*Math.PI);
                    Point place, tangent;
                    Routes[i].GetPointAtFractionLength(Math.Max(0,Math.Min(1,travel)),out place,out tangent);
                    double flutter = seconds*(7.4 + (i*1.7)%4.9) + phase + .58*Math.Sin(seconds*2.1+phase);
                    double x = i == PawSpirit ? place.X : place.X + 2.6*Math.Sin(seconds*2.2+phase);
                    double y = place.Y + 4*Math.Cos(seconds*(1.8+.12*i)+phase);
                    double depth = i == PawSpirit ? .65 : i == 0 ? .88 : i == 1 ? -.72 :
                        Math.Sin(travel*5.1+phase);
                    double scale = Sizes[i]*(.96+.14*depth);
                    var m = Matrix.Identity;
                    m.Scale(scale,scale);
                    m.Rotate(24*tangent.X + 8*Math.Sin(flutter*.46));
                    m.Translate(x,y);
                    flights[i].Matrix = m;
                    wings[i].ScaleX = .80 + .20*Math.Cos(flutter);
                    double arrival = (seconds-.19*(i%3))/.40;
                    double departure = (Period-seconds-.12*((i+1)%4))/.48;
                    double fade = Math.Max(0,Math.Min(1,Math.Min(arrival,departure)));
                    spirits[i].Opacity = fade*(.83+.17*depth);
                    SetZIndex(spirits[i],depth >= 0 ? 1 : -1);
                    if (i == 0)
                    {
                        gaze.X = (x-258)*.065;
                        gaze.Y = (y-297)*.060;
                    }
                }
            }
        }
    }
}
