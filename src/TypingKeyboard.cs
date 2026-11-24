using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Xiaohei
{
    internal static partial class PetArt
    {
        internal sealed class TypingKeyboard : Canvas
        {
            private sealed class Keycap
            {
                internal Rect Bounds;
                internal Brush Fill;
                internal Geometry Legend;
            }

            private static readonly Brush Case = ColorBrush(116, 136, 115);
            private static readonly Brush Deck = ColorBrush(164, 177, 155);
            private static readonly Brush Key = ColorBrush(244, 233, 209);
            private static readonly Brush Accent = ColorBrush(216, 225, 196);
            private static readonly Brush Pressed = ColorBrush(255, 189, 93);
            private static readonly Brush KeyInk = ColorBrush(66, 61, 50);
            private static readonly Pen Edge = MakePen(ColorBrush(96, 110, 91), 1);
            private static readonly Pen Rim = MakePen(Outline, 5);
            private static readonly Pen FurEdge = MakePen(Outline, 5);
            private static readonly Typeface Lettering = new Typeface(new FontFamily("Segoe UI, Segoe UI Symbol"),
                FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            private static readonly Dictionary<string, Keycap> Keys = new Dictionary<string, Keycap>(StringComparer.Ordinal);
            private static readonly HashSet<string> ExtendedKeys = ExtendedKeyboard();
            private static readonly DrawingGroup Board = DrawBoard();
            private static readonly DependencyProperty LeftDepth = DependencyProperty.Register("LeftDepth", typeof(double),
                typeof(TypingKeyboard), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
            private static readonly DependencyProperty RightDepth = DependencyProperty.Register("RightDepth", typeof(double),
                typeof(TypingKeyboard), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
            private static readonly DoubleAnimationUsingKeyFrames Stroke = Pulse(false), FastStroke = Pulse(true);

            private readonly bool motion;
            private readonly Border hint;
            private readonly TextBlock letter;
            private Keycap leftTarget, rightTarget;
            private bool left, anonymous;

            internal TypingKeyboard(bool motion)
            {
                this.motion = motion;
                Width = 640;
                Height = 505;
                IsHitTestVisible = false;
                letter = new TextBlock { FontFamily = Lettering.FontFamily, FontSize = 74, FontWeight = FontWeights.Bold,
                    Foreground = KeyInk, TextAlignment = TextAlignment.Center };
                hint = new Border { Visibility = Visibility.Hidden, Background = Key, BorderBrush = FaceLine,
                    BorderThickness = new Thickness(4, 4, 4, 7), CornerRadius = new CornerRadius(17),
                    Padding = new Thickness(6, 0, 6, 2), Child = new Viewbox { Stretch = Stretch.Uniform, Child = letter } };
                Children.Add(hint);
            }

            private static Pen MakePen(Brush color, double size)
            {
                var pen = new Pen(color, size) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round };
                pen.Freeze();
                return pen;
            }

            private static HashSet<string> ExtendedKeyboard()
            {
                var names = new HashSet<string>("Pause PgUp PgDn End Home PrtSc Ins Menu Num ScrLk Num* Num+ Num- Num. Num/".Split(' '), StringComparer.Ordinal);
                for (int i = 1; i <= 24; i++) names.Add("F" + i.ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i <= 9; i++) names.Add("Num" + i.ToString(CultureInfo.InvariantCulture));
                return names;
            }

            private static DrawingGroup DrawBoard()
            {
                var drawing = new DrawingGroup();
                using (var dc = drawing.Open())
                {
                    dc.DrawGeometry(Case, Rim, Geometry.Parse("M 229,424 Q 233,418 241,418 L 514,418 Q 521,418 526,424 L 561,480 Q 568,495 553,499 L 210,499 Q 193,497 200,484 Z"));
                    dc.DrawGeometry(Deck, Edge, Geometry.Parse("M 233,424 Q 236,420 242,420 H 512 Q 518,420 521,425 L 555,483 Q 560,491 549,491 H 214 Q 204,491 209,482 Z"));
                    AddRow(dc, 0, "Esc ` 1 2 3 4 5 6 7 8 9 0 - = Bksp");
                    AddRow(dc, 1, "Tab Q W E R T Y U I O P [ ] \\");
                    AddRow(dc, 2, "Caps A S D F G H J K L ; ' Enter");
                    AddRow(dc, 3, "Shift Z X C V B N M , . / ↑ Del");
                    AddRow(dc, 4, "Fn Ctrl Win Alt Space ← ↓ →");
                }
                drawing.Freeze();
                return drawing;
            }

            private static double KeyWidth(string name)
            {
                switch (name)
                {
                    case "Space": return 7;
                    case "Shift": return 2;
                    case "Enter": case "Bksp": return 1.8;
                    case "Caps": case "Tab": return 1.55;
                    case "Ctrl": case "Alt": return 1.25;
                    default: return 1;
                }
            }

            private static void AddRow(DrawingContext dc, int row, string layout)
            {
                string[] names = layout.Split(' ');
                double units = 0;
                foreach (string name in names) units += KeyWidth(name);
                double x = 236 - 6 * row, step = (280 + 14 * row) / units;
                foreach (string name in names)
                {
                    double width = step * KeyWidth(name);
                    var bounds = new Rect(x, 422 + 13.6 * row, width - 1.7, 11.9);
                    // Use the original Framework overload: key legends are frozen outlines, not monitor-dependent text.
#pragma warning disable 618
                    var label = new FormattedText(name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Lettering,
                        name.Length > 3 ? 7.9 : 9.7, KeyInk);
#pragma warning restore 618
                    var legend = label.BuildGeometry(new Point(bounds.X + (bounds.Width - label.Width) / 2,
                        bounds.Y + (bounds.Height - label.Height) / 2));
                    var key = new Keycap { Bounds = bounds, Fill = name.Length > 1 || "←↑→↓".Contains(name) ? Accent : Key,
                        Legend = legend };
                    Keys.Add(name, key);
                    DrawKey(dc, key, key.Fill, 0);
                    x += width;
                }
            }

            private static void DrawKey(DrawingContext dc, Keycap cap, Brush color, double travel)
            {
                if (travel > 0) dc.PushTransform(new TranslateTransform(0, travel));
                dc.DrawRoundedRectangle(color, Edge, cap.Bounds, 2.1, 2.1);
                dc.DrawGeometry(KeyInk, null, cap.Legend);
                if (travel > 0) dc.Pop();
            }

            internal static bool TryGetKeyBounds(string key, out Rect bounds)
            {
                Keycap cap;
                if (key != null && Keys.TryGetValue(key, out cap)) { bounds = cap.Bounds; return true; }
                bounds = Rect.Empty;
                return false;
            }

            internal void Press(string key, bool animateStroke)
            {
                Keycap cap = null;
                if (key != null && !Keys.TryGetValue(key, out cap))
                {
                    if (ExtendedKeys.Contains(key)) cap = Keys["Fn"];
                    else key = null;
                }
                // An anonymous event must not reveal a previous named reach, even while that paw is returning.
                if ((!anonymous && cap == null) || !motion)
                {
                    SetDepth(LeftDepth, 0);
                    SetDepth(RightDepth, 0);
                    leftTarget = rightTarget = null;
                }
                anonymous = cap == null;
                if (anonymous) cap = Keys[left ? "J" : "F"];
                left = cap.Bounds.X + cap.Bounds.Width / 2 < 377;
                if (left) leftTarget = cap;
                else rightTarget = cap;
                letter.Text = key ?? "";
                hint.Visibility = anonymous ? Visibility.Hidden : Visibility.Visible;
                if (!anonymous)
                {
                    bool wide = key.Length > 2;
                    hint.Width = wide ? 120 : 101;
                    hint.Height = wide ? 64 : 87;
                    SetLeft(hint, wide ? 514 : 533);
                    SetTop(hint, wide ? 314 : 278);
                }
                var property = left ? LeftDepth : RightDepth;
                if (motion)
                {
                    SetValue(property, 0.0);
                    BeginAnimation(property, animateStroke ? Stroke : FastStroke, HandoffBehavior.SnapshotAndReplace);
                }
                else
                    SetValue(property, 1.0);
                InvalidateVisual();
            }

            internal void SetPressDepth(double depth)
            {
                depth = Math.Max(0, Math.Min(1, depth));
                SetDepth(LeftDepth, left ? depth : 0);
                SetDepth(RightDepth, left ? 0 : depth);
                InvalidateVisual();
            }

            private void SetDepth(DependencyProperty property, double depth)
            {
                BeginAnimation(property, null);
                SetValue(property, depth);
            }

            private static DoubleAnimationUsingKeyFrames Pulse(bool compressed)
            {
                var pulse = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
                if (!compressed) pulse.KeyFrames.Add(new LinearDoubleKeyFrame(.16, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                pulse.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(compressed ? 0 : 36))));
                pulse.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(compressed ? 25 : 63))));
                pulse.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(compressed ? 93 : 158)), new KeySpline(.22, 0, .35, 1)));
                pulse.Freeze();
                return pulse;
            }

            protected override void OnRender(DrawingContext dc)
            {
                base.OnRender(dc);
                dc.DrawDrawing(Board);
                double leftDepth = (double)GetValue(LeftDepth), rightDepth = (double)GetValue(RightDepth);
                double latestDepth = left ? leftDepth : rightDepth;
                Keycap latestTarget = left ? leftTarget : rightTarget;
                if (!anonymous && latestTarget != null && latestDepth > .1)
                    DrawKey(dc, latestTarget, Pressed, latestDepth * 2);
                DrawPaw(dc, true, leftTarget, leftDepth);
                DrawPaw(dc, false, rightTarget, rightDepth);
            }

            private static void DrawPaw(DrawingContext dc, bool isLeft, Keycap target, double depth)
            {
                double shoulder = isLeft ? 332 : 415;
                double home = isLeft ? 344 : 402, x = home, y = 418;
                if (depth > 0 && target != null)
                {
                    var cap = target.Bounds;
                    x += (cap.X + cap.Width / 2 - home) * depth;
                    y += (cap.Y + cap.Height / 2 - 418) * depth;
                }
                // The shoulder gives slightly on contact. The open contour joins the original black torso.
                double rootY = 383 + 4 * depth;
                var fur = new StreamGeometry();
                using (var path = fur.Open())
                {
                    path.BeginFigure(new Point(shoulder - 8, rootY), true, false);
                    path.BezierTo(new Point(shoulder - 14, rootY + 16), new Point(x - 13, y - 21), new Point(x - 12, y - 10), true, true);
                    path.BezierTo(new Point(x - 11, y + 2), new Point(x + 7, y + 4), new Point(x + 11, y - 5), true, true);
                    path.BezierTo(new Point(x + 15, y - 15), new Point(shoulder + 10, rootY + 15), new Point(shoulder + 8, rootY + 2), true, true);
                }
                dc.DrawGeometry(Ink, FurEdge, fur);
            }
        }
    }
}
