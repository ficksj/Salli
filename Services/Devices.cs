using System.Runtime.InteropServices;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace Salli.Services;

public sealed record WifiNetwork(string Ssid, int Signal, bool Secure, bool Connected, string Profile);

public sealed record BtDevice(string Name, bool Connected, string Id);

/// <summary>Wi-Fi scan + connect (existing profiles) and paired Bluetooth devices.</summary>
public static class DevicesService
{
    public static List<WifiNetwork> WifiScan()
    {
        var list = new List<WifiNetwork>();

        if (WlanOpenHandle(2, IntPtr.Zero, out _, out var handle) != 0) return list;
        try
        {
            if (WlanEnumInterfaces(handle, IntPtr.Zero, out var ifaceList) != 0 || ifaceList == IntPtr.Zero)
                return list;
            try
            {
                var count = Marshal.ReadInt32(ifaceList);
                if (count <= 0) return list;

                var info = Marshal.PtrToStructure<WlanInterfaceInfo>(ifaceList + 8);

                if (WlanGetAvailableNetworkList(handle, ref info.InterfaceGuid, 0x3, IntPtr.Zero,
                        out var listPtr) != 0 || listPtr == IntPtr.Zero)
                    return list;

                try
                {
                    var n = Marshal.ReadInt32(listPtr);
                    var size = Marshal.SizeOf<WlanAvailableNetwork>();
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    for (var i = 0; i < n; i++)
                    {
                        var net = Marshal.PtrToStructure<WlanAvailableNetwork>(listPtr + 8 + i * size);
                        var ssid = net.Ssid.Text;
                        if (ssid.Length == 0 || !seen.Add(ssid)) continue;

                        list.Add(new WifiNetwork(
                            ssid,
                            (int)net.SignalQuality,
                            net.SecurityEnabled != 0,
                            net.Connected,
                            net.ProfileName ?? ""));

                        if (list.Count >= 12) break;
                    }
                }
                finally
                {
                    WlanFreeMemory(listPtr);
                }
            }
            finally
            {
                WlanFreeMemory(ifaceList);
            }
        }
        finally
        {
            WlanCloseHandle(handle, IntPtr.Zero);
        }

        return list.OrderByDescending(w => w.Connected)
                   .ThenByDescending(w => w.Signal)
                   .ToList();
    }

    public static bool WifiConnect(string profile)
    {
        if (string.IsNullOrWhiteSpace(profile)) return false;

        if (WlanOpenHandle(2, IntPtr.Zero, out _, out var handle) != 0) return false;
        try
        {
            if (WlanEnumInterfaces(handle, IntPtr.Zero, out var ifaceList) != 0 || ifaceList == IntPtr.Zero)
                return false;
            try
            {
                var count = Marshal.ReadInt32(ifaceList);
                if (count <= 0) return false;
                var info = Marshal.PtrToStructure<WlanInterfaceInfo>(ifaceList + 8);

                var p = new WlanConnectionParameters
                {
                    ConnectionMode      = 0, // profile
                    ProfileName         = profile,
                    Ssid                = IntPtr.Zero,
                    Bssid               = IntPtr.Zero,
                    BssType             = 1, // infrastructure
                    Flags               = 0
                };

                return WlanConnect(handle, ref info.InterfaceGuid, ref p, IntPtr.Zero) == 0;
            }
            finally
            {
                WlanFreeMemory(ifaceList);
            }
        }
        finally
        {
            WlanCloseHandle(handle, IntPtr.Zero);
        }
    }

    public static async Task<List<BtDevice>> BluetoothAsync()
    {
        var list = new List<BtDevice>();
        try
        {
            var radios = await Radio.GetRadiosAsync();
            var radio = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
            if (radio is null || radio.State != RadioState.On) return list;

            var selector = Windows.Devices.Bluetooth.BluetoothDevice.GetDeviceSelectorFromPairingState(true);
            var devices = await DeviceInformation.FindAllAsync(selector,
                new[] { "System.Devices.Aep.IsConnected" });

            foreach (var d in devices)
            {
                var connected = d.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var v)
                                && v is bool b && b;
                list.Add(new BtDevice(string.IsNullOrWhiteSpace(d.Name) ? "Устройство" : d.Name, connected, d.Id));
            }
        }
        catch
        {
            // Bluetooth stack unavailable
        }

        return list.OrderByDescending(d => d.Connected).ThenBy(d => d.Name).Take(10).ToList();
    }

    // ── wlanapi plumbing ──────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanInterfaceInfo
    {
        public Guid InterfaceGuid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Description;
        public int State;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Dot11Ssid
    {
        public uint Length;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Raw;

        public string Text
        {
            get
            {
                if (Raw is null) return "";
                var n = (int)Math.Min(Length, 32u);
                return System.Text.Encoding.UTF8.GetString(Raw, 0, n).TrimEnd('\0');
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanAvailableNetwork
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string ProfileName;
        public Dot11Ssid Ssid;
        public uint BssType;
        public uint NumberOfBssids;
        public int Connectable;
        public uint NotConnectableReason;
        public uint NumberOfPhyTypes;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public uint[] PhyTypes;
        public int MorePhyTypes;
        public uint SignalQuality;
        public int SecurityEnabled;
        public uint DefaultAuthAlgorithm;
        public uint DefaultCipherAlgorithm;
        public uint Flags;
        public uint Reserved;

        public bool Connected => (Flags & 0x1) != 0;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanConnectionParameters
    {
        public int ConnectionMode;
        [MarshalAs(UnmanagedType.LPWStr)] public string ProfileName;
        public IntPtr Ssid;
        public IntPtr Bssid;
        public uint BssType;
        public uint Flags;
    }

    [DllImport("wlanapi.dll")]
    private static extern int WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiated, out IntPtr handle);

    [DllImport("wlanapi.dll")]
    private static extern int WlanCloseHandle(IntPtr handle, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern int WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);

    [DllImport("wlanapi.dll")]
    private static extern int WlanGetAvailableNetworkList(IntPtr handle, ref Guid interfaceGuid,
        uint flags, IntPtr reserved, out IntPtr list);

    [DllImport("wlanapi.dll")]
    private static extern int WlanConnect(IntPtr handle, ref Guid interfaceGuid,
        ref WlanConnectionParameters parameters, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr memory);
}
