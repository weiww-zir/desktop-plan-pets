using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Xiaohei
{
    internal static class NativePasswordField
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct GuiThreadInfo
        {
            public int Size, Flags;
            public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
            public System.Drawing.Rectangle CaretRect;
        }

        [DllImport("user32.dll")]
        private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowStyle(IntPtr window, int index);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);

        internal static bool IsFocused(uint thread = 0)
        {
            var info = new GuiThreadInfo { Size = Marshal.SizeOf(typeof(GuiThreadInfo)) };
            if (!GetGUIThreadInfo(thread, ref info) || info.Focus == IntPtr.Zero) return true;
            const int styleIndex = -16, passwordStyle = 0x20;
            if ((GetWindowStyle(info.Focus, styleIndex) & passwordStyle) == 0) return false;
            // The same style bit means something different on other window classes.
            var name = new StringBuilder(128);
            if (GetClassName(info.Focus, name, name.Capacity) == 0) return true;
            var className = name.ToString();
            return className.Equals("Edit", StringComparison.OrdinalIgnoreCase) ||
                className.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase) ||
                className.StartsWith("WindowsForms10.EDIT.", StringComparison.OrdinalIgnoreCase) ||
                className.StartsWith("WindowsForms10.RichEdit", StringComparison.OrdinalIgnoreCase);
        }
    }
}
