using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace Salli.Services;

public sealed record MixerSession(string Name, string Title, IReadOnlyList<uint> Pids, int Volume, bool Muted);

/// <summary>Per-application volume via IAudioSessionManager2 / ISimpleAudioVolume.</summary>
public static class AppMixer
{
    private sealed class Group
    {
        public required string Name;
        public string Title = "";
        public readonly List<uint> Pids = new();
        public int Volume;
        public bool Muted;
    }

    public static List<MixerSession> List()
    {
        var result = new List<MixerSession>();
        var groups = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);

        IMMDeviceEnumerator? en = null;
        IMMDevice? dev = null;
        object? mgrObj = null;
        IAudioSessionEnumerator? sessions = null;

        try
        {
            en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            if (en.GetDefaultAudioEndpoint(0 /*eRender*/, 0 /*eConsole*/, out dev) != 0 || dev is null)
                return result;

            var iid = typeof(IAudioSessionManager2).GUID;
            if (dev.Activate(ref iid, 1 /*CLSCTX_ALL*/, IntPtr.Zero, out mgrObj) != 0 || mgrObj is null)
                return result;

            var mgr = (IAudioSessionManager2)mgrObj;
            if (mgr.GetSessionEnumerator(out sessions) != 0 || sessions is null) return result;

            sessions.GetCount(out var count);

            for (var i = 0; i < count; i++)
            {
                IAudioSessionControl? ctl = null;
                try
                {
                    if (sessions.GetSession(i, out ctl) != 0 || ctl is null) continue;
                    if (ctl is not IAudioSessionControl2 c2) continue;

                    c2.GetProcessId(out var pid);
                    if (pid == 0) continue;

                    // expired sessions linger after the app closes
                    if (ctl.GetState(out var state) == 0 && state == 2) continue;
                    if (ctl is not ISimpleAudioVolume vol) continue;

                    vol.GetMasterVolume(out var level);
                    vol.GetMute(out var muted);

                    string name;
                    string title;
                    try
                    {
                        using var p = Process.GetProcessById((int)pid);
                        name  = p.ProcessName;
                        title = p.MainWindowTitle ?? "";
                        if (title == name) title = "";
                    }
                    catch
                    {
                        name  = $"{L.T("app")} {pid}";
                        title = "";
                    }

                    if (name.Length > 0)
                        name = char.ToUpper(name[0]) + name[1..];

                    var volume = (int)Math.Round(Math.Clamp(level, 0, 1) * 100);

                    if (groups.TryGetValue(name, out var group))
                    {
                        group.Pids.Add(pid);
                        group.Volume = Math.Max(group.Volume, volume);
                        group.Muted &= muted;
                        if (group.Title.Length == 0) group.Title = title;
                    }
                    else
                    {
                        groups[name] = new Group
                        {
                            Name = name, Title = title, Volume = volume, Muted = muted
                        };
                        groups[name].Pids.Add(pid);
                    }
                }
                catch { }
                finally
                {
                    if (ctl != null) Marshal.ReleaseComObject(ctl);
                }
            }

            foreach (var g in groups.Values)
                result.Add(new MixerSession(g.Name, g.Title, g.Pids.ToArray(), g.Volume, g.Muted));
        }
        catch { }
        finally
        {
            if (sessions != null) Marshal.ReleaseComObject(sessions);
            if (mgrObj   != null) Marshal.ReleaseComObject(mgrObj);
            if (dev      != null) Marshal.ReleaseComObject(dev);
            if (en       != null) Marshal.ReleaseComObject(en);
        }

        return result.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static bool SetVolume(IEnumerable<uint> pids, int percent)
        => Mutate(pids, vol =>
        {
            var guid = Guid.Empty;
            return vol.SetMasterVolume(Math.Clamp(percent, 0, 100) / 100f, ref guid) == 0;
        });

    public static bool SetMute(IEnumerable<uint> pids, bool mute)
        => Mutate(pids, vol =>
        {
            var guid = Guid.Empty;
            return vol.SetMute(mute, ref guid) == 0;
        });

    private static bool Mutate(IEnumerable<uint> pids, Func<ISimpleAudioVolume, bool> action)
    {
        var targets = pids.ToHashSet();
        var any = false;
        IMMDeviceEnumerator? en = null;
        IMMDevice? dev = null;
        object? mgrObj = null;
        IAudioSessionEnumerator? sessions = null;

        try
        {
            en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            if (en.GetDefaultAudioEndpoint(0, 0, out dev) != 0 || dev is null) return false;

            var iid = typeof(IAudioSessionManager2).GUID;
            if (dev.Activate(ref iid, 1, IntPtr.Zero, out mgrObj) != 0 || mgrObj is null) return false;

            var mgr = (IAudioSessionManager2)mgrObj;
            if (mgr.GetSessionEnumerator(out sessions) != 0 || sessions is null) return false;

            sessions.GetCount(out var count);
            for (var i = 0; i < count; i++)
            {
                IAudioSessionControl? ctl = null;
                try
                {
                    if (sessions.GetSession(i, out ctl) != 0 || ctl is null) continue;
                    if (ctl is not IAudioSessionControl2 c2) continue;

                    c2.GetProcessId(out var sessionPid);
                    if (!targets.Contains(sessionPid)) continue;

                    if (ctl is ISimpleAudioVolume vol && action(vol)) any = true;
                }
                catch { }
                finally
                {
                    if (ctl != null) Marshal.ReleaseComObject(ctl);
                }
            }
        }
        catch { }
        finally
        {
            if (sessions != null) Marshal.ReleaseComObject(sessions);
            if (mgrObj   != null) Marshal.ReleaseComObject(mgrObj);
            if (dev      != null) Marshal.ReleaseComObject(dev);
            if (en       != null) Marshal.ReleaseComObject(en);
        }

        return any;
    }

    // ── COM plumbing ──────────────────────────────────────────────────────
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? device);
        [PreserveSig] int GetDevice(string id, out IMMDevice? device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object? iface);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        // IAudioSessionManager
        [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, uint streamFlags, out IAudioSessionControl? session);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, uint streamFlags, out ISimpleAudioVolume? volume);
        // IAudioSessionManager2
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator? enumerator);
        [PreserveSig] int RegisterSessionNotification(IntPtr notification);
        [PreserveSig] int UnregisterSessionNotification(IntPtr notification);
        [PreserveSig] int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, IntPtr notification);
        [PreserveSig] int UnregisterDuckNotification(IntPtr notification);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl? session);
    }

    [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr eventContext);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr eventContext);
        [PreserveSig] int GetGroupingParam(out Guid grouping);
        [PreserveSig] int SetGroupingParam(ref Guid grouping, IntPtr eventContext);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr notification);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notification);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        // IAudioSessionControl
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr eventContext);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr eventContext);
        [PreserveSig] int GetGroupingParam(out Guid grouping);
        [PreserveSig] int SetGroupingParam(ref Guid grouping, IntPtr eventContext);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr notification);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notification);
        // IAudioSessionControl2
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetProcessId(out uint pid);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference(bool optOut);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute(bool mute, ref Guid eventContext);
        [PreserveSig] int GetMute(out bool mute);
    }
}
