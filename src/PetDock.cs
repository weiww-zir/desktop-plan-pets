using System;
using System.Windows;

namespace Xiaohei
{
    internal static class PetDock
    {
        internal static Point Place(Rect desired, Rect work)
        {
            return new Point(
                Math.Max(work.Left, Math.Min(desired.Left, work.Right - desired.Width)),
                Math.Max(work.Top, Math.Min(desired.Top, work.Bottom - desired.Height)));
        }

        internal static Size VisibleSize(double preferredWidth, Rect work)
        {
            const double aspect = 148.0 / 152;
            double width = Math.Min(preferredWidth, Math.Min(work.Width, work.Height / aspect));
            return new Size(width, width * aspect);
        }
    }
}
