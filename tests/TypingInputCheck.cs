using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
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

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var root = args.Length > 0 ? args[0] : Path.GetTempPath();
                var store = new Store(Path.Combine(root, "typing-input-" + Guid.NewGuid().ToString("N")));
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
