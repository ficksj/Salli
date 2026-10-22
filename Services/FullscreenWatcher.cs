using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Salli.Services;

/// <summary>
/// Detects real full-screen apps (games, players, presentation mode) so the
/// island can get out of the way. Uses the shell notification state first,
/// then a monitor-bounds comparison as a fallback for borderless windows.
/// </summary>
public static class FullscreenWatcher
{
    public static bool IsFullscreen(IntPtr ownWindow, params IntPtr[] ignored)
    {
        // QUNS_RUNNING_D3D_FULL_SCREEN = 3, QUNS_PRESENTATION_MODE = 4
        try
        {
            if (SHQueryUserNotificationState(out var state) == 0 && (state == 3 || state == 4))
                return true;
        }
        catch { }

        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == ownWindow) return false;
        foreach (var h in ignored)
            if (h != IntPtr.Zero && h == fg) return false;

        var cls = ClassName(fg);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            return false;

        if (!GetWindowRect(fg, out var r)) return false;
        if (r.Left <= -30000 || r.Top <= -30000) return false;   // minimised
        if (r.Right - r.Left < 200 || r.Bottom - r.Top < 200) return false;

        var mon = MonitorFromWindow(fg, 2 /*MONITOR_DEFAULTTONEAREST*/);
        if (mon == IntPtr.Zero) return false;

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(mon, ref info)) return false;

        var m = info.rcMonitor;
        const int tol = 2;
        return Math.Abs(r.Left - m.Left) <= tol &&
               Math.Abs(r.Top - m.Top) <= tol &&
               Math.Abs(r.Right - m.Right) <= tol &&
               Math.Abs(r.Bottom - m.Bottom) <= tol;
    }

    private static string ClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(64);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);
}
