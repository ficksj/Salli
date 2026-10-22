using System.Runtime.InteropServices;

namespace Salli.Services;

public sealed record AudioDevice(string Id, string Name, bool IsDefault);

/// <summary>Render endpoint enumeration, default-device switching and peak metering.</summary>
public static class AudioDeviceService
{
    public static List<AudioDevice> List()
    {
        var result = new List<AudioDevice>();
        IMMDeviceEnumerator? en = null;
        IMMDeviceCollection? col = null;

        try
        {
            string? current = null;
            try
            {
                en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                en.GetDefaultAudioEndpoint(0, 0, out var def);
                def.GetId(out current);
                Marshal.ReleaseComObject(def);
            }
            catch { }

            en ??= (IMMDeviceEnumerator)new MMDeviceEnumerator();
            if (en.EnumAudioEndpoints(0, 1 /*DEVICE_STATE_ACTIVE*/, out col) != 0 || col is null)
                return result;

            col.GetCount(out var count);
            for (var i = 0; i < count; i++)
            {
                IMMDevice? dev = null;
                try
                {
                    if (col.Item(i, out dev) != 0 || dev is null) continue;
                    dev.GetId(out var id);

                    var name = FriendlyName(dev) ?? "Устройство";
                    result.Add(new AudioDevice(id, name, id == current));
                }
                catch { }
                finally
                {
                    if (dev != null) Marshal.ReleaseComObject(dev);
                }
            }
        }
        catch { }
        finally
        {
            if (col != null) Marshal.ReleaseComObject(col);
            if (en != null) Marshal.ReleaseComObject(en);
        }

        return result;
    }

    public static bool SetDefault(string deviceId)
    {
        try
        {
            var policy = (IPolicyConfig)new PolicyConfigClient();
            for (var role = 0; role < 3; role++)
                policy.SetDefaultEndpoint(deviceId, role);

            Marshal.ReleaseComObject(policy);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Peak level of the default render device, 0..1.</summary>
    public static float Peak() => Peak(0);

    /// <summary>Peak level of the default capture (microphone) device, 0..1.</summary>
    public static float MicPeak() => Peak(1);

    private static float Peak(int dataFlow)
    {
        IMMDeviceEnumerator? en = null;
        IMMDevice? dev = null;
        try
        {
            en = (IMMDeviceEnumerator)new MMDeviceEnumerator();

            // capture devices use eCommunications as the usual default
            var role = 0;
            if (en.GetDefaultAudioEndpoint(dataFlow, role, out dev) != 0 || dev is null)
            {
                if (en.GetDefaultAudioEndpoint(dataFlow, 2, out dev) != 0 || dev is null) return 0;
            }

            var iid = typeof(IAudioMeterInformation).GUID;
            if (dev.Activate(ref iid, 0, IntPtr.Zero, out var obj) != 0 || obj is null) return 0;

            var meter = (IAudioMeterInformation)obj;
            meter.GetPeakValue(out var peak);
            Marshal.ReleaseComObject(meter);
            return Math.Clamp(peak, 0, 1);
        }
        catch
        {
            return 0;
        }
        finally
        {
            if (dev != null) Marshal.ReleaseComObject(dev);
            if (en != null) Marshal.ReleaseComObject(en);
        }
    }

    private static string? FriendlyName(IMMDevice dev)
    {
        IPropertyStore? store = null;
        try
        {
            if (dev.OpenPropertyStore(0, out store) != 0 || store is null) return null;

            var key = new PropertyKey
            {
                FormatId = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
                PropertyId = 14
            };

            if (store.GetValue(ref key, out var pv) != 0) return null;
            return pv.Pointer == IntPtr.Zero ? null : Marshal.PtrToStringUni(pv.Pointer);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (store != null) Marshal.ReleaseComObject(store);
        }
    }

    // ── COM plumbing ──────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public int  PropertyId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort ValueType;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public IntPtr Pointer;
        public IntPtr Pointer2;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    private class PolicyConfigClient { }

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        int GetMixFormat(string device, IntPtr format);
        int GetDeviceFormat(string device, bool isDefault, IntPtr format);
        int ResetDeviceFormat(string device);
        int SetDeviceFormat(string device, IntPtr endpointFormat, IntPtr mixFormat);
        int GetProcessingPeriod(string device, bool isDefault, IntPtr defaultPeriod, IntPtr minimumPeriod);
        int SetProcessingPeriod(string device, IntPtr period);
        int GetShareMode(string device, IntPtr mode);
        int SetShareMode(string device, IntPtr mode);
        int GetPropertyValue(string device, bool fxStore, IntPtr key, IntPtr value);
        int SetPropertyValue(string device, bool fxStore, IntPtr key, IntPtr value);
        int SetDefaultEndpoint(string device, int role);
        int SetEndpointVisibility(string device, bool visible);
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection? devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice(string id, out IMMDevice device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int Item(int index, out IMMDevice? device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object? iface);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore? store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
    }
}
