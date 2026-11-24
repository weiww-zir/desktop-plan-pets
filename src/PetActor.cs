using System;
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
        private readonly DispatcherTimer idle = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        private readonly DispatcherTimer hover = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(380) };
        private readonly DispatcherTimer finish = new DispatcherTimer();
        private readonly DispatcherTimer tailTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        private bool jumpAfterWave;
        private bool tailAfterSpirits;
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
            SetPose(PetPose.Rest);
            Loaded += delegate
            {
                tailAfterSpirits = false;
                if (Pose != PetPose.Rest) SetPose(PetPose.Rest);
                TrackEarIfMoving();
                RecordActivity();
                tailTimer.Start();
            };
            Unloaded += delegate { idle.Stop(); hover.Stop(); finish.Stop(); tailTimer.Stop(); TrackEar(false); };
            tailTimer.Tick += delegate
            {
                if (Pose == PetPose.SpiritOrbit) tailAfterSpirits = true;
                else Play(PetPose.TailKittens, PetArt.TailLitter.DurationMs);
            };
            hover.Tick += delegate
            {
                hover.Stop();
                if (Pose == PetPose.Rest) Act(PetPose.Tilt, 1150);
            };
            idle.Tick += delegate
            {
                idle.Stop();
                hover.Stop();
                idleExpired = true;
                if (Pose != PetPose.TailKittens) SetPose(PetPose.Nap);
            };
            finish.Tick += delegate
            {
                finish.Stop();
                if (tailAfterSpirits)
                {
                    tailAfterSpirits = false;
                    Play(PetPose.TailKittens, PetArt.TailLitter.DurationMs);
                }
                else if (Pose == PetPose.Wave && jumpAfterWave) Act(PetPose.Hop, 630);
                else SetPose(idleExpired ? PetPose.Nap : PetPose.Rest);
            };
        }

        private void SetPose(PetPose pose)
        {
            Pose = pose;
            earAnchor = new FrameworkElement { IsHitTestVisible = false };
            drawing.Child = PetArt.Draw(true, pose, earAnchor);
            TrackEarIfMoving();
            InvalidateArrange();
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
                (Pose == PetPose.Tilt || Pose == PetPose.Hop));
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

        private void Play(PetPose pose, int milliseconds)
        {
            hover.Stop();
            finish.Stop();
            SetPose(pose);
            finish.Interval = TimeSpan.FromMilliseconds(milliseconds);
            finish.Start();
        }

        private void RecordActivity()
        {
            idleExpired = false;
            idle.Stop();
            idle.Start();
        }

        internal void Touch()
        {
            RecordActivity();
            if (Pose == PetPose.Nap) Act(PetPose.Stretch, 1140);
        }

        internal void Enter()
        {
            Touch();
            if (Pose == PetPose.Rest) hover.Start();
        }

        internal void Leave() { hover.Stop(); }

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
