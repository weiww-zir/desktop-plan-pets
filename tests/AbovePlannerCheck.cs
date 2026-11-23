using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Xiaohei
{
    internal static class AbovePlannerCheck
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Pixels { internal int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr handle, out Pixels rect);

        private static Rect Frame(Window window)
        {
            Pixels rect;
            if (!GetWindowRect(new WindowInteropHelper(window).Handle, out rect)) throw new Exception("No window rectangle");
            return Rect.Transform(new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top),
                PresentationSource.FromVisual(window).CompositionTarget.TransformFromDevice);
        }

        private static IEnumerable<T> Find<T>(DependencyObject root) where T : FrameworkElement
        {
            if (root is T) yield return (T)root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var value in Find<T>(VisualTreeHelper.GetChild(root, i))) yield return value;
        }

        private static void Check(bool ok, string message)
        {
            if (!ok) throw new Exception(message);
            Console.WriteLine("PASS " + message);
        }

        private static void Settle()
        {
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(delegate { }));
        }

        private static Planner Open(DesktopPet pet)
        {
            pet.ShowPlanner();
            Settle();
            return pet.OwnedWindows.OfType<Planner>().Single(p => p.IsVisible);
        }

        private static void Click(Planner window, string label)
        {
            var button = Find<Button>(window).Single(b => b.Content as string == label);
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Settle();
        }

        private static void Move(DesktopPet pet, double x, double y)
        {
            pet.Left = x;
            pet.Top = y;
            Settle();
        }

        private static void Verify(Window window, DesktopPet pet, Rect area, bool above, string scenario)
        {
            var panel = Frame(window);
            var cat = Frame(pet);
            var tolerance = area;
            tolerance.Inflate(1.1, 1.1);
            var overlap = Rect.Intersect(panel, cat);
            Check(tolerance.Contains(panel) && (overlap.IsEmpty || overlap.Width * overlap.Height == 0),
                scenario + " stays in the work area and clear of the pet: " + panel);
            if (!above)
            {
                Check(panel.Bottom > cat.Top && panel.Height >= 269, scenario + " uses a usable alternative");
                return;
            }
            double centered = cat.Left + (cat.Width - panel.Width) / 2;
            double expectedX = Math.Max(area.Left, Math.Min(centered, area.Right - panel.Width));
            Check(Math.Abs(panel.Left - expectedX) <= 1.5 && Math.Abs((cat.Top - panel.Bottom) - 8) <= 1.5,
                scenario + " horizontally aligned, with 8 DIP above-pet spacing");
        }

        [STAThread]
        private static int Main()
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                string folder = Path.Combine("work", "above-check-" + Guid.NewGuid().ToString("N"));
                var today = new DateTime(2026, 11, 23, 9, 0, 0);
                var store = new Store(folder, () => today);
                store.PlannerWidth = 480;
                store.PlannerHeight = 480;
                for (int i = 0; i < 17; i++)
                    store.Tasks.Add(new TaskItem { Id = Guid.NewGuid().ToString("N"),
                        Day = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Title = "定位测试任务 " + i });
                store.Save();
                var pet = new DesktopPet(store) { ShowActivated = false };
                pet.Show();
                Settle();
                var screen = Forms.Screen.FromHandle(new WindowInteropHelper(pet).Handle).WorkingArea;
                var area = Rect.Transform(new Rect(screen.X, screen.Y, screen.Width, screen.Height),
                    PresentationSource.FromVisual(pet).CompositionTarget.TransformFromDevice);
                double center = area.Left + (area.Width - pet.Width) / 2;
                double bottom = area.Bottom - pet.Height - 12;
                Move(pet, area.Right - pet.Width - 8, bottom);
                var planner = Open(pet);
                Verify(planner, pet, area, true, "bottom-right open");
                planner.Close();
                Move(pet, center, bottom);
                planner = Open(pet);
                Verify(planner, pet, area, true, "bottom-center fresh open");
                planner.Close();
                Move(pet, center, area.Top + 326);
                planner = Open(pet);
                Verify(planner, pet, area, true, "less than preferred height above");
                var scroll = Find<ScrollViewer>(planner).OrderByDescending(s => s.ActualHeight).First();
                Check(planner.ActualHeight >= 269 && planner.ActualHeight < 480 && scroll.ScrollableHeight > 0,
                    "partially fitting planner retains scrolling and minimum height");
                var header = Find<TextBlock>(planner).Single(t => t.Text == "小黑手帐");
                planner.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) {
                    RoutedEvent = Control.PreviewMouseDoubleClickEvent, Source = header
                });
                Settle();
                Verify(planner, pet, area, true, "title double-click auto-fit");
                Check(planner.ActualWidth <= 453 && new Store(folder).PlannerWidth <= 453, "double-click changed and saved fitted width");
                planner.Close();
                Move(pet, area.Left + 55, area.Top + 65);
                planner = Open(pet);
                Verify(planner, pet, area, false, "above strip shorter than minimum");
                Click(planner, "＋ 新建任务");
                Click(planner, "添加子任务");
                var draft = Find<TextBox>(planner).Single(t => AutomationProperties.GetName(t) == "子任务（每行一项）");
                draft.Text = "保留尚未命名的草稿";
                planner.DismissCalendar();
                Settle();
                Check(!planner.IsVisible, "untitled draft is hidden");
                Move(pet, center, bottom);
                var reopened = Open(pet);
                Check(reopened == planner && draft.IsVisible && draft.Text == "保留尚未命名的草稿",
                    "hidden draft instance and text retained");
                Verify(reopened, pet, area, true, "reopening hidden draft at new pet location");
                Click(reopened, "取消");
                pet.Close();
                app.Shutdown();
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
    }
}
