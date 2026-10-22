using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Salli.Services;

public static class NetworkSpeedService
{
    private static long _prevRx, _prevTx;
    private static DateTime _prevAt = DateTime.MinValue;

    public static (double Down, double Up) Sample()
    {
        long rx = 0, tx = 0;

        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

                try
                {
                    var s = ni.GetIPStatistics();
                    rx += s.BytesReceived;
                    tx += s.BytesSent;
                }
                catch { }
            }
        }
        catch { }

        var now = DateTime.UtcNow;
        var dt = (now - _prevAt).TotalSeconds;

        double down = 0, up = 0;
        if (_prevAt != DateTime.MinValue && dt > 0.2)
        {
            down = Math.Max(0, (rx - _prevRx) / dt);
            up   = Math.Max(0, (tx - _prevTx) / dt);
        }

        _prevRx = rx;
        _prevTx = tx;
        _prevAt = now;

        return (down, up);
    }

    public static string Format(double bytesPerSecond) => bytesPerSecond switch
    {
        < 1024                => $"{bytesPerSecond:0} Б/с",
        < 1024 * 1024         => $"{bytesPerSecond / 1024:0} КБ/с",
        < 1024d * 1024 * 1024 => $"{bytesPerSecond / 1024 / 1024:0.0} МБ/с",
        _                     => $"{bytesPerSecond / 1024 / 1024 / 1024:0.00} ГБ/с"
    };
}

public static class DiskService
{
    public sealed record DiskInfo(string Text, int UsedPercent, double FreeGb);

    public static DiskInfo System()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var drive = new DriveInfo(root);
            var total = drive.TotalSize;
            var free  = drive.TotalFreeSpace;
            var used  = total > 0 ? (int)Math.Round(100.0 * (total - free) / total) : 0;
            var freeGb = free / 1024.0 / 1024 / 1024;
            return new DiskInfo($"{freeGb:0} ГБ свободно", used, freeGb);
        }
        catch
        {
            return new DiskInfo("—", 0, 0);
        }
    }
}

public sealed record HeavyProcess(string Name, double Cpu, double RamMb);

public static class ProcessService
{
    private static readonly Dictionary<int, TimeSpan> Previous = new();
    private static DateTime _prevAt = DateTime.MinValue;

    public static List<HeavyProcess> Top(int count = 3)
    {
        var list = new List<HeavyProcess>();
        var now = DateTime.UtcNow;
        var dt = (now - _prevAt).TotalSeconds;

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var cpu = p.TotalProcessorTime;
                double percent = 0;

                if (dt > 0.2 && Previous.TryGetValue(p.Id, out var old))
                    percent = Math.Max(0, (cpu - old).TotalSeconds / dt / Environment.ProcessorCount * 100);

                Previous[p.Id] = cpu;

                list.Add(new HeavyProcess(
                    p.ProcessName,
                    Math.Round(percent),
                    Math.Round(p.WorkingSet64 / 1024.0 / 1024)));
            }
            catch
            {
                // access denied / exited
            }
        }

        _prevAt = now;

        return list.OrderByDescending(p => p.Cpu)
                   .ThenByDescending(p => p.RamMb)
                   .Take(count)
                   .ToList();
    }
}

public static class SoundService
{
    private const uint SndAsync    = 0x0001;
    private const uint SndMemory   = 0x0004;
    private const uint SndNoDefault= 0x0002;

    private static byte[]? _open;
    private static byte[]? _close;

    public static void Pop(bool opening)
    {
        try
        {
            var data = opening ? _open ??= Build(opening) : _close ??= Build(opening);

            var ptr = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, ptr, data.Length);

            PlaySound(ptr, IntPtr.Zero, SndMemory | SndAsync | SndNoDefault);

            // the buffer must outlive asynchronous playback
            _ = Task.Delay(1200).ContinueWith(_ => Marshal.FreeHGlobal(ptr));
        }
        catch
        {
            // sound is optional
        }
    }

    /// <summary>Three rising notes for a finished timer.</summary>
    public static void Alarm()
    {
        try
        {
            var data = _alarm ??= BuildChime();

            var ptr = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, ptr, data.Length);

            PlaySound(ptr, IntPtr.Zero, SndMemory | SndAsync | SndNoDefault);
            _ = Task.Delay(2600).ContinueWith(_ => Marshal.FreeHGlobal(ptr));
        }
        catch { }
    }

    private static byte[]? _alarm;

    private static byte[] BuildChime()
    {
        const int rate = 22050;
        var notes = new[] { (Freq: 784.0, Ms: 170), (Freq: 988.0, Ms: 170), (Freq: 1319.0, Ms: 320) };
        var total = notes.Sum(n => (int)(rate * n.Ms / 1000.0));

        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);

        var dataSize = total * 2;

        w.Write(new[] { 'R', 'I', 'F', 'F' });
        w.Write(36 + dataSize);
        w.Write(new[] { 'W', 'A', 'V', 'E' });
        w.Write(new[] { 'f', 'm', 't', ' ' });
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(rate);
        w.Write(rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write(new[] { 'd', 'a', 't', 'a' });
        w.Write(dataSize);

        foreach (var (freq, ms) in notes)
        {
            var samples = (int)(rate * ms / 1000.0);
            for (var i = 0; i < samples; i++)
            {
                var t   = (double)i / rate;
                var env = Math.Min(1, t * 90) * Math.Exp(-t * 9);
                var v   = (Math.Sin(2 * Math.PI * freq * t) * 0.6 +
                           Math.Sin(4 * Math.PI * freq * t) * 0.18) * env * 0.16;
                w.Write((short)(v * short.MaxValue));
            }
        }

        w.Flush();
        return stream.ToArray();
    }

    /// <summary>A very quiet, short blip: sine with an exponential decay.</summary>
    private static byte[] Build(bool opening)
    {
        const int rate = 22050;
        const double ms = 55;

        var samples = (int)(rate * ms / 1000);
        var freq = opening ? 880.0 : 560.0;

        using var msStream = new MemoryStream();
        using var w = new BinaryWriter(msStream);

        var dataSize = samples * 2;

        w.Write(new[] { 'R', 'I', 'F', 'F' });
        w.Write(36 + dataSize);
        w.Write(new[] { 'W', 'A', 'V', 'E' });
        w.Write(new[] { 'f', 'm', 't', ' ' });
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(rate);
        w.Write(rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write(new[] { 'd', 'a', 't', 'a' });
        w.Write(dataSize);

        for (var i = 0; i < samples; i++)
        {
            var t = (double)i / rate;
            var env = Math.Exp(-t * 60);
            var v = Math.Sin(2 * Math.PI * freq * t) * env * 0.12;
            w.Write((short)(v * short.MaxValue));
        }

        w.Flush();
        return msStream.ToArray();
    }

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);
}

public static class SourceService
{
    public static (string Name, string Glyph) Describe(string? aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid)) return ("", "");

        var s = aumid.ToLowerInvariant();

        if (s.Contains("spotify"))                     return ("Spotify",  "\uE8D6");
        if (s.Contains("aimp"))                        return ("AIMP",     "\uE8D6");
        if (s.Contains("music.ui") || s.Contains("zune")) return ("Media Player", "\uE8D6");
        if (s.Contains("foobar"))                      return ("foobar2000", "\uE8D6");
        if (s.Contains("vlc"))                         return ("VLC",      "\uE8D6");
        if (s.Contains("youtube"))                     return ("YouTube",  "\uE774");
        if (s.Contains("chrome"))                      return ("Chrome",   "\uE774");
        if (s.Contains("msedge"))                      return ("Edge",     "\uE774");
        if (s.Contains("firefox"))                     return ("Firefox",  "\uE774");
        if (s.Contains("opera"))                       return ("Opera",    "\uE774");
        if (s.Contains("tidal"))                       return ("Tidal",    "\uE8D6");
        if (s.Contains("deezer"))                      return ("Deezer",   "\uE8D6");
        if (s.Contains("soundcloud"))                  return ("SoundCloud", "\uE8D6");
        if (s.Contains("telegram"))                    return ("Telegram", "\uE8D6");
        if (s.Contains("yandex"))                      return ("Яндекс Музыка", "\uE8D6");

        // fall back to the executable / package name
        var cut = aumid.LastIndexOfAny(new[] { '\\', '/', '!' });
        var name = cut >= 0 ? aumid[(cut + 1)..] : aumid;
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];

        return (name, "\uE8D6");
    }
}

/// <summary>Warm “night light” via a gamma ramp on the desktop DC.</summary>
public static class NightLightService
{
    public static bool Enabled { get; private set; }

    public static void Set(bool on)
    {
        var dc = GetDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) return;

        try
        {
            var ramp = new ushort[3 * 256];

            for (var i = 0; i < 256; i++)
            {
                var v = i / 255.0;

                ramp[i]       = (ushort)(Math.Min(1, v)          * 65535); // R
                ramp[256 + i] = (ushort)(Math.Min(1, v * (on ? 0.84 : 1)) * 65535); // G
                ramp[512 + i] = (ushort)(Math.Min(1, v * (on ? 0.62 : 1)) * 65535); // B
            }

            SetDeviceGammaRamp(dc, ramp);
            Enabled = on;
        }
        catch
        {
            // not supported on this adapter
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, dc);
        }
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool SetDeviceGammaRamp(IntPtr dc, ushort[] ramp);
}

/// <summary>DDC/CI brightness for external monitors (dxva2).</summary>
public static class DdcService
{
    public static (int Min, int Cur, int Max)? Get(Window window)
        => Get(new WindowInteropHelper(window).Handle);

    public static (int Min, int Cur, int Max)? Get(IntPtr hwnd)
    {
        if (!TryGet(out var monitor, out var min, out var cur, out var max, hwnd)) return null;
        DestroyPhysicalMonitor(monitor);
        return ((int)min, (int)cur, (int)max);
    }

    public static bool Set(Window window, int percent)
    {
        if (!TryGet(out var monitor, out var min, out _, out var max, new WindowInteropHelper(window).Handle)) return false;
        try
        {
            var value = (uint)Math.Clamp(min + (max - min) * Math.Clamp(percent, 0, 100) / 100.0, min, max);
            return SetMonitorBrightness(monitor, value);
        }
        finally
        {
            DestroyPhysicalMonitor(monitor);
        }
    }

    private static bool TryGet(out IntPtr monitor, out uint min, out uint cur, out uint max, IntPtr hwnd)
    {
        monitor = IntPtr.Zero;
        min = cur = max = 0;

        try
        {
            var hmon = MonitorFromWindow(hwnd == IntPtr.Zero ? IntPtr.Zero : hwnd, 1 /*PRIMARY*/);
            if (hmon == IntPtr.Zero) hmon = MonitorFromWindow(IntPtr.Zero, 1);
            if (hmon == IntPtr.Zero) return false;

            if (!GetPhysicalMonitorsFromHMONITOR(hmon, 1, out var physical)) return false;
            monitor = physical.Handle;
            return GetMonitorBrightness(monitor, out min, out cur, out max);
        }
        catch
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PhysicalMonitor
    {
        public IntPtr Handle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
    }

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, out PhysicalMonitor physical);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetMonitorBrightness(IntPtr monitor, out uint min, out uint cur, out uint max);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool SetMonitorBrightness(IntPtr monitor, uint brightness);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool DestroyPhysicalMonitor(IntPtr monitor);
}
