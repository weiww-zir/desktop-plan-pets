using System;
using System.Collections.Generic;
using System.Diagnostics;
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
                internal Point Center { get { return new Point(Bounds.X + Bounds.Width / 2, Bounds.Y + Bounds.Height / 2); } }
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
            private static readonly Geometry BoardOutline = Geometry.Parse("M 229,424 Q 233,418 241,418 L 514,418 Q 521,418 526,424 L 561,480 Q 568,495 553,499 L 210,499 Q 193,497 200,484 Z");
            private static readonly DrawingGroup Board = DrawBoard();
            private static readonly DependencyProperty LeftDepth = DependencyProperty.Register("LeftDepth", typeof(double),
                typeof(TypingKeyboard), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
            private static readonly DependencyProperty RightDepth = DependencyProperty.Register("RightDepth", typeof(double),
                typeof(TypingKeyboard), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
            // The aim can move between two keycaps while the same paw stays extended.
            private static readonly DependencyProperty LeftAim = DependencyProperty.Register("LeftAim", typeof(Point),
                typeof(TypingKeyboard), new FrameworkPropertyMetadata(new Point(344, 418), FrameworkPropertyMetadataOptions.AffectsRender));
            private static readonly DependencyProperty RightAim = DependencyProperty.Register("RightAim", typeof(Point),
                typeof(TypingKeyboard), new FrameworkPropertyMetadata(new Point(402, 418), FrameworkPropertyMetadataOptions.AffectsRender));
            private static readonly DependencyProperty MouseReach = DependencyProperty.Register("MouseReach", typeof(double),
                typeof(TypingKeyboard), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
            private static readonly DependencyProperty MouseOffsetX = DependencyProperty.Register("MouseOffsetX", typeof(double),
                typeof(TypingKeyboard), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
            private static readonly DependencyProperty WheelFlash = DependencyProperty.Register("WheelFlash", typeof(double),
                typeof(TypingKeyboard), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
            private static readonly DependencyProperty ClickFlash = DependencyProperty.Register("ClickFlash", typeof(double),
                typeof(TypingKeyboard), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
            private static readonly DoubleAnimationUsingKeyFrames Stroke = Pulse(false), FastStroke = Pulse(true);

            private readonly bool motion;
            private readonly Canvas seatedLeftPaw = new Canvas { Visibility = Visibility.Hidden };
            private readonly Border hint;
            private readonly TextBlock letter;
            private readonly TranslateTransform hintLift = new TranslateTransform();
            private Keycap litKey;
            private bool left, anonymous, mouseDown, mousing, contactDrawn;
            private int wheelDirection, mouseButtons, pulseButtons;
            private Point mouse = new Point(601, 466);
            private double leftWeight = 1, rightWeight = 1;
            private readonly Stopwatch sinceMouse = new Stopwatch();
            private DependencyProperty latestDepth = LeftDepth;
            private string heldKey;
            private DependencyProperty heldDepth;
            internal bool IsKeyHeld { get { return heldKey != null; } }
            internal bool KeyboardVisible { get; private set; }
            internal bool MouseVisible { get; private set; }
            internal double StrikeSide { get; private set; }
            internal TypingKeyboard(bool motion)
            {
                this.motion = motion;
                KeyboardVisible = MouseVisible = true;
                Width = 640;
                Height = 505;
                IsHitTestVisible = false;
                letter = new TextBlock { FontFamily = Lettering.FontFamily, FontSize = 74, FontWeight = FontWeights.Bold,
                    Foreground = KeyInk, TextAlignment = TextAlignment.Center };
                hint = new Border { Visibility = Visibility.Hidden, Background = Key, BorderBrush = FaceLine,
                    BorderThickness = new Thickness(4, 4, 4, 7), CornerRadius = new CornerRadius(17),
                    RenderTransform = hintLift,
                    Padding = new Thickness(6, 0, 6, 2), Child = new Viewbox { Stretch = Stretch.Uniform, Child = letter } };
                DrawSeatedLeftPaw(seatedLeftPaw);
                Children.Add(seatedLeftPaw);
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
                    dc.DrawGeometry(Case, Rim, BoardOutline);
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
                if (!KeyboardVisible || (heldKey != null && heldKey == key)) return;
                Release(heldKey);
                Keycap cap = null;
                if (key != null && !Keys.TryGetValue(key, out cap))
                {
                    if (ExtendedKeys.Contains(key)) cap = Keys["Fn"];
                    else key = null;
                }
                // An anonymous event must not reveal a previous named reach, even while that paw is returning.
                if (!anonymous && cap == null)
                {
                    SetDepth(LeftDepth, 0);
                    SetDepth(RightDepth, 0);
                    BeginAnimation(LeftAim, null);
                    BeginAnimation(RightAim, null);
                }
                anonymous = cap == null;
                if (anonymous) cap = Keys[left ? "J" : "F"];
                bool mouseBusy = mousing && (mouseDown || sinceMouse.ElapsedMilliseconds < 320);
                if (!mouseBusy && mousing) { mousing = false; SetDepth(MouseReach, 0); }
                var target = cap.Center;
                left = mouseBusy || target.X < 377;
                StrikeSide = left ? -1 : 1;
                latestDepth = left ? LeftDepth : RightDepth;
                litKey = anonymous ? null : cap;
                contactDrawn = false;
                AimPaw(left ? LeftAim : RightAim, latestDepth, target);
                double weight = key == "Tab" || key == "Shift" || key == "Ctrl" ? 1.25 : animateStroke ? 1.0 : .8;
                if (left) leftWeight = weight;
                else rightWeight = weight;
                letter.Text = key ?? "";
                hint.Visibility = anonymous ? Visibility.Hidden : Visibility.Visible;
                if (!anonymous)
                {
                    bool wide = key.Length > 2;
                    hint.Width = wide ? 120 : 101;
                    hint.Height = wide ? 64 : 87;
                    SetLeft(hint, wide ? 514 : 533);
                    SetTop(hint, wide ? 314 : 278);
                    if (motion) hintLift.BeginAnimation(TranslateTransform.YProperty,
                        new DoubleAnimation(animateStroke ? 9 : 4, 0, TimeSpan.FromMilliseconds(155))
                        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
                }
                if (motion) Tap(latestDepth, animateStroke ? Stroke : FastStroke);
                else
                    SetPressDepth(1);
                InvalidateVisual();
            }

            internal void Hold(string key)
            {
                if (key == null || heldKey == key || litKey == null || letter.Text != key) return;
                heldKey = key;
                heldDepth = latestDepth;
                AnimateTo(heldDepth, 1, 42);
            }

            internal void Release(string key)
            {
                if (heldKey == null || (key != null && key != heldKey)) return;
                heldKey = null;
                double depth = (double)GetValue(heldDepth);
                // A key pressed and released between display frames should still get a full tap.
                if (motion && key != null && depth < .94) Tap(heldDepth, Stroke);
                else if (!motion && key != null && !contactDrawn)
                {
                    // Hold one static pose for a short tap when Windows animations are off.
                    SetDepth(heldDepth, 1);
                    var lift = new DoubleAnimationUsingKeyFrames();
                    lift.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(70))));
                    BeginAnimation(heldDepth, lift);
                }
                else AnimateTo(heldDepth, 0, 110);
                heldDepth = null;
                if (key == null)
                {
                    litKey = null;
                    letter.Text = "";
                    hint.Visibility = Visibility.Hidden;
                }
            }

            private void AimPaw(DependencyProperty aim, DependencyProperty depth, Point target)
            {
                var from = (Point)GetValue(aim);
                BeginAnimation(aim, null);
                SetValue(aim, target);
                if (motion && from != target && (double)GetValue(depth) > .05)
                    BeginAnimation(aim, new PointAnimation(from, target, TimeSpan.FromMilliseconds(44))
                        { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }, FillBehavior = FillBehavior.Stop });
            }

            private void Tap(DependencyProperty depth, DoubleAnimationUsingKeyFrames timing)
            {
                var stroke = timing;
                double current = (double)GetValue(depth);
                if (current > .001)
                {
                    stroke = timing.Clone();
                    stroke.KeyFrames[0].Value = current;
                }
                SetValue(depth, 0.0);
                BeginAnimation(depth, stroke);
            }

            private void AnimateTo(DependencyProperty property, double value, int milliseconds)
            {
                double current = (double)GetValue(property);
                SetDepth(property, value);
                if (motion && Math.Abs(current - value) > .005)
                    BeginAnimation(property, new DoubleAnimation(current, value, TimeSpan.FromMilliseconds(milliseconds))
                        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
            }

            internal void Mouse(double x, double y, int heldButtons, int wheel)
            {
                if (!MouseVisible) return;
                mouse = new Point(601 + 8 * Math.Max(-1, Math.Min(1, x)), 466 + 7 * Math.Max(-1, Math.Min(1, y)));
                int pressed = heldButtons & ~mouseButtons & 7;
                mouseButtons = heldButtons & 7;
                mouseDown = mouseButtons != 0;
                sinceMouse.Restart();
                if (!mousing)
                {
                    mousing = true;
                    if (IsKeyHeld && heldDepth == RightDepth)
                    {
                        // Keep holding the actual key with the free paw when the right paw reaches for the mouse.
                        AimPaw(LeftAim, LeftDepth, litKey.Center);
                        leftWeight = rightWeight;
                        heldDepth = latestDepth = LeftDepth;
                        AnimateTo(LeftDepth, 1, 75);
                    }
                    SetDepth(RightDepth, 0);
                    SetDepth(MouseReach, 1);
                    if (motion) BeginAnimation(MouseReach, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(110))
                        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
                }
                if (pressed != 0)
                {
                    pulseButtons = pressed;
                    SetDepth(ClickFlash, 0);
                    BeginAnimation(ClickFlash, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(175))
                        { FillBehavior = FillBehavior.Stop });
                }
                if (wheel != 0)
                {
                    wheelDirection = Math.Sign(wheel);
                    SetDepth(WheelFlash, motion ? 0 : 1);
                    if (motion) BeginAnimation(WheelFlash, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(245))
                        { FillBehavior = FillBehavior.Stop });
                }
                else if (!motion) SetDepth(WheelFlash, 0);
                StrikeSide = 1;
                InvalidateVisual();
            }

            internal void SetKeyboardVisible(bool visible)
            {
                if (KeyboardVisible == visible) return;
                KeyboardVisible = visible;
                seatedLeftPaw.Visibility = visible ? Visibility.Hidden : Visibility.Visible;
                // Without a keyboard in the way, the mouse can sit next to the cat's paw.
                double offset = visible ? 0 : -98;
                if (MouseVisible && mousing) AnimateTo(MouseOffsetX, offset, 120);
                else SetDepth(MouseOffsetX, offset);
                if (!visible) ClearKeyboard();
                InvalidateVisual();
            }

            internal void SetMouseVisible(bool visible)
            {
                if (MouseVisible == visible) return;
                MouseVisible = visible;
                if (!visible) ClearMouse();
                InvalidateVisual();
            }

            private void ClearKeyboard()
            {
                heldKey = null;
                heldDepth = null;
                litKey = null;
                left = anonymous = contactDrawn = false;
                BeginAnimation(LeftAim, null);
                BeginAnimation(RightAim, null);
                SetDepth(LeftDepth, 0);
                SetDepth(RightDepth, 0);
                letter.Text = "";
                hint.Visibility = Visibility.Hidden;
                hintLift.BeginAnimation(TranslateTransform.YProperty, null);
            }

            private void ClearMouse()
            {
                SetDepth(MouseReach, 0);
                SetDepth(WheelFlash, 0);
                SetDepth(ClickFlash, 0);
                mouseButtons = pulseButtons = 0;
                mouse = new Point(601, 466);
                mousing = mouseDown = false;
                sinceMouse.Reset();
            }

            internal void Rest()
            {
                ClearKeyboard();
                ClearMouse();
                StrikeSide = 0;
                InvalidateVisual();
            }

            internal void SetPressDepth(double depth)
            {
                depth = Math.Max(0, Math.Min(1, depth));
                SetDepth(LeftDepth, latestDepth == LeftDepth ? depth : 0);
                SetDepth(RightDepth, latestDepth == RightDepth ? depth : 0);
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
                pulse.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                pulse.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(compressed ? 20 : 36))));
                pulse.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(compressed ? 43 : 63))));
                pulse.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(compressed ? 102 : 158)), new KeySpline(.22, 0, .35, 1)));
                pulse.Freeze();
                return pulse;
            }

            protected override void OnRender(DrawingContext dc)
            {
                base.OnRender(dc);
                double leftDepth = (double)GetValue(LeftDepth), rightDepth = (double)GetValue(RightDepth);
                if (KeyboardVisible)
                {
                    dc.DrawDrawing(Board);
                    double keyDepth = (double)GetValue(latestDepth);
                    if (litKey != null && keyDepth >= .94) contactDrawn = true;
                    if (litKey != null && keyDepth > .1) DrawKey(dc, litKey, Pressed, keyDepth * 2);
                }
                var mousePosition = new Point(mouse.X + (double)GetValue(MouseOffsetX), mouse.Y);
                if (MouseVisible) DrawMouse(dc, mousePosition);
                if (KeyboardVisible) DrawPaw(dc, true, (Point)GetValue(LeftAim), leftDepth, leftWeight);
                if (mousing)
                    DrawPaw(dc, false, new Point(mousePosition.X + ((mouseButtons & 2) != 0 ? 4 : -8), mousePosition.Y + (mouseDown ? 9 : 3)),
                        (double)GetValue(MouseReach), mouseDown ? 1.3 : .75);
                else DrawPaw(dc, false, (Point)GetValue(RightAim), rightDepth, rightWeight);
            }

            private static readonly Geometry MouseShell = Geometry.Parse("M -22,4 C -25,-7 -18,-22 -7,-26 C 6,-31 18,-23 22,-9 C 26,5 23,19 12,24 C -1,30 -16,23 -22,4 Z");
            private static readonly Geometry MouseLeft = Geometry.Parse("M -21,0 Q -23,-19 -8,-24 Q -5,-26 -2,-26 L -1,-4 Q -12,-1 -21,0 Z");
            private static readonly Geometry MouseRight = Geometry.Parse("M 1,-26 Q 17,-26 21,-7 L 2,-4 Z");
            private static readonly Pen MouseLine = MakePen(ColorBrush(102, 113, 96), 2.4);

            private void DrawMouse(DrawingContext dc, Point position)
            {
                dc.PushTransform(new TranslateTransform(position.X, position.Y));
                dc.DrawGeometry(Key, Rim, MouseShell);
                int active = mouseButtons | ((double)GetValue(ClickFlash) > .12 ? pulseButtons : 0);
                dc.DrawGeometry((active & 1) != 0 ? Pressed : Key, MouseLine, MouseLeft);
                dc.DrawGeometry((active & 2) != 0 ? Pressed : Key, MouseLine, MouseRight);
                double flash = (double)GetValue(WheelFlash);
                dc.DrawRoundedRectangle((active & 4) != 0 || flash > .15 ? Pressed : Deck, MouseLine,
                    new Rect(-3.6, -19, 7.2, 12), 3.5, 3.5);
                if (flash > .05)
                {
                    dc.PushOpacity(flash);
                    double y = wheelDirection > 0 ? -34 : -32;
                    var peak = new Point(1, y - 5 * wheelDirection);
                    dc.DrawLine(MouseLine, new Point(-5, y), peak);
                    dc.DrawLine(MouseLine, peak, new Point(7, y));
                    dc.Pop();
                }
                dc.Pop();
            }

            private static void DrawPaw(DrawingContext dc, bool isLeft, Point target, double depth, double weight)
            {
                double shoulder = isLeft ? 332 : 415;
                double home = isLeft ? 344 : 402, x = home, y = 418;
                x += (target.X - home) * depth;
                y += (target.Y - 418) * depth;
                double rootY = 383 + 5 * depth * weight;
                var root = new Point(shoulder, rootY);
                var tip = new Point(x, y);
                Vector direction = tip - root;
                direction.Normalize();
                var across = new Vector(direction.Y, -direction.X);
                // The elbow sags a little on a long reach; keep the wrist rounded when the arm turns sideways.
                var bend = new Vector(0, Math.Min(12, Math.Abs(x - home) * .1));
                var fur = new StreamGeometry();
                using (var path = fur.Open())
                {
                    path.BeginFigure(root - across * 8, true, false);
                    path.BezierTo(root - across * 13 + direction * 15 + bend, tip - across * 13 - direction * 18 + bend,
                        tip - across * 11 - direction * 7, true, true);
                    path.BezierTo(tip - across * 11 + direction * 3, tip + across * 8 + direction * 4, tip + across * 10 - direction * 5, true, true);
                    path.BezierTo(tip + across * 14 - direction * 17 + bend, root + across * 10 + direction * 15 + bend,
                        root + across * 8, true, true);
                }
                dc.DrawGeometry(Ink, FurEdge, fur);
            }
        }
    }
}
