using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;

namespace Xiaohei
{
    internal static class PetApp
    {
        internal static void ShowError(Window owner, string caption, string message)
        {
            if (!Dispatcher.CurrentDispatcher.HasShutdownStarted)
            {
                try
                {
                    Planner.ShowPlannerPrompt(owner, caption, message, MessageBoxButton.OK);
                    return;
                }
                catch (Exception)
                {
                    // WPF may be unable to create a window after a startup or rendering failure.
                }
            }
            if (owner == null) MessageBox.Show(message, caption, MessageBoxButton.OK, MessageBoxImage.Error);
            else MessageBox.Show(owner, message, caption, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        [STAThread]
        private static void Main(string[] args)
        {
            bool first;
            using (Mutex instance = new Mutex(true, @"Local\LuoXiaoheiPlanner.23E819", out first))
            using (EventWaitHandle show = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\LuoXiaoheiPlanner.Show.23E819"))
            {
                if (!first) { show.Set(); return; }
                try
                {
                    var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                    var tipStyle = new Style(typeof(ToolTip));
                    tipStyle.Setters.Add(new Setter(Control.TemplateProperty, PetMenu.HintTemplate));
                    tipStyle.Setters.Add(new Setter(Control.ForegroundProperty, PetMenu.PopupInk));
                    app.Resources[typeof(ToolTip)] = tipStyle;
                    PetMenu.StyleTextEditingMenus();
                    var pet = new DesktopPet(new Store());
                    if (args.Contains("--planner", StringComparer.OrdinalIgnoreCase))
                        pet.ContentRendered += delegate { pet.Dispatcher.BeginInvoke(new Action(pet.ShowPlanner)); };
                    var listener = ThreadPool.RegisterWaitForSingleObject(show, delegate
                    {
                        if (!app.Dispatcher.HasShutdownStarted)
                            app.Dispatcher.BeginInvoke(new Action(pet.ShowPlanner));
                    }, null, Timeout.Infinite, false);
                    try { app.Run(pet); }
                    finally { listener.Unregister(null); }
                }
                catch (Exception error)
                {
                    ShowError(null, "小黑手帐", error.Message);
                }
                finally { instance.ReleaseMutex(); }
            }
        }
    }

    internal sealed class DesktopPet : Window
    {
        private readonly Store store;
        private readonly PetActor actor = new PetActor();
        private Planner planner;
        private Forms.NotifyIcon tray;
        private ContextMenu trayMenu;
        private KeyboardActivity input;
        private IntPtr sessionWindow;
        private bool sessionLocked, sessionDisconnected, suspended;
        [DllImport("wtsapi32.dll")]
        private static extern bool WTSRegisterSessionNotification(IntPtr window, uint flags);
        [DllImport("wtsapi32.dll")]
        private static extern bool WTSUnRegisterSessionNotification(IntPtr window);
        private readonly DispatcherTimer reminderTimer = new DispatcherTimer();
        private DateTime previousReminderTime;
        private bool reminderSaveFailed;
        private Point pressScreen, pressWindow;
        private bool pressed, moving;
        private int wheelRemainder;
        private const string Hint = "点击打开手帐，按住拖动。右键选择桌宠大小，Ctrl + 滚轮可微调。";
        private static readonly int[] PetSizes = { 64, 92, 120, 152, 192 };
        private static readonly string[] SizeLabels = { "迷你（64）", "小巧（92，默认）", "适中（120）", "原始（152）", "大号（192）" };

        internal DesktopPet(Store store)
        {
            this.store = store;
            Title = "小黑手帐";
            Width = store.PetWidth;
            Height = Width * 148.0 / 152;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Cursor = Cursors.Hand;
            AutomationProperties.SetName(this, "罗小黑桌面宠物");
            AutomationProperties.SetHelpText(this, Hint);
            Content = actor;
            MouseEnter += delegate { actor.Enter(); };
            MouseLeave += delegate { actor.Leave(); };
            var menu = new ContextMenu();
            var open = new MenuItem { Header = "打开日历手帐" };
            open.Click += delegate { ShowPlanner(); };
            menu.Items.Add(open);
            menu.Items.Add(CreateSizeMenu());
            var keyLabels = new MenuItem { Header = "显示按下的键", IsCheckable = true, IsChecked = !store.HideKeyLabels };
            keyLabels.Click += delegate
            {
                bool previous = store.HideKeyLabels;
                store.HideKeyLabels = !keyLabels.IsChecked;
                actor.EndTyping();
                try { store.Save(); }
                catch (Exception error)
                {
                    store.HideKeyLabels = previous;
                    keyLabels.IsChecked = !previous;
                    PetApp.ShowError(this, Title, "按键显示设置未能保存。\n" + error.Message);
                }
            };
            menu.Items.Add(keyLabels);
            var greet = new MenuItem { Header = "让小黑喵一声" };
            greet.Click += delegate { actor.Meow(); };
            menu.Items.Add(greet);
            var kittens = new MenuItem { Header = "尾巴变小猫" };
            kittens.Click += delegate { actor.SplitTail(); };
            menu.Items.Add(kittens);
            var spirits = new MenuItem { Header = "白色小精灵围绕" };
            spirits.Click += delegate { actor.WatchSpirits(); };
            menu.Items.Add(spirits);
            var exit = new MenuItem { Header = "退出小黑" };
            exit.Click += delegate { Close(); };
            menu.Items.Add(exit);
            PetMenu.Apply(menu, this);
            ContextMenu = menu;
            SourceInitialized += delegate
            {
                var source = (HwndSource)PresentationSource.FromVisual(this);
                source.AddHook(OnDisplayMessage);
                input = KeyboardActivity.Register(source.Handle, actor);
                if (WTSRegisterSessionNotification(source.Handle, 0)) sessionWindow = source.Handle;
                RestoreLocation();
            };
            IsVisibleChanged += delegate { if (!IsVisible && input != null) input.Reset(); };
            previousReminderTime = store.Now;
            store.Changed += UpdateBadge;
            reminderTimer.Tick += delegate { RefreshReminders(); };
            Loaded += delegate { CreateTray(); RefreshReminders(); };
            LocationChanged += delegate { KeepClear(planner); };
            SizeChanged += delegate { KeepClear(planner); };
            PreviewMouseLeftButtonDown += Press;
            PreviewMouseMove += MovePet;
            PreviewMouseLeftButtonUp += Release;
            PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                if (pressed || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
                AdjustPetWheel(e.Delta);
                e.Handled = true;
            };
            LostMouseCapture += delegate { pressed = false; };
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Enter || e.Key == Key.Space) { e.Handled = true; ShowPlanner(); }
            };
        }

        private MenuItem CreateSizeMenu()
        {
            var sizes = new MenuItem { Header = "桌宠大小" };
            AutomationProperties.SetHelpText(sizes, "选择大小，或按住 Ctrl 在小黑身上滚动鼠标滚轮");
            for (int i = 0; i < PetSizes.Length; i++)
            {
                int width = PetSizes[i];
                var item = new MenuItem { Header = SizeLabels[i], Tag = width, IsCheckable = true };
                item.Click += delegate
                {
                    wheelRemainder = 0;
                    ChangeSize(width);
                };
                sizes.Items.Add(item);
            }
            sizes.SubmenuOpened += delegate
            {
                foreach (MenuItem item in sizes.Items) item.IsChecked = (int)item.Tag == store.PetWidth;
            };
            return sizes;
        }

        private void AdjustPetWheel(int delta)
        {
            // Some high-resolution devices send several small wheel messages per notch.
            wheelRemainder += delta;
            int change = wheelRemainder / 30;
            wheelRemainder %= 30;
            if (change != 0) ChangeSize(store.PetWidth + change);
        }

        private void UpdateBadge()
        {
            actor.ShowPendingCount(store.PendingCount(store.Now));
        }

        internal void RefreshReminders()
        {
            var now = store.Now;
            bool crossedBoundary = now.Date != previousReminderTime.Date ||
                (now.Hour >= 18) != (previousReminderTime.Hour >= 18);
            reminderTimer.Stop();
            bool changed = false;
            try
            {
                changed = store.ApplySchedule(now);
                reminderSaveFailed = false;
            }
            catch (Exception error)
            {
                if (!reminderSaveFailed && tray != null)
                    tray.ShowBalloonTip(6000, Title, "顺延未能保存，稍后会重试。\n" + error.Message, Forms.ToolTipIcon.Warning);
                reminderSaveFailed = true;
            }
            if (planner != null && (crossedBoundary || changed)) planner.RefreshSchedule(previousReminderTime);
            previousReminderTime = now;
            UpdateBadge();
            var next = now.Hour < 18 ? now.Date.AddHours(18) : now.Date.AddDays(1);
            // Check the local clock after sleep or a clock adjustment, and wake
            // precisely on the normal 18:00 and midnight transitions.
            reminderTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, Math.Min(60000, (next - now).TotalMilliseconds)));
            reminderTimer.Start();
        }

        private void CreateTray()
        {
            trayMenu = new ContextMenu { Placement = PlacementMode.MousePoint, PlacementTarget = this };
            var open = new MenuItem { Header = "打开日历手帐" };
            open.Click += delegate { ShowPlanner(); };
            var reset = new MenuItem { Header = "把小黑移回主屏幕" };
            reset.Click += delegate
            {
                PlaceAtCorner(WorkingArea(Forms.Screen.PrimaryScreen));
                SavePosition();
            };
            var exit = new MenuItem { Header = "退出小黑" };
            exit.Click += delegate { Close(); };
            trayMenu.Items.Add(open);
            trayMenu.Items.Add(CreateSizeMenu());
            trayMenu.Items.Add(reset);
            trayMenu.Items.Add(exit);
            PetMenu.ApplySurface(trayMenu, this);
            tray = new Forms.NotifyIcon
            {
                Text = "小黑手帐 · 双击打开日历", Visible = false,
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(Forms.Application.ExecutablePath)
            };
            tray.MouseUp += delegate(object sender, Forms.MouseEventArgs e)
            {
                if (e.Button != Forms.MouseButtons.Right) return;
                Activate();
                trayMenu.IsOpen = true;
            };
            tray.DoubleClick += delegate { ShowPlanner(); };
            tray.Visible = true;
            if (store.RecoveredFromBackup)
                tray.ShowBalloonTip(6000, Title, "已从上一次备份恢复手帐，请核对最近的任务。", Forms.ToolTipIcon.Info);
        }

        private Matrix FromPixels()
        {
            return PresentationSource.FromVisual(this).CompositionTarget.TransformFromDevice;
        }

        private Rect WorkingArea(Forms.Screen screen)
        {
            var bounds = screen.WorkingArea;
            return Rect.Transform(new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height), FromPixels());
        }

        private static double Fit(double value, double min, double max)
        {
            return Math.Max(min, Math.Min(value, max));
        }

        private void PlaceAtCorner(Rect area)
        {
            var size = PetDock.VisibleSize(store.PetWidth, area);
            PlacePet(new Point(area.Right - size.Width - 26, area.Bottom - size.Height - 22), area);
        }

        private void PlacePet(Point desired, Rect area)
        {
            var size = PetDock.VisibleSize(store.PetWidth, area);
            var point = PetDock.Place(new Rect(desired, size), area);
            Width = size.Width;
            Height = size.Height;
            Left = point.X;
            Top = point.Y;
        }

        private IntPtr OnDisplayMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == KeyboardActivity.InputMessage && input != null && IsVisible &&
                !sessionLocked && !sessionDisconnected && !suspended)
            {
                input.Receive(lParam, !store.HideKeyLabels);
                // Leave the message unhandled so Windows can clean up foreground raw input.
                return IntPtr.Zero;
            }
            const int wmDisplayChange = 0x007E, wmSettingChange = 0x001A, spiSetWorkArea = 0x002F;
            const int wmTimeChange = 0x001E, wmPowerBroadcast = 0x0218;
            const int wmInputDeviceChange = 0x00FE, wmSessionChange = 0x02B1;
            long reason = wParam.ToInt64();
            if (message == wmSessionChange || message == wmPowerBroadcast)
            {
                if (message == wmSessionChange)
                {
                    if (reason == 7 || reason == 6) sessionLocked = true;
                    if (reason == 8 || reason == 5) sessionLocked = false;
                    if (reason == 2 || reason == 4) sessionDisconnected = true;
                    if (reason == 1 || reason == 3) sessionDisconnected = false;
                }
                else if (reason == 4) suspended = true;
                else if (reason == 7 || reason == 18) suspended = false;
                if (sessionLocked || sessionDisconnected || suspended)
                {
                    if (input != null) input.Reset();
                    actor.SetSessionActive(false);
                }
                else actor.SetSessionActive(true);
            }
            if (message == wmInputDeviceChange && reason == 2 && input != null) input.Reset();
            if (message == wmTimeChange || (message == wmPowerBroadcast && (wParam.ToInt64() == 7 || wParam.ToInt64() == 18)))
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate { if (IsVisible) RefreshReminders(); }));
            if (message == wmDisplayChange || (message == wmSettingChange && wParam.ToInt64() == spiSetWorkArea))
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(delegate
                {
                    if (IsLoaded) PlacePet(new Point(Left, Top), WorkingArea(Forms.Screen.FromHandle(hwnd)));
                }));
            }
            return IntPtr.Zero;
        }

        private void ChangeSize(double desired)
        {
            double width = Fit(Math.Round(desired), 50, 240);
            if (width == store.PetWidth) return;
            var screen = Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
            var area = WorkingArea(screen);
            var previous = new Rect(Left, Top, Width, Height);
            double oldWidth = store.PetWidth;
            double oldLeft = store.PetLeft, oldTop = store.PetTop;
            store.PetWidth = width;
            var size = PetDock.VisibleSize(width, area);
            PlacePet(new Point(previous.Left + (previous.Width - size.Width) / 2, previous.Bottom - size.Height), area);
            store.PetLeft = Left;
            store.PetTop = Top;
            try { store.Save(); }
            catch (Exception error)
            {
                store.PetWidth = oldWidth;
                Width = previous.Width;
                Height = previous.Height;
                Left = previous.Left;
                Top = previous.Top;
                store.PetLeft = oldLeft;
                store.PetTop = oldTop;
                PetApp.ShowError(this, Title, "桌宠大小未能保存。\n" + error.Message);
            }
        }

        private void RestoreLocation()
        {
            if (!double.IsNaN(store.PetLeft) && !double.IsNaN(store.PetTop))
            {
                var saved = new Rect(store.PetLeft, store.PetTop, Width, Height);
                Rect closest = WorkingArea(Forms.Screen.PrimaryScreen);
                double leastMove = double.PositiveInfinity;
                foreach (var screen in Forms.Screen.AllScreens)
                {
                    var area = WorkingArea(screen);
                    var pet = new Rect(saved.Location, PetDock.VisibleSize(store.PetWidth, area));
                    double distance = (PetDock.Place(pet, area) - saved.Location).LengthSquared;
                    if (distance < leastMove) { leastMove = distance; closest = area; }
                }
                PlacePet(saved.Location, closest);
                return;
            }
            PlaceAtCorner(WorkingArea(Forms.Screen.PrimaryScreen));
        }

        private Point MouseScreenPosition()
        {
            var mouse = Forms.Cursor.Position;
            return FromPixels().Transform(new Point(mouse.X, mouse.Y));
        }

        private void Press(object sender, MouseButtonEventArgs e)
        {
            actor.Touch();
            actor.Leave();
            pressScreen = MouseScreenPosition();
            pressWindow = new Point(Left, Top);
            moving = false;
            pressed = CaptureMouse();
            e.Handled = true;
        }

        private void MovePet(object sender, MouseEventArgs e)
        {
            if (e.MouseDevice != null && (IsMouseOver || pressed)) actor.Touch();
            if (!pressed || e.LeftButton != MouseButtonState.Pressed) return;
            var cursor = MouseScreenPosition();
            var offset = cursor - pressScreen;
            if (!moving && Math.Abs(offset.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(offset.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            moving = true;
            PlacePet(pressWindow + offset, WorkingArea(Forms.Screen.FromPoint(Forms.Cursor.Position)));
        }

        private void Release(object sender, MouseButtonEventArgs e)
        {
            if (!pressed) return;
            pressed = false;
            ReleaseMouseCapture();
            if (moving)
            {
                var cursor = MouseScreenPosition();
                PlacePet(pressWindow + (cursor - pressScreen), WorkingArea(Forms.Screen.FromPoint(Forms.Cursor.Position)));
                SavePosition();
            }
            else if (planner != null && planner.IsVisible) planner.DismissCalendar();
            else ShowPlanner();
            e.Handled = true;
        }

        private void SavePosition()
        {
            store.PetLeft = Left;
            store.PetTop = Top;
            try { store.Save(); }
            catch (Exception error)
            {
                PetApp.ShowError(this, Title, "小黑的位置未能保存。\n" + error.Message);
            }
        }

        private bool arrangingWindows;
        internal DateTime? ManualHeightDay;
        internal double ManualDayContentHeight;

        internal void WatchPlannerWindow(Window window)
        {
            window.LocationChanged += delegate { KeepClear(window); };
            window.SizeChanged += delegate { KeepClear(window, true); };
            window.Loaded += delegate { KeepClear(window); };
        }

        private void KeepClear(Window window, bool resizing = false)
        {
            if (arrangingWindows || window == null || !window.IsVisible ||
                window.WindowState == WindowState.Minimized) return;
            var pet = new Rect(Left, Top, Width, Height);
            var bounds = new Rect(window.Left, window.Top, window.Width, window.Height);
            var area = WorkingArea(Forms.Screen.FromHandle(new WindowInteropHelper(window).Handle));
            if (pet.IntersectsWith(bounds))
                PlaceClearOfPet(window, area);
            else if (resizing && bounds.IntersectsWith(area) && !area.Contains(bounds))
            {
                var w = Math.Min(bounds.Width, area.Width);
                var h = Math.Min(bounds.Height, area.Height);
                var clamped = new Rect(Fit(bounds.Left, area.Left, area.Right - w),
                    Fit(bounds.Top, area.Top, area.Bottom - h), w, h);
                if (clamped.IntersectsWith(pet)) PlaceClearOfPet(window, area);
                else SetBounds(window, clamped);
            }
        }

        private void PlaceClearOfPet(Window window, Rect area)
        {
            var size = new Size(window.Width, window.Height);
            var calendar = window as Planner;
            if (calendar != null) { FitPlanner(calendar, size); return; }
            var pet = new Rect(Left, Top, Width, Height);
            var min = new Size(window.MinWidth, window.MinHeight);
            var bounds = PetLayout.Beside(area, pet, size, min, 8);
            if (bounds.IsEmpty) return;
            SetBounds(window, bounds);
        }

        internal void FitPlanner(Planner window, Size size)
        {
            var area = WorkingArea(Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle));
            double topGap = Math.Min(20, Math.Max(0, area.Height - 270));
            area = new Rect(area.Left, area.Top + topGap, area.Width, area.Height - topGap);
            var minimum = new Size(Math.Min(300, area.Width), Math.Min(270, area.Height));
            var requested = new Size(Math.Max(minimum.Width, size.Width), Math.Max(minimum.Height, size.Height));
            var bounds = PetLayout.AbovePet(area, new Rect(Left, Top, Width, Height), requested, minimum, 8);
            if (bounds.IsEmpty) return;
            window.MaxWidth = area.Width;
            window.MaxHeight = area.Height;
            SetBounds(window, bounds);
            window.MinWidth = Math.Min(minimum.Width, bounds.Width);
            window.MinHeight = Math.Min(minimum.Height, bounds.Height);
        }

        internal void SetBounds(Window window, Rect bounds)
        {
            arrangingWindows = true;
            try
            {
                window.MinWidth = Math.Min(window.MinWidth, bounds.Width);
                window.MinHeight = Math.Min(window.MinHeight, bounds.Height);
                window.Width = bounds.Width;
                window.Height = bounds.Height;
                window.Left = bounds.Left;
                window.Top = bounds.Top;
            }
            finally { arrangingWindows = false; }
        }

        internal void ShowPlanner()
        {
            RefreshReminders();
            actor.Meow();
            if (planner == null)
            {
                planner = new Planner(store);
                planner.TaskCompleted += actor.Celebrate;
                planner.PreviewMouseDown += delegate { actor.Touch(); };
                planner.PreviewKeyDown += delegate { actor.Touch(); };
                planner.Owner = this;
                planner.WindowStartupLocation = WindowStartupLocation.Manual;
                WatchPlannerWindow(planner);
                planner.Closed += delegate { planner = null; };
            }
            if (!planner.IsVisible)
            {
                FitPlanner(planner, new Size(store.PlannerWidth, store.PlannerHeight));
                planner.Show();
                planner.FitDayHeight(true);
            }
            if (planner.WindowState == WindowState.Minimized) planner.WindowState = WindowState.Normal;
            var active = planner.OwnedWindows.Cast<Window>().FirstOrDefault(w => w.IsVisible) ?? planner;
            active.Activate();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (planner != null)
            {
                if (!planner.IsVisible) planner.Show();
                foreach (var editor in planner.OwnedWindows.Cast<Window>().ToArray()) editor.Close();
                if (planner.OwnedWindows.Count == 0) planner.Close();
                if (planner != null) e.Cancel = true;
            }
            base.OnClosing(e);
            if (!e.Cancel && sessionWindow != IntPtr.Zero)
            {
                WTSUnRegisterSessionNotification(sessionWindow);
                sessionWindow = IntPtr.Zero;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            if (input != null) input.Dispose();
            reminderTimer.Stop();
            store.Changed -= UpdateBadge;
            if (tray != null)
            {
                tray.Visible = false;
                var icon = tray.Icon;
                trayMenu.IsOpen = false;
                tray.Dispose();
                if (icon != null) icon.Dispose();
            }
            base.OnClosed(e);
        }
    }
}
