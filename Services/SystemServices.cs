using System.Runtime.InteropServices;
using Windows.Devices.Radios;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Salli.Services;

public sealed class AudioEndpoint
{
    public int Volume { get; init; }
    public bool Muted { get; init; }
}

/// <summary>Master volume via the Core Audio endpoint API (no extra packages).</summary>
public static class AudioService
{
    public static AudioEndpoint Get()
    {
        if (!TryGetEndpoint(out var endpoint))
            return new AudioEndpoint { Volume = 0, Muted = true };

        try
        {
            endpoint.GetMasterVolumeLevelScalar(out float level);
            endpoint.GetMute(out bool mute);
            return new AudioEndpoint { Volume = (int)Math.Round(level * 100), Muted = mute };
        }
        finally
        {
            Marshal.ReleaseComObject(endpoint);
        }
    }

    public static void SetVolume(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        if (!TryGetEndpoint(out var endpoint)) return;
        try
        {
            endpoint.SetMasterVolumeLevelScalar(percent / 100f, Guid.Empty);
            if (percent > 0) endpoint.SetMute(false, Guid.Empty);
        }
        finally
        {
            Marshal.ReleaseComObject(endpoint);
        }
    }

    public static void ToggleMute()
    {
        if (!TryGetEndpoint(out var endpoint)) return;
        try
        {
            endpoint.GetMute(out bool mute);
            endpoint.SetMute(!mute, Guid.Empty);
        }
        finally
        {
            Marshal.ReleaseComObject(endpoint);
        }
    }

    private static bool TryGetEndpoint(out IAudioEndpointVolume endpoint)
    {
        endpoint = null!;
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            enumerator.GetDefaultAudioEndpoint(0, 0, out device);
            var iid = typeof(IAudioEndpointVolume).GUID;
            device.Activate(ref iid, 0, IntPtr.Zero, out var obj);
            endpoint = (IAudioEndpointVolume)obj;
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (device != null) Marshal.ReleaseComObject(device);
            if (enumerator != null) Marshal.ReleaseComObject(enumerator);
        }
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int NotImpl1();
        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr pNotify);
        int UnregisterControlChangeNotify(IntPtr pNotify);
        int GetChannelCount(out uint count);
        int SetMasterVolumeLevel(float levelDb, Guid eventContext);
        int SetMasterVolumeLevelScalar(float level, Guid eventContext);
        int GetMasterVolumeLevel(out float levelDb);
        int GetMasterVolumeLevelScalar(out float level);
        int SetChannelVolumeLevel(uint channel, float levelDb, Guid eventContext);
        int SetChannelVolumeLevelScalar(uint channel, float level, Guid eventContext);
        int GetChannelVolumeLevel(uint channel, out float levelDb);
        int GetChannelVolumeLevelScalar(uint channel, out float level);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, Guid eventContext);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}

public static class BrightnessService
{
    public static int? Get()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (System.Management.ManagementObject obj in searcher.Get())
                return Convert.ToInt32(obj["CurrentBrightness"]);
        }
        catch
        {
            // Desktop monitors usually don't expose this; laptops do.
        }
        return null;
    }

    public static bool Set(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (System.Management.ManagementObject obj in searcher.Get())
            {
                obj.InvokeMethod("WmiSetBrightness", new object[] { 1u, (byte)percent });
                return true;
            }
        }
        catch
        {
            // ignored
        }
        return false;
    }
}

public sealed class RadioInfo
{
    public required string Name { get; init; }
    public bool IsOn { get; init; }
    public bool Available { get; init; }
    public Radio? Radio { get; init; }
}

public static class RadioService
{
    public static async Task<RadioInfo> GetWifiAsync() => await GetAsync(RadioKind.WiFi, "Wi-Fi");
    public static async Task<RadioInfo> GetBluetoothAsync() => await GetAsync(RadioKind.Bluetooth, "Bluetooth");

    private static async Task<RadioInfo> GetAsync(RadioKind kind, string name)
    {
        try
        {
            var radios = await Radio.GetRadiosAsync();
            var radio = radios.FirstOrDefault(r => r.Kind == kind);
            if (radio == null)
                return new RadioInfo { Name = name, Available = false };

            return new RadioInfo
            {
                Name = string.IsNullOrWhiteSpace(radio.Name) ? name : radio.Name,
                IsOn = radio.State == RadioState.On,
                Available = true,
                Radio = radio
            };
        }
        catch
        {
            return new RadioInfo { Name = name, Available = false };
        }
    }

    public static async Task<bool> ToggleAsync(RadioInfo info)
    {
        if (info.Radio == null) return false;
        try
        {
            var access = await Radio.RequestAccessAsync();
            if (access != RadioAccessStatus.Allowed) return false;
            var target = info.IsOn ? RadioState.Off : RadioState.On;
            var result = await info.Radio.SetStateAsync(target);
            return result == RadioAccessStatus.Allowed;
        }
        catch
        {
            return false;
        }
    }
}

public sealed class NowPlaying
{
    public bool HasSession { get; init; }
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public bool IsPlaying { get; init; }
    public double Progress { get; init; }
    public double Duration { get; init; }
    public byte[]? Art { get; init; }
    public string SourceApp { get; init; } = "";
    public GlobalSystemMediaTransportControlsSession? Session { get; init; }
}

public static class MediaService
{
    public static async Task<NowPlaying> GetAsync()
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = manager.GetCurrentSession();
            if (session == null) return new NowPlaying();

            var props = await session.TryGetMediaPropertiesAsync();
            var playback = session.GetPlaybackInfo();

            byte[]? art = null;
            try
            {
                if (props.Thumbnail != null) art = await ReadBytesAsync(props.Thumbnail);
            }
            catch { /* players frequently return a broken thumbnail */ }

            double progress = 0;
            double duration = 0;
            try
            {
                var tl = session.GetTimelineProperties();
                var total = (tl.EndTime - tl.StartTime).TotalSeconds;
                if (total > 0)
                {
                    duration = total;
                    progress = Math.Clamp((tl.Position - tl.StartTime).TotalSeconds / total, 0, 1);
                }
            }
            catch { }

            return new NowPlaying
            {
                HasSession = true,
                Title = props.Title ?? "",
                Artist = props.Artist ?? "",
                IsPlaying = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                Progress = progress,
                Duration = duration,
                Art = art,
                SourceApp = session.SourceAppUserModelId ?? "",
                Session = session
            };
        }
        catch
        {
            return new NowPlaying();
        }
    }

    private static async Task<byte[]?> ReadBytesAsync(IRandomAccessStreamReference thumb)
    {
        using var stream = await thumb.OpenReadAsync();
        var size = (uint)stream.Size;
        if (size == 0) return null;

        var bytes = new byte[size];
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync(size);
        reader.ReadBytes(bytes);
        return bytes;
    }

    public static async Task ToggleAsync(NowPlaying now)
    {
        if (now.Session == null) return;
        try
        {
            if (now.IsPlaying) await now.Session.TryPauseAsync();
            else await now.Session.TryPlayAsync();
        }
        catch
        {
            // player may have closed
        }
    }

    public static async Task NextAsync(NowPlaying now)
    {
        if (now.Session == null) return;
        try { await now.Session.TrySkipNextAsync(); } catch { /* ignore */ }
    }

    public static async Task PreviousAsync(NowPlaying now)
    {
        if (now.Session == null) return;
        try { await now.Session.TrySkipPreviousAsync(); } catch { /* ignore */ }
    }

    /// <summary>Seek to a fraction (0..1) of the current track.</summary>
    public static async Task<bool> SeekAsync(NowPlaying now, double fraction)
    {
        if (now.Session == null) return false;
        try
        {
            var tl    = now.Session.GetTimelineProperties();
            var total = tl.EndTime - tl.StartTime;
            if (total.TotalSeconds <= 0) return false;

            var pos = tl.StartTime + TimeSpan.FromSeconds(
                Math.Clamp(fraction, 0, 1) * total.TotalSeconds);

            return await now.Session.TryChangePlaybackPositionAsync(pos.Ticks);
        }
        catch
        {
            return false;
        }
    }
}

public static class NetworkService
{
    public static string? WifiName()
    {
        try
        {
            var profile = WlanQuery();
            return string.IsNullOrWhiteSpace(profile) ? null : profile;
        }
        catch
        {
            return null;
        }
    }

    private static string? WlanQuery()
    {
        if (WlanOpenHandle(2, IntPtr.Zero, out _, out var handle) != 0) return null;
        try
        {
            if (WlanEnumInterfaces(handle, IntPtr.Zero, out var listPtr) != 0 || listPtr == IntPtr.Zero)
                return null;

            try
            {
                int count = Marshal.ReadInt32(listPtr);
                if (count <= 0) return null;
                // WLAN_INTERFACE_INFO_LIST: DWORD count + DWORD index, then the first interface.
                var info = Marshal.PtrToStructure<WlanInterfaceInfo>(listPtr + 8);

                // opcode 7 = wlan_intf_opcode_current_connection
                int result = WlanQueryInterface(handle, ref info.InterfaceGuid, 7, IntPtr.Zero,
                    out _, out IntPtr data, IntPtr.Zero);
                if (result != 0 || data == IntPtr.Zero) return null;
                try
                {
                    // WLAN_CONNECTION_ATTRIBUTES: isState(4) + wlanConnectionMode(4) + profile name.
                    if (Marshal.ReadInt32(data) != 1) return null;
                    return (Marshal.PtrToStringUni(data + 8) ?? "").Trim('\0', ' ');
                }
                finally
                {
                    WlanFreeMemory(data);
                }
            }
            finally
            {
                WlanFreeMemory(listPtr);
            }
        }
        finally
        {
            WlanCloseHandle(handle, IntPtr.Zero);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanInterfaceInfo
    {
        public Guid InterfaceGuid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Description;
        public int State;
    }

    [DllImport("wlanapi.dll")]
    private static extern int WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiated, out IntPtr handle);

    [DllImport("wlanapi.dll")]
    private static extern int WlanCloseHandle(IntPtr handle, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern int WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);

    [DllImport("wlanapi.dll")]
    private static extern int WlanQueryInterface(IntPtr handle, ref Guid interfaceGuid, int opcode,
        IntPtr reserved, out uint dataSize, out IntPtr data, IntPtr opcodeValueType);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr pMemory);
}

public static class PowerService
{
    public static string? BatteryText()
    {
        _charging = false;
        var status = new SystemPowerStatus();
        if (!GetSystemPowerStatus(ref status)) return null;
        if (status.BatteryFlag == 128) return null; // no battery
        if (status.BatteryLifePercent is > 100) return null;
        var charging = status.ACLineStatus == 1;
        _charging = charging;
        return $"{status.BatteryLifePercent}%";
    }

    private static bool _charging;

    /// <summary>True when the last <see cref="BatteryText"/> call saw the charger plugged in.</summary>
    public static bool IsCharging => _charging;

    /// <summary>“1:20 до 100%” while charging, “3:40 осталось” on battery.</summary>
    public static string? RemainingText()
    {
        try
        {
            var status = new SystemPowerStatus();
            if (!GetSystemPowerStatus(ref status)) return null;
            if (status.BatteryFlag == 128) return null;

            if (status.ACLineStatus == 1)
            {
                if (status.BatteryLifePercent >= 100) return "заряжено";
                if (status.BatteryFullLifeTime > 0) return $"{Clock(status.BatteryFullLifeTime)} до 100%";
                return "заряжается";
            }

            return status.BatteryLifeTime > 0 ? $"{Clock(status.BatteryLifeTime)} осталось" : null;
        }
        catch
        {
            return null;
        }
    }

    private static string Clock(int seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(ref SystemPowerStatus status);
}

public static class ClipboardService
{
    public static string Peek()
    {
        try
        {
            if (!System.Windows.Clipboard.ContainsText()) return "Буфер пуст";
            var text = System.Windows.Clipboard.GetText() ?? "";
            text = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (text.Length > 48) text = text[..48] + "…";
            return string.IsNullOrWhiteSpace(text) ? "Буфер пуст" : text;
        }
        catch
        {
            return "Буфер недоступен";
        }
    }
}

public static class QuickSettings
{
    public static void Open(string uri)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch
        {
            // settings app missing on very old builds
        }
    }

    public const string Wifi = "ms-settings:network-wifi";
    public const string Bluetooth = "ms-settings:bluetooth";
    public const string Sound = "ms-settings:sound";
    public const string Display = "ms-settings:display";
    public const string Calendar = "outlookcal:";
    public const string Notifications = "ms-settings:quietmoments";
    public const string Weather = "https://www.msn.com/ru-ru/weather";
}

public static class SystemStatsService
{
    private static ulong _prevIdle, _prevKernel, _prevUser;

    /// <summary>CPU load and memory load in percent (0..100).</summary>
    public static (int Cpu, int Ram) Sample()
    {
        int cpu = 0;
        try
        {
            if (GetSystemTimes(out var idle, out var kernel, out var user))
            {
                var i = ToU64(idle);
                var k = ToU64(kernel);
                var u = ToU64(user);

                if (_prevKernel != 0)
                {
                    var idleDelta  = i - _prevIdle;
                    var totalDelta = (k - _prevKernel) + (u - _prevUser);
                    if (totalDelta > 0)
                        cpu = (int)Math.Clamp(100.0 * (totalDelta - idleDelta) / totalDelta, 0, 100);
                }

                _prevIdle = i;
                _prevKernel = k;
                _prevUser = u;
            }
        }
        catch { }

        int ram = 0;
        try
        {
            var mem = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (GlobalMemoryStatusEx(ref mem)) ram = (int)mem.dwMemoryLoad;
        }
        catch { }

        return (cpu, ram);
    }

    private static ulong ToU64(FileTime ft) => ((ulong)ft.dwHighDateTime << 32) | ft.dwLowDateTime;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
