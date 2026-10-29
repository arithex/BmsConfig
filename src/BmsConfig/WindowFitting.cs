using System;
using System.Windows;

namespace BmsConfig
{
    /// <summary>
    /// Keeps the window's startup size within the screen's work area.  WPF sizes are in 1/96" units, so the
    /// nominal 1600x1000 window is scaled up by the display's DPI setting -- eg. on 2560x1440 at 150% the work
    /// area is only ~1707x920 units, and the titlebar would be off-screen.
    /// </summary>
    /// <remarks>
    /// Deliberately pure WPF (no p/invoke), for portability.  SystemParameters.WorkArea describes the primary
    /// monitor, which is where new windows open by default.
    /// </remarks>
    static class WindowFitting
    {
        /// <summary>Fraction of the work area the window may take up, at most, when it opens.</summary>
        const double MaxFraction = 0.8;

        /// <summary>
        /// Shrinks the window (and its minimum size, if need be) to fit the work area, and centers it there.
        /// Call before the window is shown, eg. from its constructor.
        /// </summary>
        public static void FitToWorkArea(Window window)
        {
            Rect work = SystemParameters.WorkArea;
            if (work.IsEmpty || work.Width <= 0 || work.Height <= 0)
                return;

            window.MinWidth = Math.Min(window.MinWidth, work.Width);
            window.MinHeight = Math.Min(window.MinHeight, work.Height);
            window.Width = Math.Max(window.MinWidth, Math.Min(window.Width, work.Width * MaxFraction));
            window.Height = Math.Max(window.MinHeight, Math.Min(window.Height, work.Height * MaxFraction));

            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = work.Left + (work.Width - window.Width) / 2;
            window.Top = work.Top + (work.Height - window.Height) / 2;
        }
    }
}
