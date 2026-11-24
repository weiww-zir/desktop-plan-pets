using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Xiaohei
{
    internal sealed class KeyboardActivity : IDisposable
    {
        internal const int InputMessage = 0x00FF; // WM_INPUT
        private const uint BackgroundWithDeviceChanges = 0x100 | 0x2000, Remove = 0x01, ReadInput = 0x10000003;
        private const uint MouseType = 0, KeyboardType = 1;
        private const uint DesktopSwitch = 0x0020;
        private static KeyboardActivity mouseOwner, keyboardOwner;
        private static readonly DesktopEventProc desktopEvent = OnDesktopSwitch;
        private static IntPtr desktopHook;
        private readonly PetActor actor;
        private readonly PointerFeedback pointer;
        private const ushort BreakFlag = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct RawInputDevice
        {
            public ushort UsagePage, Usage;
            public uint Flags;
            public IntPtr Target;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct InputHeader
        {
            public uint Type, Size;
            public IntPtr Device, MessageParameter;
        }

        [StructLayout(LayoutKind.Explicit, Size = 24)]
        internal struct RawMouse
        {
            [FieldOffset(0)] public ushort MotionFlags;
            [FieldOffset(4)] public ushort Buttons;
            [FieldOffset(6)] public short WheelData;
            [FieldOffset(12)] public int DeltaX;
            [FieldOffset(16)] public int DeltaY;
        }

        // The mouse and keyboard bodies share the native RAWINPUT union.
        [StructLayout(LayoutKind.Explicit, Size = 24)]
        private struct InputBody
        {
            [FieldOffset(0)] public RawMouse Mouse;
            [FieldOffset(2)] public ushort Flags;
            [FieldOffset(6)] public ushort VirtualKey;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct InputPacket
        {
            public InputHeader Header;
            public InputBody Body;
        }

        private static readonly uint DeviceSize = (uint)Marshal.SizeOf(typeof(RawInputDevice));
        private static readonly uint HeaderSize = (uint)Marshal.SizeOf(typeof(InputHeader));
        private static readonly uint PacketSize = (uint)Marshal.SizeOf(typeof(InputPacket));

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(ref RawInputDevice device, uint count, uint size);

        [DllImport("user32.dll")]
        private static extern uint GetRawInputData(IntPtr input, uint command, out InputPacket packet, ref uint bytes, uint headerSize);

        internal static string Keycap(ushort virtualKey)
        {
            if ((virtualKey >= 0x30 && virtualKey <= 0x39) || (virtualKey >= 0x41 && virtualKey <= 0x5A))
                return ((char)virtualKey).ToString();
            if (virtualKey >= 0x60 && virtualKey <= 0x69)
                return "Num" + (char)('0' + virtualKey - 0x60);
            if (virtualKey >= 0x70 && virtualKey <= 0x87)
                return "F" + (virtualKey - 0x6F).ToString(System.Globalization.CultureInfo.InvariantCulture);

            // Fixed keycap legends, independent of the active layout, modifiers and input method.
            switch (virtualKey)
            {
                case 0x08: return "Bksp";
                case 0x09: return "Tab";
                case 0x0D: return "Enter";
                case 0x10: case 0xA0: case 0xA1: return "Shift";
                case 0x11: case 0xA2: case 0xA3: return "Ctrl";
                case 0x12: case 0xA4: case 0xA5: return "Alt";
                case 0x13: return "Pause";
                case 0x14: return "Caps";
                case 0x1B: return "Esc";
                case 0x20: return "Space";
                case 0x21: return "PgUp";
                case 0x22: return "PgDn";
                case 0x23: return "End";
                case 0x24: return "Home";
                case 0x25: return "←";
                case 0x26: return "↑";
                case 0x27: return "→";
                case 0x28: return "↓";
                case 0x2C: return "PrtSc";
                case 0x2D: return "Ins";
                case 0x2E: return "Del";
                case 0x5B: case 0x5C: return "Win";
                case 0x5D: return "Menu";
                case 0x6A: return "Num*";
                case 0x6B: return "Num+";
                case 0x6D: return "Num-";
                case 0x6E: return "Num.";
                case 0x6F: return "Num/";
                case 0x90: return "Num";
                case 0x91: return "ScrLk";
                case 0xBA: return ";";
                case 0xBB: return "=";
                case 0xBC: return ",";
                case 0xBD: return "-";
                case 0xBE: return ".";
                case 0xBF: return "/";
                case 0xC0: return "`";
                case 0xDB: return "[";
                case 0xDC: return "\\";
                case 0xDD: return "]";
                case 0xDE: return "'";
                default: return null;
            }
        }

        private delegate void DesktopEventProc(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, DesktopEventProc callback,
            uint process, uint thread, uint flags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hook);

        private static void OnDesktopSwitch(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time)
        {
            if (hook == IntPtr.Zero || hook != desktopHook || eventType != DesktopSwitch) return;
            if (keyboardOwner != null) keyboardOwner.Reset();
            if (mouseOwner != null && mouseOwner != keyboardOwner) mouseOwner.Reset();
        }

        private KeyboardActivity(PetActor actor)
        {
            this.actor = actor;
            pointer = new PointerFeedback(actor.Dispatcher, actor.PointerActivity);
        }

        private static bool SetRegistration(ushort usage, uint flags, IntPtr window)
        {
            var device = new RawInputDevice { UsagePage = 1, Usage = usage, Flags = flags, Target = window };
            return RegisterRawInputDevices(ref device, 1, DeviceSize);
        }

        internal static KeyboardActivity Register(IntPtr window, PetActor actor)
        {
            if (window == IntPtr.Zero) return null;
            var input = new KeyboardActivity(actor);
            // A device class can fail independently. Keep either successful subscription.
            if (SetRegistration(6, BackgroundWithDeviceChanges, window)) keyboardOwner = input;
            if (SetRegistration(2, BackgroundWithDeviceChanges, window))
            {
                if (mouseOwner != null) mouseOwner.pointer.Stop();
                mouseOwner = input;
            }
            if (keyboardOwner != input && mouseOwner != input) return null;
            // One out-of-context desktop-switch event for the application's current input owners.
            if (desktopHook == IntPtr.Zero)
                desktopHook = SetWinEventHook(DesktopSwitch, DesktopSwitch, IntPtr.Zero, desktopEvent, 0, 0, 0);
            return input;
        }

        internal void Reset()
        {
            pointer.Stop();
            if (keyboardOwner == this || mouseOwner == this) actor.ResetTransientInput();
        }

        public void Dispose()
        {
            pointer.Stop();
            // Windows keeps the last HWND per device class. Closing an old pet must not unregister a newer one.
            if (mouseOwner == this)
            {
                SetRegistration(2, Remove, IntPtr.Zero);
                mouseOwner = null;
            }
            if (keyboardOwner == this)
            {
                SetRegistration(6, Remove, IntPtr.Zero);
                keyboardOwner = null;
            }
            if (mouseOwner == null && keyboardOwner == null && desktopHook != IntPtr.Zero && UnhookWinEvent(desktopHook))
                desktopHook = IntPtr.Zero;
        }

        internal void Receive(IntPtr input, bool showKeycaps = true)
        {
            InputPacket packet;
            uint capacity = PacketSize;
            uint read = GetRawInputData(input, ReadInput, out packet, ref capacity, HeaderSize);
            if (read == UInt32.MaxValue) return;
            if (mouseOwner == this && packet.Header.Type == MouseType && read >= HeaderSize + 24)
                pointer.Receive(packet.Body.Mouse);
            else if (keyboardOwner == this && packet.Header.Type == KeyboardType && read >= HeaderSize + 16)
                AnimatePress(actor, packet.Header.Type, packet.Body.Flags, packet.Body.VirtualKey,
                    showKeycaps && !NativePasswordField.IsFocused(), true);
        }

        internal sealed class PointerFeedback
        {
            private readonly DispatcherTimer timer;
            private readonly Action<int, int, int, int> notify;
            private long deltaX, deltaY;

            internal PointerFeedback(Dispatcher dispatcher, Action<int, int, int, int> notify)
            {
                this.notify = notify;
                timer = new DispatcherTimer(DispatcherPriority.Input, dispatcher)
                { Interval = TimeSpan.FromMilliseconds(33) };
                timer.Tick += delegate { Flush(0, 0); };
            }

            internal void Receive(RawMouse mouse)
            {
                int native = mouse.Buttons;
                // RAWMOUSE interleaves down/up per button. The actor takes three down bits, then three up bits.
                int buttons = 0;
                if ((native & 0x01) != 0) buttons |= 1;
                if ((native & 0x04) != 0) buttons |= 2;
                if ((native & 0x10) != 0) buttons |= 4;
                if ((native & 0x02) != 0) buttons |= 8;
                if ((native & 0x08) != 0) buttons |= 16;
                if ((native & 0x20) != 0) buttons |= 32;
                // Both wheel axes have signed deltas; high-resolution wheels need not send a full 120-unit notch.
                int wheel = (native & 0xC00) != 0 ? mouse.WheelData : 0;
                bool absolute = (mouse.MotionFlags & 1) != 0;
                if (absolute)
                {
                    // Absolute coordinates belong to the primary or virtual desktop, not to this window.
                    // A zero-delta refresh lets the actor use the current pointer location on either display.
                    deltaX = deltaY = 0;
                }
                else
                {
                    deltaX += mouse.DeltaX;
                    deltaY += mouse.DeltaY;
                }
                if (buttons != 0 || wheel != 0) Flush(buttons, wheel);
                else if (!timer.IsEnabled && (absolute || mouse.DeltaX != 0 || mouse.DeltaY != 0)) timer.Start();
            }

            private void Flush(int buttons, int wheel)
            {
                int x = (int)Math.Max(Int32.MinValue, Math.Min(Int32.MaxValue, deltaX));
                int y = (int)Math.Max(Int32.MinValue, Math.Min(Int32.MaxValue, deltaY));
                Stop();
                notify(x, y, buttons, wheel);
            }

            internal void Stop()
            {
                timer.Stop();
                deltaX = deltaY = 0;
            }
        }

        internal static void AnimatePress(PetActor actor, uint deviceType, ushort flags, ushort virtualKey = 0,
            bool showKeycaps = true, bool held = false)
        {
            if (deviceType != KeyboardType) return;
            if ((flags & BreakFlag) != 0)
            {
                // The actor only releases its currently held cap. An anonymous release clears it.
                if (held) actor.ReleaseKeyboard(showKeycaps ? Keycap(virtualKey) : null);
                return;
            }
            actor.TapKeyboard(showKeycaps ? Keycap(virtualKey) : null, !showKeycaps, held);
        }
    }
}
