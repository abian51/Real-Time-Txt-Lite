using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace LCET
{
    public static class WindowDocking
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left, Top, Right, Bottom;
            public Rect ToRect() { return new Rect(Left, Top, Math.Max(0, Right - Left), Math.Max(0, Bottom - Top)); }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRect Monitor, Work;
            public uint Flags;
        }

        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int left, int top, int width, int height, uint flags);

        public static Rect Place(Rect anchor, Rect workArea, double height)
        {
            if (anchor.IsEmpty || workArea.IsEmpty || anchor.Width <= 0 || anchor.Height <= 0 ||
                height <= 0 || double.IsNaN(height) || double.IsInfinity(height)) return Rect.Empty;
            double below = Math.Max(0, workArea.Bottom - Math.Max(workArea.Top, anchor.Bottom));
            double above = Math.Max(0, Math.Min(workArea.Bottom, anchor.Top) - workArea.Top);
            if (below >= height) return new Rect(anchor.Left, Math.Max(workArea.Top, anchor.Bottom), anchor.Width, height);
            if (above >= height) return new Rect(anchor.Left, Math.Min(workArea.Bottom, anchor.Top) - height, anchor.Width, height);
            if (below == 0 && above == 0) return Rect.Empty;
            return below >= above
                ? new Rect(anchor.Left, Math.Max(workArea.Top, anchor.Bottom), anchor.Width, Math.Min(height, below))
                : new Rect(anchor.Left, Math.Min(workArea.Bottom, anchor.Top) - Math.Min(height, above), anchor.Width, Math.Min(height, above));
        }

        public static bool TryGetPlacement(IntPtr anchor, IntPtr follower, double desiredHeight, double actualHeight, out Rect placement)
        {
            placement = Rect.Empty;
            if (anchor == IntPtr.Zero || follower == IntPtr.Zero || !IsWindow(anchor) ||
                !IsWindowVisible(anchor) || IsIconic(anchor) || actualHeight <= 0) return false;
            IntPtr context = SetThreadDpiAwarenessContext(new IntPtr(-4));
            try
            {
                NativeRect anchorRect, followerRect;
                if (!GetWindowRect(anchor, out anchorRect) || !GetWindowRect(follower, out followerRect)) return false;
                var monitor = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
                if (!GetMonitorInfo(MonitorFromWindow(anchor, 2), ref monitor)) return false;
                double scale = (followerRect.Bottom - followerRect.Top) / actualHeight;
                if (scale <= 0) return false;
                placement = Place(anchorRect.ToRect(), monitor.Work.ToRect(), desiredHeight * scale);
                return !placement.IsEmpty;
            }
            finally { if (context != IntPtr.Zero) SetThreadDpiAwarenessContext(context); }
        }

        public static bool Apply(IntPtr follower, Rect placement)
        {
            if (follower == IntPtr.Zero || placement.IsEmpty) return false;
            IntPtr context = SetThreadDpiAwarenessContext(new IntPtr(-4));
            try
            {
                NativeRect current;
                if (!GetWindowRect(follower, out current)) return false;
                int left = (int)Math.Round(placement.Left), top = (int)Math.Round(placement.Top);
                int width = Math.Max(1, (int)Math.Round(placement.Width)), height = Math.Max(1, (int)Math.Round(placement.Height));
                if (current.Left == left && current.Top == top && current.Right - current.Left == width && current.Bottom - current.Top == height) return true;
                return SetWindowPos(follower, IntPtr.Zero, left, top, width, height, 0x0014);
            }
            finally { if (context != IntPtr.Zero) SetThreadDpiAwarenessContext(context); }
        }
    }
}