using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace BmsConfig
{
    /// <summary>
    /// Keeps the window's startup size within the monitor's work area.  WPF sizes are in 1/96" units, so the
    /// nominal 1600x1000 window is scaled up by the display's DPI setting -- eg. on 2560x1440 at 150% the work
    /// area is only ~1707x920 units, and the titlebar would be off-screen.
    /// </summary>
    static class WindowFitting
    {
        /// <summary>Fraction of the work area the window may take up, at most, when it opens.</summary>
        const double MaxFraction = 0.92;

        /// <summary>
        /// Shrinks the window (and its minimum size, if need be) to fit the work area of the monitor it's on, and
        /// centers it there.  Call from SourceInitialized, ie. once the window has a handle but before it's shown.
        /// </summary>
        public static void FitToWorkArea(Window window)
        {
            Rect work = GetWorkArea(window);
            if (work.IsEmpty || work.Width <= 0 || work.Height <= 0)
                return;

            window.MinWidth = Math.Min(window.MinWidth, work.Width);
            window.MinHeight = Math.Min(window.MinHeight, work.Height);
            window.Width = Math.Max(window.MinWidth, Math.Min(window.Width, work.Width * MaxFraction));
            window.Height = Math.Max(window.MinHeight, Math.Min(window.Height, work.Height * MaxFraction));

            window.Left = work.Left + (work.Width - window.Width) / 2;
            window.Top = work.Top + (work.Height - window.Height) / 2;
        }

        /// <summary>The work area (screen minus taskbar) of the window's monitor, in WPF units.</summary>
        static Rect GetWorkArea(Window window)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(window).Handle;
                IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                MONITORINFO info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };

                if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
                {
                    // Pixels -> WPF units, at this monitor's DPI.
                    DpiScale dpi = VisualTreeHelper.GetDpi(window);
                    return new Rect(
                        info.rcWork.Left / dpi.DpiScaleX,
                        info.rcWork.Top / dpi.DpiScaleY,
                        (info.rcWork.Right - info.rcWork.Left) / dpi.DpiScaleX,
                        (info.rcWork.Bottom - info.rcWork.Top) / dpi.DpiScaleY);
                }
            }
            catch (Exception)
            {
                // Fall through to the primary monitor's work area.
            }
            return SystemParameters.WorkArea;
        }

        const int MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

        [DllImport("user32.dll")]
        static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    }
}
