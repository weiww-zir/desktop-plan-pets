using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Xiaohei
{
    internal static class TaskbarHideCheck
    {
        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll", EntryPoint="GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

        private static bool HasTaskbarStyle(IntPtr hwnd)
        {
            const int extendedStyle = -20, appWindow = 0x40000, toolWindow = 0x80;
            const uint owner = 4;
            int style = GetWindowLong(hwnd, extendedStyle);
            return (style & appWindow) != 0 || ((style & toolWindow) == 0 && GetWindow(hwnd, owner) == IntPtr.Zero);
        }

        private static void WaitFor(Func<bool> condition, string message, int seconds = 3)
        {
            var elapsed = Stopwatch.StartNew();
            do
            {
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(delegate { }));
                if (condition()) return;
                Thread.Sleep(15);
            } while (elapsed.Elapsed < TimeSpan.FromSeconds(seconds));
            throw new Exception(message);
        }

        private static IEnumerable<T> Find<T>(DependencyObject parent) where T : FrameworkElement
        {
            if (parent is T) yield return (T)parent;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                foreach (var child in Find<T>(VisualTreeHelper.GetChild(parent, i))) yield return child;
        }

        [STAThread]
        private static int Main(string[] args)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var store = new Store(Path.Combine("work", "taskbar-hide-" + Guid.NewGuid().ToString("N")));
            var pet = new DesktopPet(store) { Title = "TaskbarHideCheck", ShowActivated = false };
            try
            {
                pet.Show();
                var hwnd = new WindowInteropHelper(pet).Handle;
                var start = new Point(pet.Left, pet.Top);
                Func<bool> restored = () => !IsIconic(hwnd) && pet.WindowState == WindowState.Normal &&
                    !pet.ShowInTaskbar && !HasTaskbarStyle(hwnd) && pet.IsVisible &&
                    Math.Abs(pet.Left - start.X) < 1 && Math.Abs(pet.Top - start.Y) < 1;
                WaitFor(restored, "Pet should start on the desktop without a taskbar button");

                var hide = pet.ContextMenu.Items.OfType<MenuItem>().Single(item => (string)item.Header == "隐藏小黑");
                Action minimize = delegate
                {
                    hide.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    WaitFor(() => IsIconic(hwnd) && pet.ShowInTaskbar && HasTaskbarStyle(hwnd),
                        "Pet did not minimize into a shell-eligible taskbar window");
                };
                Action restore = delegate
                {
                    const int systemCommand = 0x112, restoreWindow = 0xf120;
                    SendMessage(hwnd, systemCommand, new IntPtr(restoreWindow), IntPtr.Zero);
                    WaitFor(restored, "Restoring should return the pet to its position and remove its taskbar button");
                };
                minimize();
                if (args.Contains("--wait-for-taskbar-click"))
                {
                    Console.WriteLine("Waiting for Explorer taskbar activation");
                    WaitFor(restored, "Explorer did not restore the pet", 45);
                }
                else restore();
                minimize();
                restore();

                pet.ShowPlanner();
                var planner = pet.OwnedWindows.OfType<Planner>().Single();
                planner.UpdateLayout();
                var add = Find<Button>(planner).Single(b => b.Content as string == "＋ 新建任务");
                add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                planner.UpdateLayout();
                var draft = Find<TextBox>(planner).Single(b => AutomationProperties.GetName(b) == "任务名称");
                draft.Text = "隐藏时保留的草稿";
                minimize();
                WaitFor(() => !planner.IsVisible, "The planner remained visible over the minimized pet");
                restore();
                WaitFor(() => !planner.IsVisible, "Restoring the pet unexpectedly opened its planner");
                minimize();
                pet.ShowPlanner();
                WaitFor(() => restored() && planner.IsVisible && draft.IsVisible && draft.Text == "隐藏时保留的草稿",
                    "The shortcut entry should restore the pet and the previous planner draft");
                Console.WriteLine("Passed minimize, native restore, repeat, position, and planner draft checks.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally { pet.Close(); app.Shutdown(); }
        }
    }
}
