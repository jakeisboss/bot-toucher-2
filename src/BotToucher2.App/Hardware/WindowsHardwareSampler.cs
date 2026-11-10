// Adapted from BT1 6c30c11 HardwareMonitorService.cs. See docs/BT1-REUSE.md.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using BotToucher2.Core.Hardware;

namespace BotToucher2.App.Hardware;

public sealed class WindowsHardwareSampler : IHardwareSampler
{
    private readonly object _sync = new();
    private List<PerformanceCounter>? _gpuUtilizationCounters;
    private List<PerformanceCounter>? _vramUsageCounters;
    private bool _gpuCountersInitialized;
    private ulong? _lastIdle;
    private ulong? _lastKernel;
    private ulong? _lastUser;

    // Task.Run does not abandon native sampling after cancellation. The owner awaits it
    // before disposal; a token already cancelled prevents starting another sample.
    public Task<HardwareSnapshot> CaptureAsync(CancellationToken cancellationToken) =>
        Task.Run(Capture, cancellationToken);

    public HardwareSnapshot Capture()
    {
        var metrics = new List<HardwareMetricSnapshot>
        {
            CpuMetric(),
            GpuMetric(),
            RamMetric(),
            VramMetric(),
            UnavailableTemperature("cpu-temperature", "CPU Temp"),
            UnavailableTemperature("gpu-temperature", "GPU Temp")
        };

        return new HardwareSnapshot
        {
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Metrics = metrics
        };
    }

    private HardwareMetricSnapshot CpuMetric()
    {
        if (!OperatingSystem.IsWindows() || !GetSystemTimes(out var idle, out var kernel, out var user))
            return Unavailable("cpu", "CPU", "System CPU telemetry is unavailable.");

        lock (_sync)
        {
            var idleTicks = ToUInt64(idle);
            var kernelTicks = ToUInt64(kernel);
            var userTicks = ToUInt64(user);

            if (_lastIdle is null || _lastKernel is null || _lastUser is null)
            {
                _lastIdle = idleTicks;
                _lastKernel = kernelTicks;
                _lastUser = userTicks;
                return new HardwareMetricSnapshot
                {
                    Key = "cpu",
                    Label = "CPU",
                    Value = "sampling…",
                    Detail = "Windows system utilization",
                    Available = true
                };
            }

            var idleDelta = idleTicks - _lastIdle.Value;
            var kernelDelta = kernelTicks - _lastKernel.Value;
            var userDelta = userTicks - _lastUser.Value;
            _lastIdle = idleTicks;
            _lastKernel = kernelTicks;
            _lastUser = userTicks;

            var total = kernelDelta + userDelta;
            if (total == 0)
                return Unavailable("cpu", "CPU", "No CPU sample interval was available.");

            var busy = total > idleDelta ? total - idleDelta : 0;
            var percent = Math.Clamp(100d * busy / total, 0d, 100d);
            return Percent("cpu", "CPU", percent, "Windows system utilization");
        }
    }

    private HardwareMetricSnapshot GpuMetric()
    {
        if (!OperatingSystem.IsWindows())
            return Unavailable("gpu", "GPU", "GPU telemetry requires Windows performance counters.");

        EnsureGpuCounters();
        if (_gpuUtilizationCounters is null || _gpuUtilizationCounters.Count == 0)
            return Unavailable("gpu", "GPU", "Windows did not expose GPU Engine counters.");

        try
        {
            var values = _gpuUtilizationCounters
                .Select(counter => SafeNextValue(counter))
                .Where(value => value is not null)
                .Select(value => (double)value!.Value)
                .ToArray();

            if (values.Length == 0)
                return Unavailable("gpu", "GPU", "GPU counters could not be sampled.");

            var percent = Math.Clamp(values.Max(), 0d, 100d);
            return Percent("gpu", "GPU", percent, "Busiest Windows GPU engine");
        }
        catch
        {
            return Unavailable("gpu", "GPU", "GPU counters could not be sampled.");
        }
    }

    private HardwareMetricSnapshot RamMetric()
    {
        if (!OperatingSystem.IsWindows())
            return Unavailable("ram", "RAM", "System memory telemetry is unavailable.");

        var status = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(status) || status.TotalPhysical == 0)
            return Unavailable("ram", "RAM", "System memory telemetry is unavailable.");

        var used = status.TotalPhysical - status.AvailablePhysical;
        var percent = Math.Clamp(100d * used / status.TotalPhysical, 0d, 100d);
        return new HardwareMetricSnapshot
        {
            Key = "ram",
            Label = "RAM",
            Value = $"{percent:0.#}%",
            Detail = $"{HardwareReadings.FormatBytes(used)} / {HardwareReadings.FormatBytes(status.TotalPhysical)}",
            Percent = percent,
            Available = true
        };
    }

    private HardwareMetricSnapshot VramMetric()
    {
        if (!OperatingSystem.IsWindows())
            return Unavailable("vram", "VRAM", "VRAM telemetry requires Windows.");

        EnsureGpuCounters();
        var total = TryReadDedicatedVideoMemory();
        if ((_vramUsageCounters is null || _vramUsageCounters.Count == 0) && total is null)
            return Unavailable("vram", "VRAM", "Windows did not expose dedicated GPU memory telemetry.");

        try
        {
            long? used = null;
            if (_vramUsageCounters is { Count: > 0 })
            {
                var values = _vramUsageCounters
                    .Select(SafeNextValue)
                    .Where(value => value is not null)
                    .Select(value => (long)Math.Max(0, value!.Value))
                    .ToArray();
                if (values.Length > 0)
                    used = values.Sum();
            }

            return HardwareReadings.Vram(used, total);
        }
        catch
        {
            return Unavailable("vram", "VRAM", "VRAM counters could not be sampled.");
        }
    }

    [SupportedOSPlatform("windows")]
    private void EnsureGpuCounters()
    {
        lock (_sync)
        {
            if (_gpuCountersInitialized)
                return;

            _gpuCountersInitialized = true;
            _gpuUtilizationCounters = [];
            _vramUsageCounters = [];

            if (!OperatingSystem.IsWindows())
                return;

            try
            {
                if (PerformanceCounterCategory.Exists("GPU Engine"))
                {
                    var category = new PerformanceCounterCategory("GPU Engine");
                    foreach (var instance in category.GetInstanceNames())
                    {
                        try
                        {
                            _gpuUtilizationCounters.Add(
                                new PerformanceCounter("GPU Engine", "Utilization Percentage", instance, readOnly: true));
                        }
                        catch
                        {
                            // Individual inaccessible counters are ignored.
                        }
                    }
                }
            }
            catch
            {
                DisposeCounters(_gpuUtilizationCounters);
                _gpuUtilizationCounters = [];
            }

            try
            {
                if (PerformanceCounterCategory.Exists("GPU Adapter Memory"))
                {
                    var category = new PerformanceCounterCategory("GPU Adapter Memory");
                    foreach (var instance in category.GetInstanceNames())
                    {
                        try
                        {
                            _vramUsageCounters.Add(
                                new PerformanceCounter("GPU Adapter Memory", "Dedicated Usage", instance, readOnly: true));
                        }
                        catch
                        {
                            // Individual inaccessible counters are ignored.
                        }
                    }
                }
            }
            catch
            {
                DisposeCounters(_vramUsageCounters);
                _vramUsageCounters = [];
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static float? SafeNextValue(PerformanceCounter counter)
    {
        try { return counter.NextValue(); }
        catch { return null; }
    }

    [SupportedOSPlatform("windows")]
    private static long? TryReadDedicatedVideoMemory()
    {
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\DirectX");
            if (root is null)
                return null;

            long best = 0;
            foreach (var name in root.GetSubKeyNames())
            {
                using var adapter = root.OpenSubKey(name);
                var value = adapter?.GetValue("DedicatedVideoMemory");
                var bytes = value switch
                {
                    long number => number,
                    int number => number,
                    _ => 0
                };
                best = Math.Max(best, bytes);
            }

            return best > 0 ? best : null;
        }
        catch
        {
            return null;
        }
    }

    private static HardwareMetricSnapshot Percent(string key, string label, double percent, string detail) => new()
    {
        Key = key,
        Label = label,
        Value = $"{percent:0.#}%",
        Detail = detail,
        Percent = percent,
        Available = true
    };

    private static HardwareMetricSnapshot UnavailableTemperature(string key, string label) =>
        Unavailable(key, label, "No trusted generic temperature provider is available yet.");

    private static HardwareMetricSnapshot Unavailable(string key, string label, string detail) => new()
    {
        Key = key,
        Label = label,
        Value = "unavailable",
        Detail = detail,
        Available = false
    };

    private static ulong ToUInt64(FileTime value) => ((ulong)value.High << 32) | value.Low;

    public void Dispose()
    {
        lock (_sync)
        {
            DisposeCounters(_gpuUtilizationCounters);
            DisposeCounters(_vramUsageCounters);
            _gpuUtilizationCounters = null;
            _vramUsageCounters = null;
        }
    }

    private static void DisposeCounters(IEnumerable<PerformanceCounter>? counters)
    {
        if (counters is null)
            return;
        foreach (var counter in counters)
            counter.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);
}
