using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Xiaohei
{
    internal static class TypingInputCheck
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Registration
        {
            public ushort UsagePage, Usage;
            public uint Flags;
            public IntPtr Target;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRegisteredRawInputDevices([Out] Registration[] devices, ref uint count, uint size);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr window, int message, IntPtr parameter, IntPtr data);
        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(ref Registration device, uint count, uint size);

        private static Registration[] AllRegistrations()
        {
            uint size = (uint)Marshal.SizeOf(typeof(Registration)), count = 0;
            if (GetRegisteredRawInputDevices(null, ref count, size) == UInt32.MaxValue) throw new Win32Exception();
            if (count == 0) return new Registration[0];
            var devices = new Registration[count];
            uint actual = GetRegisteredRawInputDevices(devices, ref count, size);
            if (actual == UInt32.MaxValue) throw new Win32Exception();
            return devices.Take((int)actual).ToArray();
        }

        private static Registration[] InputRegistrations()
        {
            return AllRegistrations().Where(d => d.UsagePage == 1 && (d.Usage == 6 || d.Usage == 2)).ToArray();
        }

        private static void Pump(int milliseconds)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }

        private static void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            Console.WriteLine("PASS " + description);
        }

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        private static void CheckNativeFields()
        {
            using (var form = new Forms.Form { ShowInTaskbar = false })
            {
                var ordinary = new Forms.TextBox { Top = 4 };
                var password = new Forms.TextBox { Top = 36, UseSystemPasswordChar = true };
                form.Controls.Add(ordinary);
                form.Controls.Add(password);
                form.Show();
                form.Activate();
                uint thread = GetCurrentThreadId();
                Check(ordinary.Focus() && !NativePasswordField.IsFocused(thread), "the test's ordinary native edit field permits keycaps");
                Check(password.Focus() && NativePasswordField.IsFocused(thread), "a focused native password field requests anonymous tapping");
                Check(ordinary.Focus() && !NativePasswordField.IsFocused(thread), "leaving the password field clears its protected-focus state");
                int presses = 0, releases = 0, wheel = 0;
                ordinary.MouseDown += delegate(object sender, Forms.MouseEventArgs e) { if (e.Button == Forms.MouseButtons.Left) presses++; };
                ordinary.MouseUp += delegate(object sender, Forms.MouseEventArgs e) { if (e.Button == Forms.MouseButtons.Left) releases++; };
                ordinary.MouseWheel += delegate(object sender, Forms.MouseEventArgs e) { wheel += e.Delta; };
                // Target this test control only; these legacy messages do not generate device-level WM_INPUT.
                var point = new IntPtr((4 << 16) | 4);
                var screen = ordinary.PointToScreen(new System.Drawing.Point(4, 4));
                var screenPoint = new IntPtr(unchecked((screen.Y << 16) | (screen.X & 0xffff)));
                if (!PostMessage(ordinary.Handle, 0x201, new IntPtr(1), point) ||
                    !PostMessage(ordinary.Handle, 0x202, IntPtr.Zero, point) ||
                    !PostMessage(ordinary.Handle, 0x20A, new IntPtr(120 << 16), screenPoint)) throw new Win32Exception();
                Pump(80);
                Check(ordinary.Focused && presses == 1 && releases == 1 && wheel == 120,
                    "the focused test editor still receives its ordinary mouse press, release and wheel messages");
            }
        }

        private static System.Collections.Generic.IEnumerable<DependencyObject> Visuals(DependencyObject parent)
        {
            yield return parent;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                foreach (var child in Visuals(VisualTreeHelper.GetChild(parent, i))) yield return child;
        }

        private static PetArt.TypingKeyboard Workbench(PetActor actor)
        {
            actor.UpdateLayout();
            return Visuals(actor).OfType<PetArt.TypingKeyboard>().SingleOrDefault();
        }

        private static string VisibleKeycap(PetActor actor)
        {
            var keyboard = Workbench(actor);
            if (keyboard == null) return null;
            return Visuals(keyboard).OfType<TextBlock>().Where(label => label.IsVisible && !String.IsNullOrEmpty(label.Text))
                .Select(label => label.Text).SingleOrDefault();
        }

        private static void ClickOption(MenuItem item)
        {
            item.ApplyTemplate();
            var peer = UIElementAutomationPeer.CreatePeerForElement(item);
            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
            Pump(30);
        }

        private static void CheckFixturePacket(PetActor actor)
        {
            // This buffer models a fixed Shift packet. It is never sent to Windows as an HRAWINPUT.
            var packetType = typeof(KeyboardActivity).GetNestedType("InputPacket", System.Reflection.BindingFlags.NonPublic);
            var headerField = packetType.GetField("Header");
            var bodyField = packetType.GetField("Body");
            int headerSize = 8 + 2 * IntPtr.Size;
            var bytes = new byte[Marshal.SizeOf(packetType)];
            BitConverter.GetBytes((uint)1).CopyTo(bytes, 0);
            BitConverter.GetBytes((uint)(headerSize + 16)).CopyTo(bytes, 4);
            bytes[headerSize + 6] = 0x10;
            var memory = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                foreach (bool released in new[] { true, false })
                {
                    // A modifier holds its cap only after an ordinary keystroke has opened the keyboard.
                    if (!released) KeyboardActivity.AnimatePress(actor, 1, 0, 0x41, true, true);
                    bytes[headerSize + 2] = (byte)(released ? 1 : 0);
                    object packet = Marshal.PtrToStructure(memory.AddrOfPinnedObject(), packetType);
                    object header = headerField.GetValue(packet), body = bodyField.GetValue(packet);
                    KeyboardActivity.AnimatePress(actor, (uint)headerField.FieldType.GetField("Type").GetValue(header),
                        (ushort)bodyField.FieldType.GetField("Flags").GetValue(body),
                        (ushort)bodyField.FieldType.GetField("VirtualKey").GetValue(body), true, true);
                    Check(released ? actor.Pose == PetPose.Rest : VisibleKeycap(actor) == "Shift",
                        released ? "the marshalled fixture's break flag leaves the cat at rest" : "the marshalled make packet displays its fixed Shift cap");
                }
            }
            finally { memory.Free(); }

            var scene = Visuals(actor).OfType<PetArt.TypingKeyboard>().Single();
            Check(scene.IsKeyHeld, "the make packet holds only the currently displayed key");
            KeyboardActivity.AnimatePress(actor, 1, 1, 0x11, true, true);
            Check(scene.IsKeyHeld && VisibleKeycap(actor) == "Shift", "releasing a different known key leaves the held cap unchanged");
            KeyboardActivity.AnimatePress(actor, 1, 1, 0x10, true, true);
            Check(!scene.IsKeyHeld, "releasing the matching key lifts the paw");

            KeyboardActivity.AnimatePress(actor, 1, 0, 0x41, true, true);
            KeyboardActivity.AnimatePress(actor, 1, 1, 0x42, false, true);
            Check(!scene.IsKeyHeld && VisibleKeycap(actor) == null, "a private release clears the cap without revealing a key");
            KeyboardActivity.AnimatePress(actor, 1, 0, 0x41, true, true);
            KeyboardActivity.AnimatePress(actor, 1, 1, 0xE5, true, true);
            Check(!scene.IsKeyHeld && VisibleKeycap(actor) == null, "an anonymous release also clears the single visible key");
        }

        private static void CheckLabelPreference(DesktopPet pet, Store store, string directory)
        {
            var actor = (PetActor)pet.Content;
            CheckFixturePacket(actor);
            var option = pet.ContextMenu.Items.OfType<MenuItem>().Single(item => (string)item.Header == "显示按下的键");
            Check(option.IsCheckable && option.IsChecked == !store.HideKeyLabels, "the pet menu reflects the stored keycap preference");
            KeyboardActivity.AnimatePress(actor, 1, 0, 0x41);
            Check(VisibleKeycap(actor) == "A", "a known fixture key displays one current keycap");
            KeyboardActivity.AnimatePress(actor, 1, 0, 0xE5);
            Check(actor.Pose == PetPose.Typing && VisibleKeycap(actor) == null, "an unrecognized IME key clears the previous legend");
            KeyboardActivity.AnimatePress(actor, 1, 0, 0x10);
            Check(VisibleKeycap(actor) == "Shift", "a new key after an anonymous tap displays only its own legend");
            ClickOption(option);
            Check(!option.IsChecked && actor.Pose == PetPose.Rest && VisibleKeycap(actor) == null && new Store(directory).HideKeyLabels,
                "turning off keycaps clears the active illustration immediately and persists the setting");
            KeyboardActivity.AnimatePress(actor, 1, 0, 0x41, !store.HideKeyLabels);
            Check(actor.Pose == PetPose.Typing && VisibleKeycap(actor) == null, "typing still animates anonymously with keycaps disabled");
            ClickOption(option);
            Check(option.IsChecked && !new Store(directory).HideKeyLabels && VisibleKeycap(actor) == null,
                "re-enabling keycaps persists the setting without redisplaying the previous key");
        }

        private static KeyboardActivity.RawMouse MouseFixture(ushort motion, ushort buttons, short wheel, int x, int y)
        {
            // Model the native byte layout without sending a synthetic input event to the user's desktop.
            var bytes = new byte[24];
            BitConverter.GetBytes(motion).CopyTo(bytes, 0);
            BitConverter.GetBytes(buttons).CopyTo(bytes, 4);
            BitConverter.GetBytes(wheel).CopyTo(bytes, 6);
            BitConverter.GetBytes(x).CopyTo(bytes, 12);
            BitConverter.GetBytes(y).CopyTo(bytes, 16);
            var memory = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try { return (KeyboardActivity.RawMouse)Marshal.PtrToStructure(memory.AddrOfPinnedObject(), typeof(KeyboardActivity.RawMouse)); }
            finally { memory.Free(); }
        }

        private static void CheckMouseFeedback(PetActor actor)
        {
            int count = 0;
            int[] latest = null;
            var feedback = new KeyboardActivity.PointerFeedback(actor.Dispatcher, delegate(int x, int y, int buttons, int wheel)
            {
                count++;
                latest = new[] { x, y, buttons, wheel };
                actor.PointerActivity(x, y, buttons, wheel);
            });
            try
            {
                var relative = MouseFixture(0, 0, 0, 2, -3);
                for (int i = 0; i < 2000; i++) feedback.Receive(relative);
                Check(count == 0, "a high-rate motion burst does not redraw per device report");
                Pump(90);
                Check(count == 1 && latest.SequenceEqual(new[] { 4000, -6000, 0, 0 }), "one refresh preserves the summed relative movement");

                feedback.Receive(relative);
                feedback.Receive(MouseFixture(0, 0x415, 30, 0, 0));
                Check(count == 2 && latest.SequenceEqual(new[] { 2, -3, 7, 30 }), "three button presses and a partial wheel notch flush pending motion immediately");
                feedback.Receive(MouseFixture(0, 0x2A, 0, 0, 0));
                Check(count == 3 && latest.SequenceEqual(new[] { 0, 0, 56, 0 }), "left, right and middle release bits arrive immediately");
                feedback.Receive(MouseFixture(0, 0x803, -120, 0, 0));
                Check(count == 4 && latest.SequenceEqual(new[] { 0, 0, 9, -120 }), "a same-report press and release retains both edges and signed horizontal scroll");
                feedback.Receive(MouseFixture(0, 0x400, -4, 0, 0));
                Check(count == 5 && latest[3] == -4, "negative high-resolution vertical scrolling retains its direction");
                Pump(70);
                Check(count == 5, "immediate button and wheel feedback leaves no stale movement timer");

                feedback.Receive(MouseFixture(3, 0, 0, 64000, 21000));
                Pump(85);
                Check(count == 6 && latest.SequenceEqual(new[] { 0, 0, 0, 0 }), "absolute virtual-desktop coordinates request a cursor refresh instead of becoming a giant relative jump");
                feedback.Receive(relative);
                feedback.Stop();
                Pump(70);
                Check(count == 6, "stopping a subscription discards its pending movement");
                int before = count, reports = 0;
                var duration = Stopwatch.StartNew();
                do
                {
                    for (int i = 0; i < 10; i++) { feedback.Receive(relative); reports++; }
                    Pump(4);
                }
                while (duration.ElapsedMilliseconds < 300);
                duration.Stop();
                Pump(85);
                int frames = count - before;
                Check(frames > 0 && frames <= Math.Ceiling(duration.Elapsed.TotalMilliseconds / 33) + 1 && frames < reports,
                    reports + " interleaved movement reports produced " + frames + " bounded updates in " + duration.ElapsedMilliseconds + " ms");
            }
            finally { feedback.Stop(); }
        }

        private static IntPtr DesktopSwitchHook()
        {
            return (IntPtr)typeof(KeyboardActivity).GetField("desktopHook",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
        }

        private static void CheckInputReset(DesktopPet pet, PetActor actor)
        {
            const System.Reflection.BindingFlags instance = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var input = (KeyboardActivity)typeof(DesktopPet).GetField("input", instance).GetValue(pet);
            var pointer = (KeyboardActivity.PointerFeedback)typeof(KeyboardActivity).GetField("pointer", instance).GetValue(input);
            var heldButtons = typeof(PetActor).GetField("heldButtons", instance);
            var pointerState = (PetPointer)typeof(PetActor).GetField("pointer", instance).GetValue(actor);
            var sample = typeof(PetPointer).GetField("screenPoint", instance);
            KeyboardActivity.AnimatePress(actor, 1, 0, 0x41, true, true);
            actor.PointerActivity(0, 0, 1, 0);
            Check(Workbench(actor).IsKeyHeld && (int)heldButtons.GetValue(actor) == 1,
                "the reset fixture has both a held mouse button and a held key");
            var movement = MouseFixture(0, 0, 0, 140, 0);
            pointer.Receive(movement);
            input.Reset();
            Pump(100);
            Check(actor.Pose == PetPose.Rest && Workbench(actor) == null && (int)heldButtons.GetValue(actor) == 0 && sample.GetValue(pointerState) == null,
                "reset releases held controls, removes the keyboard and cancels queued motion");
            pointer.Receive(movement);
            Pump(85);
            Check(sample.GetValue(pointerState) != null && actor.Pose != PetPose.Typing && Workbench(actor) == null,
                "a reset subscription still receives mouse movement without opening the keyboard");
            input.Reset();
            KeyboardActivity.AnimatePress(actor, 1, 0, 0x41, true, true);
            Check(VisibleKeycap(actor) == "A", "a new keyboard make displays normally after reset");
            // A targeted device-removal notification exercises the window hook without disconnecting a real device.
            if (!PostMessage(new WindowInteropHelper(pet).Handle, 0x00FE, new IntPtr(2), IntPtr.Zero)) throw new Win32Exception();
            Pump(75);
            Check(actor.Pose == PetPose.Rest && Workbench(actor) == null,
                "a device-removal message to the live pet also clears a missing release");

            var hook = DesktopSwitchHook();
            Check(hook != IntPtr.Zero, "a desktop-switch listener is active");
            var callback = (Delegate)typeof(KeyboardActivity).GetField("desktopEvent",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
            KeyboardActivity.AnimatePress(actor, 1, 0, 0x41, true, true);
            pointer.Receive(movement);
            // Call only our boundary, never switch or broadcast an event to a desktop.
            callback.DynamicInvoke(hook, 0x001Fu, IntPtr.Zero, 0, 0, 0u, 0u);
            callback.DynamicInvoke(IntPtr.Zero, 0x0020u, IntPtr.Zero, 0, 0, 0u, 0u);
            Check(Workbench(actor).IsKeyHeld, "unrelated events and an invalid listener cannot reset the current input");
            callback.DynamicInvoke(hook, 0x0020u, IntPtr.Zero, 0, 0, 0u, 0u);
            Pump(85);
            Check(actor.Pose == PetPose.Rest && Workbench(actor) == null && sample.GetValue(pointerState) == null,
                "the desktop-switch boundary removes the keyboard and cancels pending pointer feedback");
        }

        private static void CheckRegistrationOwnership(Window editor, PetActor actor)
        {
            var alternate = new Window { ShowActivated = false, ShowInTaskbar = false };
            var unrelated = new Registration { UsagePage = 1, Usage = 4, Flags = 0x100, Target = new WindowInteropHelper(editor).Handle };
            uint size = (uint)Marshal.SizeOf(typeof(Registration));
            if (!RegisterRawInputDevices(ref unrelated, 1, size)) throw new Win32Exception();
            try
            {
                var previous = KeyboardActivity.Register(new WindowInteropHelper(alternate).EnsureHandle(), actor);
                var originalHook = DesktopSwitchHook();
                var latestHandle = new WindowInteropHelper(editor).Handle;
                using (previous)
                using (var latest = KeyboardActivity.Register(latestHandle, actor))
                {
                    Check(previous != null && latest != null && KeyboardActivity.Register(IntPtr.Zero, actor) == null,
                        "valid HWNDs register while an invalid registration leaves the current owner intact");
                    previous.Dispose();
                    var active = InputRegistrations();
                    Check(active.Length == 2 && active.All(d => d.Target == latestHandle) &&
                        originalHook != IntPtr.Zero && DesktopSwitchHook() == originalHook,
                        "disposing the old owner leaves both input classes on the newer HWND and preserves one desktop-switch listener");
                }
                Check(InputRegistrations().Length == 0 && DesktopSwitchHook() == IntPtr.Zero && AllRegistrations().Any(d => d.UsagePage == 1 &&
                    d.Usage == 4 && d.Target == unrelated.Target), "disposing the last owner removes its desktop listener and two input classes while preserving an independent usage");
            }
            finally
            {
                alternate.Close();
                unrelated.Target = IntPtr.Zero;
                unrelated.Flags = 1;
                if (!RegisterRawInputDevices(ref unrelated, 1, size)) throw new Win32Exception();
            }
        }

        private static void CheckKeycaps()
        {
            var expected = new System.Collections.Generic.Dictionary<int, string>
            {
                { 0x08, "Bksp" }, { 0x09, "Tab" }, { 0x0D, "Enter" }, { 0x10, "Shift" }, { 0x11, "Ctrl" }, { 0x12, "Alt" },
                { 0x13, "Pause" }, { 0x14, "Caps" }, { 0x1B, "Esc" }, { 0x20, "Space" }, { 0x21, "PgUp" }, { 0x22, "PgDn" },
                { 0x23, "End" }, { 0x24, "Home" }, { 0x25, "←" }, { 0x26, "↑" }, { 0x27, "→" }, { 0x28, "↓" },
                { 0x2C, "PrtSc" }, { 0x2D, "Ins" }, { 0x2E, "Del" }, { 0x5B, "Win" }, { 0x5C, "Win" }, { 0x5D, "Menu" },
                { 0x6A, "Num*" }, { 0x6B, "Num+" }, { 0x6D, "Num-" }, { 0x6E, "Num." }, { 0x6F, "Num/" },
                { 0x90, "Num" }, { 0x91, "ScrLk" }, { 0xA0, "Shift" }, { 0xA1, "Shift" },
                { 0xA2, "Ctrl" }, { 0xA3, "Ctrl" }, { 0xA4, "Alt" }, { 0xA5, "Alt" },
                { 0xBA, ";" }, { 0xBB, "=" }, { 0xBC, "," }, { 0xBD, "-" }, { 0xBE, "." }, { 0xBF, "/" }, { 0xC0, "`" },
                { 0xDB, "[" }, { 0xDC, "\\" }, { 0xDD, "]" }, { 0xDE, "'" }
            };
            for (int digit = 0; digit < 10; digit++)
            {
                string label = ((char)('0' + digit)).ToString();
                expected.Add(0x30 + digit, label);
                expected.Add(0x60 + digit, "Num" + label);
            }
            for (char letter = 'A'; letter <= 'Z'; letter++) expected.Add(letter, letter.ToString());
            for (int index = 1; index <= 24; index++)
                expected.Add(0x6F + index, "F" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));

            for (int code = 0; code <= UInt16.MaxValue; code++)
            {
                string label;
                expected.TryGetValue(code, out label);
                if (KeyboardActivity.Keycap((ushort)code) != label)
                    throw new InvalidOperationException("Unexpected keycap for fixture " + code.ToString("X4"));
            }
            Check(true, "all fixed keycap legends match and every other virtual-key code, including IME, has no label");
        }

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                CheckKeycaps();
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var root = args.Length > 0 ? args[0] : Path.GetTempPath();
                var directory = Path.Combine(root, "typing-input-" + Guid.NewGuid().ToString("N"));
                var store = new Store(directory);
                var pet = new DesktopPet(store) { ShowActivated = false };
                var field = new TextBox { Text = "sample" };
                var editor = new Window { Content = field, Width = 220, Height = 90, ShowInTaskbar = false };
                try
                {
                    pet.Show();
                    var petHandle = new WindowInteropHelper(pet).Handle;
                    var registrations = InputRegistrations();
                    Check(registrations.Any(d => d.Usage == 6) && registrations.Any(d => d.Usage == 2) &&
                        registrations.All(d => d.Target == petHandle && (d.Flags & ~0x2000u) == 0x100),
                        "Windows registers keyboard and mouse input sinks without suppressing legacy input");
                    var actor = (PetActor)pet.Content;
                    bool down = false, up = false;
                    field.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { down |= e.Key == Key.LeftShift; };
                    field.PreviewKeyUp += delegate(object sender, KeyEventArgs e) { up |= e.Key == Key.LeftShift; };
                    editor.Show();
                    editor.Activate();
                    field.Focus();
                    Pump(40);
                    var editorHandle = new WindowInteropHelper(editor).Handle;
                    Check(GetActiveWindow() == editorHandle && field.IsKeyboardFocused && !pet.IsActive,
                        "the editor owns the test thread's active window and keyboard focus");

                    // These messages target only our editor. PostMessage does not supply a device-generated WM_INPUT.
                    if (!PostMessage(editorHandle, 0x100, new IntPtr(0x10), new IntPtr(0x002A0001)) ||
                        !PostMessage(editorHandle, 0x101, new IntPtr(0x10), new IntPtr(unchecked((int)0xC02A0001))))
                        throw new Win32Exception();
                    Pump(60);
                    Check(down && up && field.Text == "sample", "the editor receives a Shift press and release without inserting text");

                    if (!PostMessage(petHandle, KeyboardActivity.InputMessage, new IntPtr(1), IntPtr.Zero)) throw new Win32Exception();
                    Pump(20);
                    KeyboardActivity.AnimatePress(actor, 0, 0);
                    KeyboardActivity.AnimatePress(actor, 2, 0);
                    KeyboardActivity.AnimatePress(actor, 1, 3);
                    Check(actor.Pose == PetPose.Rest && Workbench(actor) == null, "resting, invalid input and release events show the original cat without a keyboard");
                    foreach (ushort modifier in new ushort[] { 0x10, 0x11, 0x12, 0x14, 0x5B })
                    {
                        KeyboardActivity.AnimatePress(actor, 1, 0, modifier, true, true);
                        Check(actor.Pose != PetPose.Typing && Workbench(actor) == null,
                            "a standalone modifier keeps the keyboard hidden: " + modifier.ToString("X2"));
                        KeyboardActivity.AnimatePress(actor, 1, 1, modifier, true, true);
                    }
                    KeyboardActivity.AnimatePress(actor, 1, 0, 0x11, false, true);
                    actor.PointerActivity(0, 0, 0, 120);
                    KeyboardActivity.AnimatePress(actor, 1, 1, 0x11, false, true);
                    Check(actor.Pose != PetPose.Typing && Workbench(actor) == null,
                        "Ctrl plus wheel keeps the keyboard hidden even when labels are disabled");
                    KeyboardActivity.AnimatePress(actor, 1, 0);
                    Check(actor.Pose == PetPose.Typing && Workbench(actor) != null, "a decoded keyboard make opens the keyboard without any key identifier");
                    Pump(400);
                    KeyboardActivity.AnimatePress(actor, 1, 2);
                    var elapsed = Stopwatch.StartNew();
                    Pump(580);
                    Check(actor.Pose == PetPose.Typing, "another keyboard make extends the current typing interval");
                    while (actor.Pose == PetPose.Typing && elapsed.ElapsedMilliseconds < 1100) Pump(10);
                    Check(actor.Pose == PetPose.Rest && Workbench(actor) == null && elapsed.ElapsedMilliseconds >= 650 && elapsed.ElapsedMilliseconds <= 1000,
                        "the pet returns to rest " + elapsed.ElapsedMilliseconds + " ms after the last key press");
                    Check(GetActiveWindow() == editorHandle && field.IsKeyboardFocused && !pet.IsActive,
                        "the pet animation leaves the editor's keyboard focus in place");
                    CheckLabelPreference(pet, store, directory);
                    editor.Activate();
                    field.Focus();
                    CheckMouseFeedback(actor);
                    Check(actor.Pose != PetPose.Typing && Workbench(actor) == null, "mouse movement, button and wheel activity alone keep the keyboard hidden");
                    Check(GetActiveWindow() == editorHandle && field.IsKeyboardFocused && !pet.IsActive,
                        "pointer feedback preserves the editor's focus and active window");
                    CheckInputReset(pet, actor);
                    CheckNativeFields();
                    pet.Close();
                    Check(InputRegistrations().Length == 0 && editor.IsVisible, "closing the pet unregisters keyboard and mouse while the editor stays open");
                    CheckRegistrationOwnership(editor, actor);
                }
                finally { if (pet.IsVisible) pet.Close(); editor.Close(); app.Shutdown(); }
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
    }
}
