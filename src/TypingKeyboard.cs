using System;
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
            private static readonly Brush Case = ColorBrush(119, 139, 117);
            private static readonly Brush Deck = ColorBrush(178, 187, 163);
            private static readonly Brush Key = ColorBrush(242, 230, 204);
            private static readonly Brush Accent = ColorBrush(216, 225, 192);
            private static readonly Brush Pressed = ColorBrush(255, 198, 115);
            private static readonly Brush KeyEdge = ColorBrush(112, 115, 95);

            private static readonly DependencyProperty LeftDepth = DependencyProperty.Register("LeftDepth", typeof(double),
                typeof(TypingKeyboard), new PropertyMetadata(0.0, (d, e) => ((TypingKeyboard)d).SetDepth(true, (double)e.NewValue)));
            private static readonly DependencyProperty RightDepth = DependencyProperty.Register("RightDepth", typeof(double),
                typeof(TypingKeyboard), new PropertyMetadata(0.0, (d, e) => ((TypingKeyboard)d).SetDepth(false, (double)e.NewValue)));
            private static readonly DoubleAnimationUsingKeyFrames Stroke = MakeStroke();

            private readonly bool motion;
            private readonly ScaleTransform[] wrists = { new ScaleTransform(1, 1, 333, 394), new ScaleTransform(1, 1, 417, 394) };
            private readonly Border[] strikeKeys = new Border[2];
            private readonly TranslateTransform[] keyTravel = { new TranslateTransform(), new TranslateTransform() };

            internal TypingKeyboard(bool motion)
            {
                this.motion = motion;
                IsHitTestVisible = false;
                // Draw the lip and the tapered deck in the original sitting pose's coordinate space.
                Shape(this, "M 294,437 Q 297,433 304,433 L 451,433 Q 457,433 460,437 L 486,468 Q 492,473 487,481 Q 485,487 477,487 L 276,487 Q 267,487 265,480 Q 262,474 266,468 Z", Case, 6);
                Shape(this, "M 295,437 Q 298,433 304,433 L 451,433 Q 456,433 460,437 L 482,466 Q 486,472 478,474 L 274,474 Q 267,473 271,468 Z", Deck, 3.5, KeyEdge);
                for (int i = 0; i < 6; i++)
                    AddKey(304 + 25 * i, 438, 21, 9, i == 0 ? Accent : Key);
                for (int i = 0; i < 7; i++)
                {
                    var key = AddKey(294 + 24 * i, 450, 20, 10, Key);
                    if (i == 2 || i == 4)
                    {
                        int side = i == 2 ? 0 : 1;
                        key.RenderTransform = keyTravel[side];
                        strikeKeys[side] = key;
                    }
                }
                AddKey(282, 463, 25, 8, Accent);
                AddKey(312, 463, 19, 8, Key);
                AddKey(337, 463, 76, 8, Key);
                AddKey(419, 463, 19, 8, Key);
                AddKey(444, 463, 28, 8, Accent);

                var left = new Canvas { RenderTransform = wrists[0] };
                var right = new Canvas { RenderTransform = wrists[1] };
                Children.Add(left);
                Children.Add(right);
                // Open shoulder contours join the original black body.
                Shape(left, "M 329,395 C 325,400 322,410 323,416 C 323,425 331,435 341,439 Q 352,443 358,435 Q 361,428 354,423 L 347,417 Q 341,411 342,403", Ink, 7);
                Shape(right, "M 417,395 Q 428,406 425,418 C 423,428 416,436 408,439 Q 395,442 390,434 Q 387,428 394,423 L 400,419 Q 409,412 408,403", Ink, 7);
                Shape(left, "M 342,438 Q 342,434 346,432", null, 3.5, FaceLine);
                Shape(right, "M 406,438 Q 406,434 402,432", null, 3.5, FaceLine);
            }

            private Border AddKey(double x, double y, double width, double height, Brush fill)
            {
                var key = new Border { Width = width, Height = height, Background = fill, BorderBrush = KeyEdge,
                    BorderThickness = new Thickness(1, 1, 1, 2), CornerRadius = new CornerRadius(3) };
                Canvas.SetLeft(key, x);
                Canvas.SetTop(key, y);
                Children.Add(key);
                return key;
            }

            private static DoubleAnimationUsingKeyFrames MakeStroke()
            {
                var stroke = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(165), FillBehavior = FillBehavior.Stop };
                stroke.KeyFrames.Add(new LinearDoubleKeyFrame(0.2, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                stroke.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(32))));
                stroke.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(65))));
                stroke.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(165)), new KeySpline(.22, 0, .35, 1)));
                stroke.Freeze();
                return stroke;
            }

            internal void Tap(bool left)
            {
                if (motion) BeginAnimation(left ? LeftDepth : RightDepth, Stroke, HandoffBehavior.SnapshotAndReplace);
                else
                {
                    strikeKeys[left ? 0 : 1].Background = Pressed;
                    strikeKeys[left ? 1 : 0].Background = Key;
                }
            }

            internal void SetDepth(bool left, double depth)
            {
                int side = left ? 0 : 1;
                double press = Math.Max(0, Math.Min(1, depth));
                wrists[side].ScaleY = 1 + .30 * press;
                keyTravel[side].Y = 4 * press;
                strikeKeys[side].Background = press >= .55 ? Pressed : Key;
            }
        }
    }
}
