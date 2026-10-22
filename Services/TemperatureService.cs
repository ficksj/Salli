using System.Management;

namespace Salli.Services;

/// <summary>
/// Best-effort CPU temperature. Primary source is the ACPI thermal zone perf counter
/// (<c>Win32_PerfFormattedData_Counters_ThermalZoneInformation</c>, value in tenths of Kelvin);
/// the classic <c>MSAcpi_ThermalZoneTemperature</c> class is used as a fallback.
/// Returns <c>null</c> on machines that do not publish thermal zones.
/// </summary>
public static class TemperatureService
{
    private static DateTime _at = DateTime.MinValue;
    private static int? _cached;

    public static int? Cpu()
    {
        if ((DateTime.Now - _at).TotalSeconds < 10) return _cached;
        _at = DateTime.Now;

        _cached = FromPerfCounters() ?? FromAcpi() ;
        return _cached;
    }

    private static int? FromPerfCounters()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\cimv2",
                "SELECT HighPrecisionTemperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation");

            int best = -1;

            foreach (var item in searcher.Get())
            {
                using var mo = (ManagementObject)item;

                if (mo["HighPrecisionTemperature"] is null) continue;

                var celsius = (int)Math.Round(Convert.ToDouble(mo["HighPrecisionTemperature"]) / 10.0 - 273.15);
                if (celsius is > 15 and < 115 && celsius > best) best = celsius;
            }

            return best > 0 ? best : null;
        }
        catch
        {
            return null;
        }
    }

    private static int? FromAcpi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI",
                "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");

            foreach (var item in searcher.Get())
            {
                using var mo = (ManagementObject)item;

                if (mo["CurrentTemperature"] is null) continue;

                var celsius = (int)Math.Round(Convert.ToDouble(mo["CurrentTemperature"]) / 10.0 - 273.15);
                if (celsius is > 15 and < 115) return celsius;
            }
        }
        catch
        {
            // not supported on this hardware
        }

        return null;
    }
}
