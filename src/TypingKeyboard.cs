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
            private static readonly DependencyProperty TailDepth = DependencyProperty.Register("TailDepth", typeof(double),
                typeof(TypingKeyboard), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender,
                    (d, e) => ((TypingKeyboard)d).BendTailTip()));
            private static readonly DependencyProperty TailAim = DependencyProperty.Register("TailAim", typeof(Point),
                typeof(TypingKeyboard), new FrameworkPropertyMetadata(new Point(), FrameworkPropertyMetadataOptions.AffectsRender,
                    (d, e) => ((TypingKeyboard)d).BendTailTip()));
            private static readonly DependencyProperty MouseReach = DependencyProperty.Register("MouseReach", typeof(double),
                typeof(TypingKeyboard), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
            private static readonly DependencyProperty WheelFlash = DependencyProperty.Register("WheelFlash", typeof(double),
                typeof(TypingKeyboard), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
            private static readonly DependencyProperty ClickFlash = DependencyProperty.Register("ClickFlash", typeof(double),
                typeof(TypingKeyboard), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
            private static readonly DoubleAnimationUsingKeyFrames Stroke = Pulse(false), FastStroke = Pulse(true);
            private static readonly DoubleAnimationUsingKeyFrames TailStroke = Pulse(false, 1.65);

            private readonly bool motion;
            private bool motionActive = true;
            private readonly Border hint;
            private readonly TextBlock letter;
            private readonly TranslateTransform hintLift = new TranslateTransform();
            private Keycap leftTarget, rightTarget, tailTarget, litKey;
            private bool left, anonymous, mouseDown, mousing, idlePawRaised;
            private int wheelDirection, mouseButtons, pulseButtons;
            private Point mouse = new Point(601, 466);
            private double leftWeight = 1, rightWeight = 1;
            private readonly Stopwatch sinceMouse = new Stopwatch(), sinceLeft = new Stopwatch();
            private Canvas seatedTail;
            private System.Windows.Shapes.Path tailTip;
            private Geometry restingTip;
            private double restingTipWidth;
            private readonly Pen tipEdge = FurEdge.Clone();
            private readonly RotateTransform tailSwing = new RotateTransform(0, 430, 420);
            private readonly RotateTransform tailThump = new RotateTransform(0, 430, 420);
            private DependencyProperty latestDepth = LeftDepth;
            private string heldKey;
            private DependencyProperty heldDepth;
            internal bool IsKeyHeld { get { return heldKey != null; } }
            internal double StrikeSide { get; private set; }
            internal bool IdlePawRaised
            {
                get { return idlePawRaised; }
                set { if (idlePawRaised != value) { idlePawRaised = value; InvalidateVisual(); } }
            }

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
                    RenderTransform = hintLift,
                    Padding = new Thickness(6, 0, 6, 2), Child = new Viewbox { Stretch = Stretch.Uniform, Child = letter } };
                Children.Add(hint);
                Loaded += delegate { SwingTail(); };
                Unloaded += delegate {
                    tailSwing.BeginAnimation(RotateTransform.AngleProperty, null);
                    tailThump.BeginAnimation(RotateTransform.AngleProperty, null);
                };
            }

            internal void AttachTail(Canvas tail, Canvas leftTip)
            {
                seatedTail = tail;
                tailTip = (System.Windows.Shapes.Path)leftTip.Children[0];
                restingTip = tailTip.Data;
                restingTipWidth = tailTip.StrokeThickness;
                var turns = new TransformGroup();
                turns.Children.Add(tailSwing);
                turns.Children.Add(tailThump);
                tail.RenderTransform = turns;
                BendTailTip();
                if (IsLoaded) SwingTail();
            }

            private void SwingTail()
            {
                if (!motion || !motionActive || !IsLoaded || seatedTail == null) return;
                var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
                var sway = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
                sway.KeyFrames.Add(new EasingDoubleKeyFrame(3, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.05)), ease));
                sway.KeyFrames.Add(new EasingDoubleKeyFrame(-2.5, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.15)), ease));
                sway.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.2)), ease));
                tailSwing.BeginAnimation(RotateTransform.AngleProperty, sway);
            }

            internal void SetMotionActive(bool value)
            {
                if (motionActive == value) return;
                motionActive = value;
                if (value) SwingTail();
                else
                {
                    tailSwing.BeginAnimation(RotateTransform.AngleProperty, null);
                    tailThump.BeginAnimation(RotateTransform.AngleProperty, null);
                }
            }

            internal void ThumpTail()
            {
                if (!motion || !motionActive || seatedTail == null || (double)GetValue(TailDepth) > .015 || (latestDepth == TailDepth && litKey != null)) return;
                var tap = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
                tap.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                tap.KeyFrames.Add(new SplineDoubleKeyFrame(-12, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90)), new KeySpline(.2, .8, .4, 1)));
                tap.KeyFrames.Add(new LinearDoubleKeyFrame(8, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(155))));
                tap.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300)), new KeySpline(.2, .8, .4, 1)));
                tailThump.BeginAnimation(RotateTransform.AngleProperty, tap);
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
                if (heldKey != null && heldKey == key) return;
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
                    SetDepth(TailDepth, 0);
                    leftTarget = rightTarget = tailTarget = null;
                }
                anonymous = cap == null;
                if (anonymous) cap = Keys[left ? "J" : "F"];
                bool mouseBusy = mousing && (mouseDown || sinceMouse.ElapsedMilliseconds < 320);
                bool special = key == "Space" || key == "Enter" || key == "Bksp" || key == "Esc";
                bool thirdHand = mouseBusy && mouseDown && sinceLeft.IsRunning && sinceLeft.ElapsedMilliseconds < 125;
                if (!mouseBusy && mousing) { mousing = false; SetDepth(MouseReach, 0); }
                left = mouseBusy || cap.Bounds.X + cap.Bounds.Width / 2 < 377;
                StrikeSide = special || (thirdHand && !anonymous) ? 0 : left ? -1 : 1;
                latestDepth = StrikeSide == 0 ? TailDepth : left ? LeftDepth : RightDepth;
                litKey = anonymous ? null : cap;
                if (StrikeSide == 0)
                {
                    Point start = (Point)GetValue(TailAim), target = Center(cap);
                    bool crossing = tailTarget != null && start != target && (double)GetValue(TailDepth) > .01;
                    tailTarget = cap;
                    BeginAnimation(TailAim, null);
                    SetValue(TailAim, target);
                    if (motion && crossing)
                        BeginAnimation(TailAim, new PointAnimation(start, target, TimeSpan.FromMilliseconds(68))
                            { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }, FillBehavior = FillBehavior.Stop });
                    tailThump.BeginAnimation(RotateTransform.AngleProperty, null);
                }
                else
                {
                    double weight = key == "Tab" || key == "Shift" || key == "Ctrl" ? 1.25 : animateStroke ? 1.0 : .8;
                    if (left) { leftTarget = cap; leftWeight = weight; sinceLeft.Restart(); }
                    else { rightTarget = cap; rightWeight = weight; }
                }
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
                if (motion)
                {
                    var stroke = animateStroke ? Stroke : FastStroke;
                    if (StrikeSide == 0)
                    {
                        stroke = TailStroke.Clone();
                        stroke.KeyFrames[0].Value = (double)GetValue(TailDepth);
                    }
                    SetValue(latestDepth, 0.0);
                    BeginAnimation(latestDepth, stroke);
                }
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
                if (motion && key != null && heldDepth == TailDepth && depth < .94)
                {
                    // Even a make/break pair between display frames gets one complete tap.
                    var tap = TailStroke.Clone();
                    tap.KeyFrames[0].Value = depth;
                    SetValue(TailDepth, 0.0);
                    BeginAnimation(TailDepth, tap);
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
                        leftTarget = rightTarget;
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
                if (!IsKeyHeld) litKey = null;
                letter.Text = "";
                hint.Visibility = Visibility.Hidden;
                hintLift.BeginAnimation(TranslateTransform.YProperty, null);
                InvalidateVisual();
            }

            internal void Rest()
            {
                tailThump.BeginAnimation(RotateTransform.AngleProperty, null);
                heldKey = null;
                heldDepth = null;
                leftTarget = rightTarget = tailTarget = litKey = null;
                BeginAnimation(TailAim, null);
                SetDepth(LeftDepth, 0);
                SetDepth(RightDepth, 0);
                SetDepth(TailDepth, 0);
                SetDepth(MouseReach, 0);
                SetDepth(WheelFlash, 0);
                SetDepth(ClickFlash, 0);
                mouseButtons = pulseButtons = 0;
                mouse = new Point(601, 466);
                mousing = mouseDown = anonymous = IdlePawRaised = false;
                sinceMouse.Reset();
                sinceLeft.Reset();
                StrikeSide = 0;
                letter.Text = "";
                hint.Visibility = Visibility.Hidden;
                hintLift.BeginAnimation(TranslateTransform.YProperty, null);
                InvalidateVisual();
            }

            internal void SetPressDepth(double depth)
            {
                depth = Math.Max(0, Math.Min(1, depth));
                SetDepth(LeftDepth, latestDepth == LeftDepth ? depth : 0);
                SetDepth(RightDepth, latestDepth == RightDepth ? depth : 0);
                SetDepth(TailDepth, latestDepth == TailDepth ? depth : 0);
                InvalidateVisual();
            }

            private void SetDepth(DependencyProperty property, double depth)
            {
                BeginAnimation(property, null);
                SetValue(property, depth);
            }

            private static DoubleAnimationUsingKeyFrames Pulse(bool compressed, double durationScale = 1)
            {
                var pulse = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
                if (!compressed) pulse.KeyFrames.Add(new LinearDoubleKeyFrame(.16, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                pulse.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds((compressed ? 0 : 36) * durationScale))));
                pulse.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds((compressed ? 25 : 63) * durationScale))));
                pulse.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds((compressed ? 93 : 158) * durationScale)), new KeySpline(.22, 0, .35, 1)));
                pulse.Freeze();
                return pulse;
            }

            protected override void OnRender(DrawingContext dc)
            {
                base.OnRender(dc);
                dc.DrawDrawing(Board);
                double leftDepth = (double)GetValue(LeftDepth), rightDepth = (double)GetValue(RightDepth);
                double keyDepth = (double)GetValue(latestDepth), tailDepth = (double)GetValue(TailDepth);
                if (litKey != null && keyDepth > .1) DrawKey(dc, litKey, Pressed, keyDepth * 2);
                DrawMouse(dc);
                if (tailTip != null && tailTarget != null && tailDepth > 0)
                {
                    // The bent tip already sits behind the cat. Bring only its keyboard contact in front of the deck.
                    dc.PushClip(BoardOutline);
                    dc.PushOpacity(Math.Min(1, tailDepth * 3));
                    dc.DrawGeometry(Ink, tipEdge, tailTip.Data);
                    dc.Pop();
                    dc.Pop();
                }
                if (!IdlePawRaised) DrawPaw(dc, true, Center(leftTarget), leftDepth, leftWeight);
                if (mousing)
                    DrawPaw(dc, false, new Point(mouse.X + ((mouseButtons & 2) != 0 ? 4 : -8), mouse.Y + (mouseDown ? 9 : 3)),
                        (double)GetValue(MouseReach), mouseDown ? 1.3 : .75);
                else DrawPaw(dc, false, Center(rightTarget), rightDepth, rightWeight);
            }

            private static Point Center(Keycap cap)
            {
                return cap == null ? new Point() : new Point(cap.Bounds.X + cap.Bounds.Width / 2, cap.Bounds.Y + cap.Bounds.Height / 2);
            }

            private static readonly Geometry MouseShell = Geometry.Parse("M -22,4 C -25,-7 -18,-22 -7,-26 C 6,-31 18,-23 22,-9 C 26,5 23,19 12,24 C -1,30 -16,23 -22,4 Z");
            private static readonly Geometry MouseLeft = Geometry.Parse("M -21,0 Q -23,-19 -8,-24 Q -5,-26 -2,-26 L -1,-4 Q -12,-1 -21,0 Z");
            private static readonly Geometry MouseRight = Geometry.Parse("M 1,-26 Q 17,-26 21,-7 L 2,-4 Z");
            private static readonly Pen MouseLine = MakePen(ColorBrush(102, 113, 96), 2.4);

            private void DrawMouse(DrawingContext dc)
            {
                dc.PushTransform(new TranslateTransform(mouse.X, mouse.Y));
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

            private static Point BendPoint(double x, double y, double endX, double endY, double depth)
            {
                return new Point(x + (endX - x) * depth, y + (endY - y) * depth);
            }

            private void BendTailTip()
            {
                if (tailTip == null) return;
                double depth = (double)GetValue(TailDepth);
                double unfold = Math.Min(1, depth * 2.5);
                tailTip.StrokeThickness = tipEdge.Thickness = restingTipWidth + (5 - restingTipWidth) * unfold;
                if (depth <= 0 || tailTarget == null) { tailTip.Data = restingTip; return; }
                var key = (Point)GetValue(TailAim);
                double bendX = Math.Min(244, key.X - 50), reach = key.X - bendX - 24;
                double bendY = Math.Min(466, key.Y + Math.Min(30, reach * .18));
                double approach = Math.Min(32, reach * .35), lift = Math.Min(15, reach * .15);
                // Subdivide the seated tip's original cubic at one third. The folded length unfurls from that point.
                const double foldX = 266.667, foldY = 425.111;
                var outline = new StreamGeometry();
                using (var p = outline.Open())
                {
                    p.BeginFigure(new Point(318, 402), true, true);
                    p.BezierTo(BendPoint(297,399, 293,395, depth), BendPoint(278,391, bendX+9,391, depth),
                        BendPoint(269,407, bendX-1,413, depth), true,true);
                    p.BezierTo(BendPoint(265.667,413.333, bendX-16,437, depth), BendPoint(265,419.667, bendX-5,bendY+6, depth),
                        BendPoint(foldX,foldY, bendX+24,bendY+9, depth), true,true);
                    p.BezierTo(BendPoint(foldX,foldY, bendX+24+approach,bendY+15, depth), BendPoint(foldX,foldY, key.X-approach,key.Y-lift, depth),
                        BendPoint(foldX,foldY, key.X-5,key.Y+5, depth), true,true);
                    p.BezierTo(BendPoint(foldX,foldY, key.X+7,key.Y+7, depth), BendPoint(foldX,foldY, key.X+9,key.Y-6, depth),
                        BendPoint(foldX,foldY, key.X-3,key.Y-7, depth), true,true);
                    p.BezierTo(BendPoint(foldX,foldY, key.X-approach-1,key.Y-lift-17, depth), BendPoint(foldX,foldY, bendX+3,bendY+8, depth),
                        BendPoint(foldX,foldY, bendX+10,435, depth), true,true);
                    p.BezierTo(BendPoint(270,436, bendX+13,422, depth), BendPoint(282.667,443.333, 283,407, unfold),
                        BendPoint(302,440, 302,410, unfold), true,true);
                    p.LineTo(BendPoint(320,434,320,410,unfold), true,true);
                }
                outline.Freeze();
                tailTip.Data = outline;
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
