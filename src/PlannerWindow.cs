using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace Xiaohei
{
    internal sealed partial class Planner
    {
        private bool closingPlanner;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll")]
        private static extern bool IsWindowEnabled(IntPtr hwnd);

        private void DismissIfOutside()
        {
            if (closingPlanner || !IsVisible || IsActive || OwnedWindows.Cast<Window>().Any(w => w.IsVisible)) return;
            var handle = new WindowInteropHelper(this).Handle;
            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero || !IsWindowEnabled(handle)) return;
            if (Owner != null && foreground == new WindowInteropHelper(Owner).Handle) return;
            for (var w = foreground; w != IntPtr.Zero; w = GetWindow(w, 4))
                if (w == handle) return;
            DismissCalendar();
        }

        internal void DismissCalendar()
        {
            // Keep a plan with no parent title in its editor until the next open.
            if (draftRow != null && String.IsNullOrWhiteSpace(draftTitle.Text) && !String.IsNullOrWhiteSpace(draftNotes.Text))
                Hide();
            else Close();
        }

        private Grid AddResizeHandles(Grid surface)
        {
            var handles = new Grid();
            surface.Children.Add(handles);
            var hitArea = new FrameworkElementFactory(typeof(Border));
            hitArea.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            var invisibleHandle = new ControlTemplate(typeof(Thumb)) { VisualTree = hitArea };
            // Horizontal/vertical direction: -1 for left/top, +1 for right/bottom.
            for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    if (x == 0 && y == 0) continue;
                    bool corner = x != 0 && y != 0;
                    bool grip = x == 1 && y == 1;
                    var thumb = new Thumb {
                        Template = grip ? GripSkin : invisibleHandle, Focusable = grip,
                        HorizontalAlignment = x == 0 ? HorizontalAlignment.Stretch : x < 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                        VerticalAlignment = y == 0 ? VerticalAlignment.Stretch : y < 0 ? VerticalAlignment.Top : VerticalAlignment.Bottom,
                        Cursor = x == 0 ? Cursors.SizeNS : y == 0 ? Cursors.SizeWE : x == y ? Cursors.SizeNWSE : Cursors.SizeNESW
                    };
                    if (x != 0) thumb.Width = corner ? 26 : 8;
                    if (y != 0) thumb.Height = corner ? 22 : 8;
                    Panel.SetZIndex(thumb, corner ? 1 : 0);
                    AutomationProperties.SetName(thumb, grip ? "调整窗口大小" :
                        (x < 0 ? "左" : x > 0 ? "右" : "") + (y < 0 ? "上" : y > 0 ? "下" : "") + (corner ? "角" : "边") + "调整大小");
                    if (grip)
                    {
                        const string help = "拖动任一边或角调整大小，双击自动适配";
                        thumb.ToolTip = help;
                        AutomationProperties.SetHelpText(thumb, help + "，也可使用方向键");
                    }
                    WireResize(thumb, x, y);
                    handles.Children.Add(thumb);
                }
            return handles;
        }

        private Rect FrameBounds { get { return new Rect(Left, Top, Width, Height); } }

        private void WireResize(Thumb thumb, int x, int y)
        {
            Rect start = Rect.Empty;
            Point handleOrigin = new Point();
            bool changed = false;
            Action finish = delegate {
                if (changed) SaveFrameSize();
                start = Rect.Empty;
                changed = false;
            };
            thumb.DragStarted += delegate {
                start = FrameBounds;
                handleOrigin = thumb.PointToScreen(new Point());
            };
            thumb.DragDelta += delegate(object sender, DragDeltaEventArgs e) {
                var delta = new Vector(e.HorizontalChange, e.VerticalChange);
                // Thumb reports motion relative to its original local press point.
                // Account for the handle itself moving as the window resizes.
                if (!start.IsEmpty)
                    delta += PresentationSource.FromVisual(thumb).CompositionTarget.TransformFromDevice
                        .Transform(thumb.PointToScreen(new Point()) - handleOrigin);
                changed |= ResizeFrame(start.IsEmpty ? FrameBounds : start, x, y, delta);
            };
            thumb.DragCompleted += delegate { finish(); };
            if (!thumb.Focusable) return;
            thumb.KeyDown += delegate(object sender, KeyEventArgs e) {
                var delta = new Vector();
                switch (e.Key)
                {
                    case Key.Left: delta.X = -12; break;
                    case Key.Right: delta.X = 12; break;
                    case Key.Up: delta.Y = -12; break;
                    case Key.Down: delta.Y = 12; break;
                    default: return;
                }
                changed |= ResizeFrame(FrameBounds, x, y, delta);
                e.Handled = true;
            };
            thumb.KeyUp += delegate { finish(); };
            thumb.LostKeyboardFocus += delegate { if (!thumb.IsDragging) finish(); };
        }

        private bool ResizeFrame(Rect start, int x, int y, Vector delta)
        {
            var source = PresentationSource.FromVisual(this);
            var screen = Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
            var area = Rect.Transform(new Rect(screen.X, screen.Y, screen.Width, screen.Height),
                source.CompositionTarget.TransformFromDevice);
            bool onScreen = area.IntersectsWith(start);
            if (onScreen) { MaxWidth = area.Width; MaxHeight = area.Height; }
            double width = Math.Max(MinWidth, Math.Min(MaxWidth, start.Width + Math.Round(x * delta.X)));
            double height = Math.Max(MinHeight, Math.Min(MaxHeight, start.Height + Math.Round(y * delta.Y)));
            var bounds = new Rect(x < 0 ? start.Right - width : start.Left,
                y < 0 ? start.Bottom - height : start.Top, width, height);
            var pet = Owner as DesktopPet;
            if (onScreen)
            {
                bounds = PetLayout.Inside(bounds, area);
                if (pet != null)
                    bounds = PetLayout.ClearDuringResize(area, new Rect(pet.Left, pet.Top, pet.Width, pet.Height), start, bounds);
            }
            if (FrameBounds == bounds) return false;
            if (pet != null) pet.SetBounds(this, bounds);
            else { Width = bounds.Width; Height = bounds.Height; Left = bounds.Left; Top = bounds.Top; }
            return true;
        }

        private void SaveFrameSize()
        {
            var previous = new Size(store.PlannerWidth, store.PlannerHeight);
            if (previous.Width == Width && previous.Height == Height) return;
            store.PlannerWidth = Width;
            store.PlannerHeight = Height;
            if (!Persist(this)) { store.PlannerWidth = previous.Width; store.PlannerHeight = previous.Height; }
        }

        private void InstallFrameGestures(Border titleBar, Button close, Grid handles)
        {
            Point? dragStart = null;
            titleBar.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ClickCount == 1) dragStart = e.GetPosition(titleBar);
            };
            titleBar.MouseLeftButtonUp += delegate { dragStart = null; };
            titleBar.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (e.LeftButton != MouseButtonState.Pressed) { dragStart = null; return; }
                if (!dragStart.HasValue) return;
                var offset = e.GetPosition(titleBar) - dragStart.Value;
                if (Math.Abs(offset.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(offset.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                dragStart = null;
                DragMove();
            };
            PreviewMouseDoubleClick += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ChangedButton != MouseButton.Left) return;
                var source = e.OriginalSource as DependencyObject;
                var content = source as ContentElement;
                if (content != null) source = ContentOperations.GetParent(content);
                if (!(source is Visual)) return;
                if (close == source || close.IsAncestorOf(source)) return;
                var point = e.GetPosition(this);
                bool onEdge = point.X <= 6 || point.Y <= 6 || point.X >= ActualWidth - 6 || point.Y >= ActualHeight - 6;
                if (!onEdge && !handles.IsAncestorOf(source) &&
                    source != titleBar && !titleBar.IsAncestorOf(source)) return;
                e.Handled = true;
                dragStart = null;
                AutoFit();
            };
        }

        private void AutoFit()
        {
            var pet = Owner as DesktopPet;
            if (pet == null) return;
            UpdateLayout();
            double chromeHeight = ActualHeight - scroll.ActualHeight;
            sections.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            // Long titles wrap or expose their detail tooltip; they should not
            // expand a desktop planner across the entire screen.
            double width = Math.Max(356, Math.Min(452, Math.Ceiling(sections.DesiredSize.Width + 2)));
            sections.Measure(new Size(width - 2, double.PositiveInfinity));
            double height = Math.Max(270, Math.Ceiling(chromeHeight + sections.DesiredSize.Height + 2));
            sections.InvalidateMeasure();
            pet.FitPlanner(this, new Size(width, height));
            UpdateLayout();
            scroll.ScrollToTop();
            SaveFrameSize();
        }
    }
}
