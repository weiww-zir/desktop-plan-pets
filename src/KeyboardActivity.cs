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

        // Reserve the native RAWINPUT union. Only the make/break flag is exposed.
        [StructLayout(LayoutKind.Explicit, Size = 24)]
        private struct InputBody
        {
            [FieldOffset(2)] public ushort Flags;
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

        internal static void Receive(IntPtr input, PetActor actor)
        {
            InputPacket packet;
            uint capacity = PacketSize;
            uint read = GetRawInputData(input, ReadInput, out packet, ref capacity, HeaderSize);
            if (read != UInt32.MaxValue && read >= HeaderSize + 16) // size of RAWKEYBOARD
                AnimatePress(actor, packet.Header.Type, packet.Body.Flags);
        }

        internal static void AnimatePress(PetActor actor, uint deviceType, ushort flags)
        {
            if (deviceType == KeyboardType && (flags & BreakFlag) == 0) actor.TapKeyboard();
        }
    }
}
