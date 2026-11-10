namespace BotToucher2.Core.Hardware;

/// <summary>Observation formatting adapted from BT1, independent of Windows counter access.</summary>
public static class HardwareReadings
{
    public static HardwareMetricSnapshot Vram(long? used, long? capacity)
    {
        if (used is null)
            return new HardwareMetricSnapshot {
                Key = "vram", Label = "VRAM", Available = capacity is > 0,
                Value = capacity is > 0 ? FormatBytes((ulong)capacity.Value) + " total" : "unavailable",
                Detail = "Dedicated usage counter unavailable."
            };
        // Usage spans adapter counters; capacity is the largest adapter and cannot supply a percentage.
        return new HardwareMetricSnapshot {
            Key = "vram", Label = "VRAM", Available = true, Value = FormatBytes((ulong)Math.Max(0, used.Value)),
            Detail = capacity is > 0 ? "Dedicated usage across adapter counters; capacity is not directly comparable." :
                "Dedicated usage reported by Windows counters."
        };
    }
    public static string FormatBytes(ulong bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }
}
