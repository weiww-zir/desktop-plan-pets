using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Xiaohei
{
    internal sealed class PetActor : Grid
    {
        private readonly DispatcherTimer idle = new DispatcherTimer { Interval = TimeSpan.FromSeconds(28) };
        private readonly DispatcherTimer hover = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(380) };
        private readonly DispatcherTimer finish = new DispatcherTimer();
        private readonly DispatcherTimer tailTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        private const int TypingPauseMs = 800;
        private const int StrokeIntervalMs = 84;
        private readonly Stopwatch sinceStroke = new Stopwatch();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly DispatcherTimer frames = new DispatcherTimer(DispatcherPriority.Render);
        private readonly PetArt.CompanionMotion companion = new PetArt.CompanionMotion();
        private readonly PetPointer pointer = new PetPointer();
        private double lastFrame, lastKey = -10;
        private int heldButtons;
        private bool sessionActive = true;
        private PetArt.TypingKeyboard typingKeyboard;
        private bool jumpAfterWave;
        private bool tailAfterSpirits;
        private bool automaticTailPlaying;
        private bool idleExpired;
        private readonly Decorator drawing = new Decorator();
        private FrameworkElement earAnchor;
        private readonly TranslateTransform badgeOffset = new TranslateTransform();
        private bool trackingEar;
        private readonly TextBlock countText = new TextBlock { FontFamily = new FontFamily("Segoe UI"),
            FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        private readonly Border badge = new Border { HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top, Background = new SolidColorBrush(Color.FromRgb(237, 61, 69)),
            BorderBrush = Brushes.White, BorderThickness = new Thickness(1), Visibility = Visibility.Collapsed,
            IsHitTestVisible = false };
        internal PetPose Pose { get; private set; }

        internal PetActor()
        {
            Children.Add(drawing);
            badge.Child = countText;
            badge.RenderTransform = badgeOffset;
            AutomationProperties.SetAutomationId(badge, "PendingBadge");
            Children.Add(badge);
            SizeChanged += delegate { SizeBadge(); };
            frames.Tick += AdvanceMotion;
            IsVisibleChanged += delegate { ScheduleFrames(); };
            SetPose(PetPose.Rest);
            Loaded += delegate
            {
                tailAfterSpirits = false;
                if (Pose != PetPose.Rest) SetPose(PetPose.Rest);
                if (!sessionActive) return;
                TrackEarIfMoving();
                RecordActivity();
                ScheduleFrames();
                tailTimer.Start();
            };
            Unloaded += delegate
            {
                idle.Stop(); hover.Stop(); finish.Stop(); tailTimer.Stop(); frames.Stop(); TrackEar(false);
                heldButtons = 0;
                pointer.Reset();
            };
            tailTimer.Tick += delegate
            {
                if (Pose == PetPose.SpiritOrbit) tailAfterSpirits = true;
                else if (Pose == PetPose.Rest && heldButtons == 0)
                    Play(PetPose.TailKittens, PetArt.TailLitter.DurationMs, true);
            };
            hover.Tick += delegate
            {
                hover.Stop();
                if (Pose == PetPose.Rest) { Act(PetPose.Tilt, 1320); companion.Peek(); }
            };
            idle.Tick += delegate
            {
                if (heldButtons != 0 || (typingKeyboard != null && typingKeyboard.IsKeyHeld)) return;
                idle.Stop();
                hover.Stop();
                idleExpired = true;
                if (Pose != PetPose.TailKittens) SetPose(PetPose.Nap);
            };
            finish.Tick += delegate
            {
                if (Pose == PetPose.Typing && typingKeyboard.IsKeyHeld) return;
                finish.Stop();
                if (tailAfterSpirits)
                {
                    tailAfterSpirits = false;
                    Play(PetPose.TailKittens, PetArt.TailLitter.DurationMs, true);
                }
                else if (Pose == PetPose.Wave && jumpAfterWave) Act(PetPose.Hop, 630);
                else SetPose(idleExpired ? PetPose.Nap : PetPose.Rest);
            };
        }

        private void SetPose(PetPose pose)
        {
            if (typingKeyboard != null) typingKeyboard.Rest();
            Pose = pose;
            earAnchor = new FrameworkElement { IsHitTestVisible = false };
            typingKeyboard = pose == PetPose.Typing ? new PetArt.TypingKeyboard(SystemParameters.ClientAreaAnimation) : null;
            drawing.Child = PetArt.Draw(true, pose, earAnchor, typingKeyboard, companion);
            ScheduleFrames();
            TrackEarIfMoving();
            InvalidateArrange();
        }

        private void ScheduleFrames()
        {
            if (!sessionActive || !IsLoaded || !IsVisible || !SystemParameters.ClientAreaAnimation ||
                (Pose != PetPose.Rest && Pose != PetPose.Tilt && Pose != PetPose.Typing && Pose != PetPose.Nap))
            {
                frames.Stop();
                return;
            }
            frames.Interval = TimeSpan.FromMilliseconds(Pose == PetPose.Nap ? 80 : 33);
            if (frames.IsEnabled) return;
            lastFrame = clock.Elapsed.TotalSeconds;
            frames.Start();
        }

        private void AdvanceMotion(object sender, EventArgs e)
        {
            double now = clock.Elapsed.TotalSeconds;
            double dt = Math.Min(.1, now - lastFrame);
            lastFrame = now;
            companion.Tick(dt, pointer.GazeX, pointer.GazeY, pointer.Speed);
            pointer.Decay(dt);
            PlaceBadge();
        }

        internal void ShowPendingCount(int count)
        {
            string label = count > 99 ? "99+" : count.ToString(CultureInfo.InvariantCulture);
            countText.Text = label;
            AutomationProperties.SetName(badge, "今日未完成 " + count + " 项");
            badge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
            SizeBadge();
            TrackEarIfMoving();
        }

        private void SizeBadge()
        {
            double height = Math.Max(14, Math.Min(30, Math.Round(ActualWidth * 0.21)));
            badge.Height = badge.MinWidth = height;
            badge.Padding = new Thickness(Math.Round(height * 0.15), 0, Math.Round(height * 0.15), 0);
            badge.CornerRadius = new CornerRadius(height / 2);
            countText.FontSize = Math.Round(height * 0.62);
            drawing.Margin = new Thickness(0, badge.Visibility == Visibility.Visible ? Math.Ceiling(height * .62) : 0, 0, 0);
        }

        protected override Size ArrangeOverride(Size size)
        {
            var result = base.ArrangeOverride(size);
            PlaceBadge();
            return result;
        }

        private void PlaceBadge()
        {
            if (badge.Visibility != Visibility.Visible || earAnchor == null || !earAnchor.IsArrangeValid) return;
            var tip = earAnchor.TranslatePoint(new Point(), this);
            double h = badge.ActualHeight;
            // Rest the rounded lower-left edge on the ear. Wider counts expand
            // toward the left when the ear is already near the screen edge.
            var available = drawing.RenderSize;
            double x = Math.Max(0, Math.Min(available.Width - badge.ActualWidth, tip.X - .20 * h));
            double y = Math.Max(0, Math.Min(available.Height + drawing.Margin.Top - h, tip.Y - .92 * h));
            if (badgeOffset.X != x) badgeOffset.X = x;
            if (badgeOffset.Y != y) badgeOffset.Y = y;
        }

        private void TrackEarIfMoving()
        {
            TrackEar(IsLoaded && badge.Visibility == Visibility.Visible &&
                Pose == PetPose.Hop);
        }

        private void TrackEar(bool enabled)
        {
            if (trackingEar == enabled) return;
            trackingEar = enabled;
            if (enabled) CompositionTarget.Rendering += FollowEar;
            else CompositionTarget.Rendering -= FollowEar;
        }

        private void FollowEar(object sender, EventArgs e) { PlaceBadge(); }

        private void Act(PetPose pose, int milliseconds)
        {
            RecordActivity();
            Play(pose, milliseconds);
        }

        private void Play(PetPose pose, int milliseconds, bool automaticTail = false)
        {
            hover.Stop();
            finish.Stop();
            SetPose(pose);
            automaticTailPlaying = automaticTail;
            finish.Interval = TimeSpan.FromMilliseconds(milliseconds);
            finish.Start();
        }

        private void RecordActivity()
        {
            idleExpired = false;
            idle.Stop();
            idle.Start();
            companion.ResetIdle();
        }

        internal void TapKeyboard(string key = null, bool privateInput = false, bool held = false)
        {
            if (!sessionActive || !IsLoaded || !IsVisible) return;
            RecordActivity();
            if (Pose == PetPose.Typing)
            {
                finish.Stop();
                finish.Start();
            }
            else
            {
                if (Pose != PetPose.Rest && Pose != PetPose.Nap && Pose != PetPose.Tilt &&
                    !(Pose == PetPose.TailKittens && automaticTailPlaying)) return;
                Play(PetPose.Typing, TypingPauseMs);
                if (heldButtons != 0) typingKeyboard.Mouse(pointer.MouseX, pointer.MouseY, heldButtons, 0);
            }
            bool newStroke = !sinceStroke.IsRunning || sinceStroke.ElapsedMilliseconds >= StrokeIntervalMs;
            lastKey = clock.Elapsed.TotalSeconds;
            // Always retarget the keycap, even while a previous paw stroke is in flight.
            typingKeyboard.Press(key, newStroke);
            if (held && key != null && !privateInput) typingKeyboard.Hold(key);
            if (privateInput) companion.PrivateInput();
            if (newStroke) sinceStroke.Restart();
        }

        internal void ReleaseKeyboard(string key)
        {
            if (typingKeyboard == null || !typingKeyboard.IsKeyHeld) return;
            typingKeyboard.Release(key);
            if (typingKeyboard.IsKeyHeld || Pose != PetPose.Typing) return;
            finish.Stop();
            finish.Start();
        }

        internal void PointerActivity(int dx, int dy, int buttons, int wheel)
        {
            if (!sessionActive || !IsLoaded || !IsVisible || ActualWidth <= 0 || ActualHeight <= 0 ||
                PresentationSource.FromVisual(this) == null) return;
            double now = clock.Elapsed.TotalSeconds;
            int down = buttons & 7;
            heldButtons = (heldButtons | down) & ~((buttons >> 3) & 7);
            var cursor = System.Windows.Forms.Cursor.Position;
            var bounds = System.Windows.Forms.Screen.FromPoint(cursor).Bounds;
            var screen = new Point(cursor.X, cursor.Y);
            double jitter = now - lastKey < .5 ? Math.Max(8, bounds.Width * .015) : Pose == PetPose.Nap ? 7 : 3;
            bool moved = pointer.Sample(screen, new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                PointFromScreen(screen), RenderSize, now, dx, dy, (buttons | heldButtons) != 0 || wheel != 0, jitter);
            if (!moved && buttons == 0 && wheel == 0) return;
            Touch();
            if (pointer.Petting)
            {
                companion.Pet();
                return;
            }
            if (typingKeyboard == null) return;
            if (down != 0 && (heldButtons & down) != down)
                typingKeyboard.Mouse(pointer.MouseX, pointer.MouseY, heldButtons | down, 0);
            typingKeyboard.Mouse(pointer.MouseX, pointer.MouseY, heldButtons, wheel);
            if (down != 0 || wheel != 0) companion.Pulse(1);
        }

        internal void EndTyping()
        {
            if (Pose != PetPose.Typing) return;
            finish.Stop();
            SetPose(PetPose.Rest);
        }

        internal void ResetTransientInput()
        {
            heldButtons = 0;
            lastKey = -10;
            pointer.Reset();
            EndTyping();
            if (sessionActive && IsLoaded && IsVisible && Pose != PetPose.Nap) RecordActivity();
        }

        internal void SetSessionActive(bool active)
        {
            if (sessionActive == active) return;
            sessionActive = active;
            if (!active)
            {
                idle.Stop(); hover.Stop(); finish.Stop(); tailTimer.Stop(); frames.Stop(); TrackEar(false);
                SetPose(PetPose.Rest);
            }
            else if (IsLoaded && IsVisible)
            {
                RecordActivity();
                tailTimer.Start();
                ScheduleFrames();
            }
        }

        internal void Touch()
        {
            if (!sessionActive) return;
            RecordActivity();
            if (Pose == PetPose.Nap) Act(PetPose.Stretch, 1140);
        }

        internal void Enter()
        {
            if (!sessionActive) return;
            Touch();
            if (Pose == PetPose.Rest) hover.Start();
        }

        internal void Leave() { hover.Stop(); pointer.ResetStroke(); }

        internal void Celebrate(bool dayComplete)
        {
            jumpAfterWave = dayComplete;
            Act(PetPose.Wave, 850);
        }

        internal void Meow() { Act(PetPose.Meow, 1150); }
        internal void WatchSpirits() { Act(PetPose.SpiritOrbit, PetArt.SpiritOrbit.DurationMs); }
        internal void SplitTail()
        {
            tailAfterSpirits = false;
            tailTimer.Stop();
            tailTimer.Start();
            Act(PetPose.TailKittens, PetArt.TailLitter.DurationMs);
        }
    }
}
