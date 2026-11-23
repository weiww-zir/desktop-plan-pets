using System;
using System.Windows;

namespace Xiaohei
{
    internal static class PetLayout
    {
        private static double Fit(double value, double start, double end)
        {
            return Math.Max(start, Math.Min(value, end));
        }

        internal static Rect Inside(Rect requested, Rect area)
        {
            var w = Math.Min(requested.Width, area.Width);
            var h = Math.Min(requested.Height, area.Height);
            return new Rect(Fit(requested.Left, area.Left, area.Right - w),
                Fit(requested.Top, area.Top, area.Bottom - h), w, h);
        }

        internal static Rect ClearDuringResize(Rect area, Rect pet, Rect start, Rect requested)
        {
            // If an edge reaches the pet, grow into the free space on the same
            // side. Keep the opposite edge anchored for ordinary unobstructed drags.
            var clearance = pet;
            clearance.Inflate(8, 8);
            if (!clearance.IntersectsWith(requested)) return requested;
            Rect best = Rect.Empty;
            Action<Rect> consider = delegate(Rect side)
            {
                var candidate = Inside(requested, side);
                double occupied = candidate.Width * candidate.Height;
                if (best.IsEmpty || occupied > best.Width * best.Height ||
                    (occupied == best.Width * best.Height &&
                     (candidate.Location - requested.Location).LengthSquared < (best.Location - requested.Location).LengthSquared))
                    best = candidate;
            };
            if (start.Right <= pet.Left)
                consider(new Rect(area.TopLeft, new Point(Math.Min(area.Right, Math.Max(start.Right, clearance.Left)), area.Bottom)));
            if (start.Left >= pet.Right)
                consider(new Rect(new Point(Math.Max(area.Left, Math.Min(start.Left, clearance.Right)), area.Top), area.BottomRight));
            if (start.Bottom <= pet.Top)
                consider(new Rect(area.TopLeft, new Point(area.Right, Math.Min(area.Bottom, Math.Max(start.Bottom, clearance.Top)))));
            if (start.Top >= pet.Bottom)
                consider(new Rect(new Point(area.Left, Math.Max(area.Top, Math.Min(start.Top, clearance.Bottom))), area.BottomRight));
            return best.IsEmpty ? requested : best;
        }

        internal static Rect AbovePet(Rect area, Rect avoid, Size requested, Size minimum, double gap)
        {
            var pet = Rect.Intersect(avoid, area);
            if (!pet.IsEmpty && pet.Width > 0 && pet.Height > 0)
            {
                double width = Math.Min(area.Width, requested.Width);
                double x = Fit(pet.Left + (pet.Width - width) / 2, area.Left, area.Right - width);
                double roomAbove = pet.Top - gap - area.Top;
                if (roomAbove >= minimum.Height)
                {
                    double height = Math.Min(requested.Height, roomAbove);
                    return new Rect(x, pet.Top - gap - height, width, height);
                }
                if (area.Bottom - pet.Bottom - gap >= requested.Height)
                    return new Rect(x, pet.Bottom + gap, width, requested.Height);
            }
            return Beside(area, avoid, requested, minimum, gap);
        }

        // Prefer a side that preserves the requested window size. If none does,
        // keep the largest usable rectangle that respects the normal minimum.
        internal static Rect Beside(Rect area, Rect avoid, Size requested, Size minimum, double gap)
        {
            var pet = Rect.Intersect(avoid, area);
            if (pet.IsEmpty || pet.Width == 0 || pet.Height == 0)
                return new Rect(area.Location, new Size(Math.Min(requested.Width, area.Width),
                                                         Math.Min(requested.Height, area.Height)));
            double leftEnd = Math.Max(area.Left, pet.Left - gap);
            double rightStart = Math.Min(area.Right, pet.Right + gap);
            double topEnd = Math.Max(area.Top, pet.Top - gap);
            double bottomStart = Math.Min(area.Bottom, pet.Bottom + gap);
            var strips = new[] {
                new Rect(area.Left, area.Top, leftEnd - area.Left, area.Height),
                new Rect(rightStart, area.Top, area.Right - rightStart, area.Height),
                new Rect(area.Left, area.Top, area.Width, topEnd - area.Top),
                new Rect(area.Left, bottomStart, area.Width, area.Bottom - bottomStart)
            };
            var best = Rect.Empty;
            double bestArea = -1;
            bool bestMeetsMinimum = false;
            for (int i = 0; i < strips.Length; i++)
            {
                var strip = strips[i];
                double w = Math.Min(strip.Width, requested.Width);
                double h = Math.Min(strip.Height, requested.Height);
                if (w <= 0 || h <= 0) continue;
                bool meetsMinimum = w >= minimum.Width && h >= minimum.Height;
                if (bestMeetsMinimum && !meetsMinimum) continue;
                if (meetsMinimum == bestMeetsMinimum && w * h <= bestArea) continue;
                double x = i == 0 ? strip.Right - w : i == 1 ? strip.Left :
                    Fit(pet.Left + (pet.Width - w) / 2, strip.Left, strip.Right - w);
                double y = i == 2 ? strip.Bottom - h : i == 3 ? strip.Top :
                    Fit(pet.Bottom - h, strip.Top, strip.Bottom - h);
                best = new Rect(x, y, w, h);
                bestArea = w * h;
                bestMeetsMinimum = meetsMinimum;
            }
            return best;
        }
    }
}
