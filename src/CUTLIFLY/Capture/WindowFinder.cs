using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using Cutlifly.Core;

namespace Cutlifly.Capture
{
    public struct WindowInfo
    {
        public IntPtr Handle;
        public Rectangle Bounds; // coordenadas de pantalla físicas
    }

    /// <summary>Enumera ventanas visibles en orden Z (de arriba a abajo).</summary>
    public static class WindowFinder
    {
        public static List<WindowInfo> GetVisibleWindows()
        {
            var list = new List<WindowInfo>();
            int myPid = Environment.ProcessId;
            Native.EnumWindows((h, _) =>
            {
                if (!Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;
                if (Native.DwmGetWindowAttribute(h, Native.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
                Native.GetWindowThreadProcessId(h, out int pid);
                if (pid == myPid) return true;
                var r = GetBounds(h);
                if (r.Width < 8 || r.Height < 8) return true;
                var ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
                if ((ex & Native.WS_EX_TRANSPARENT) != 0 && (ex & Native.WS_EX_LAYERED) != 0) return true; // overlays click-through
                list.Add(new WindowInfo { Handle = h, Bounds = r });
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static Rectangle GetBounds(IntPtr h)
        {
            if (Native.DwmGetWindowAttribute(h, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out Native.RECT r, 16) != 0)
                Native.GetWindowRect(h, out r);
            return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        }
    }
}
