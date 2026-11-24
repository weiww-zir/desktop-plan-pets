using System;
using System.Runtime.InteropServices;

namespace Xiaohei
{
    internal static class KeyboardActivity
    {
        internal const int InputMessage = 0x00FF; // WM_INPUT
        private const uint InputSink = 0x100, Remove = 0x01, ReadInput = 0x10000003;
        private const uint KeyboardType = 1;
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

        // The renderer uses fixed keycap legends, never composed or translated text.
        [StructLayout(LayoutKind.Explicit, Size = 24)]
        private struct InputBody
        {
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

        internal static bool Register(IntPtr window)
        {
            var device = new RawInputDevice { UsagePage = 1, Usage = 6, Flags = InputSink, Target = window };
            return RegisterRawInputDevices(ref device, 1, DeviceSize);
        }

        internal static void Unregister()
        {
            var device = new RawInputDevice { UsagePage = 1, Usage = 6, Flags = Remove };
            RegisterRawInputDevices(ref device, 1, DeviceSize);
        }

        internal static void Receive(IntPtr input, PetActor actor, bool showKeycaps = true)
        {
            InputPacket packet;
            uint capacity = PacketSize;
            uint read = GetRawInputData(input, ReadInput, out packet, ref capacity, HeaderSize);
            if (read != UInt32.MaxValue && read >= HeaderSize + 16 &&
                packet.Header.Type == KeyboardType && (packet.Body.Flags & BreakFlag) == 0)
                AnimatePress(actor, packet.Header.Type, packet.Body.Flags, packet.Body.VirtualKey,
                    showKeycaps && !NativePasswordField.IsFocused());
        }

        internal static void AnimatePress(PetActor actor, uint deviceType, ushort flags, ushort virtualKey = 0, bool showKeycaps = true)
        {
            if (deviceType == KeyboardType && (flags & BreakFlag) == 0)
                actor.TapKeyboard(showKeycaps ? Keycap(virtualKey) : null);
        }
    }
}
