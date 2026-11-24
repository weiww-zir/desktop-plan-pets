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

        private static Registration[] Keyboards()
        {
            uint size = (uint)Marshal.SizeOf(typeof(Registration)), count = 0;
            if (GetRegisteredRawInputDevices(null, ref count, size) == UInt32.MaxValue) throw new Win32Exception();
            if (count == 0) return new Registration[0];
            var devices = new Registration[count];
            uint actual = GetRegisteredRawInputDevices(devices, ref count, size);
            if (actual == UInt32.MaxValue) throw new Win32Exception();
            return devices.Take((int)actual).Where(d => d.UsagePage == 1 && d.Usage == 6).ToArray();
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
            }
        }

        private static System.Collections.Generic.IEnumerable<DependencyObject> Visuals(DependencyObject parent)
        {
            yield return parent;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                foreach (var child in Visuals(VisualTreeHelper.GetChild(parent, i))) yield return child;
        }

        private static string VisibleKeycap(PetActor actor)
        {
            actor.UpdateLayout();
            var keyboard = Visuals(actor).OfType<PetArt.TypingKeyboard>().SingleOrDefault();
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
                    bytes[headerSize + 2] = (byte)(released ? 1 : 0);
                    object packet = Marshal.PtrToStructure(memory.AddrOfPinnedObject(), packetType);
                    object header = headerField.GetValue(packet), body = bodyField.GetValue(packet);
                    KeyboardActivity.AnimatePress(actor, (uint)headerField.FieldType.GetField("Type").GetValue(header),
                        (ushort)bodyField.FieldType.GetField("Flags").GetValue(body),
                        (ushort)bodyField.FieldType.GetField("VirtualKey").GetValue(body));
                    Check(released ? actor.Pose == PetPose.Rest : VisibleKeycap(actor) == "Shift",
                        released ? "the marshalled fixture's break flag leaves the cat at rest" : "the marshalled make packet displays its fixed Shift cap");
                }
            }
            finally { memory.Free(); }
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
                    var registration = Keyboards().Single();
                    Check(registration.Target == petHandle && (registration.Flags & 0x100) != 0 && (registration.Flags & 0x30) == 0,
                        "Windows registers the pet HWND for background keyboard input and keeps legacy messages enabled");
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

                    KeyboardActivity.Receive(IntPtr.Zero, actor);
                    KeyboardActivity.AnimatePress(actor, 0, 0);
                    KeyboardActivity.AnimatePress(actor, 2, 0);
                    KeyboardActivity.AnimatePress(actor, 1, 3);
                    Check(actor.Pose == PetPose.Rest, "invalid handles, mouse, HID and extended key releases do not start typing");
                    KeyboardActivity.AnimatePress(actor, 1, 0);
                    Check(actor.Pose == PetPose.Typing, "a decoded keyboard make starts typing without any key identifier");
                    Pump(400);
                    KeyboardActivity.AnimatePress(actor, 1, 2);
                    var elapsed = Stopwatch.StartNew();
                    Pump(580);
                    Check(actor.Pose == PetPose.Typing, "another keyboard make extends the current typing interval");
                    while (actor.Pose == PetPose.Typing && elapsed.ElapsedMilliseconds < 1100) Pump(10);
                    Check(actor.Pose == PetPose.Rest && elapsed.ElapsedMilliseconds >= 650 && elapsed.ElapsedMilliseconds <= 1000,
                        "the pet returns to rest " + elapsed.ElapsedMilliseconds + " ms after the last key press");
                    Check(GetActiveWindow() == editorHandle && field.IsKeyboardFocused && !pet.IsActive,
                        "the pet animation leaves the editor's keyboard focus in place");
                    CheckLabelPreference(pet, store, directory);
                    CheckNativeFields();
                    pet.Close();
                    Check(Keyboards().Length == 0 && editor.IsVisible, "closing the pet unregisters its keyboard subscription while the editor stays open");
                }
                finally { if (pet.IsVisible) pet.Close(); editor.Close(); app.Shutdown(); }
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
    }
}
