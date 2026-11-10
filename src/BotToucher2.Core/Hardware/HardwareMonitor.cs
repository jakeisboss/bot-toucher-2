namespace BotToucher2.Core.Hardware;

/// <summary>The monitor exclusively owns this sampler, including disposal.</summary>
public interface IHardwareSampler : IDisposable
{
    Task<HardwareSnapshot> CaptureAsync(CancellationToken cancellationToken);
}
public sealed record HardwareObservation(HardwareSnapshot? Snapshot, string? Notice);

/// <summary>One serial polling loop per application. View lifetime never controls sampling.</summary>
public sealed class HardwareMonitor
{
    private readonly IHardwareSampler _sampler;
    private readonly TimeSpan _interval;
    private int _started;
    public event Action<HardwareObservation>? Observed;
    public HardwareMonitor(IHardwareSampler sampler, TimeSpan interval)
    {
        ArgumentNullException.ThrowIfNull(sampler);
        if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
        _sampler = sampler;
        _interval = interval;
    }
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("The hardware monitor can only be started once.");
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                HardwareObservation observation;
                try
                {
                    var sample = await _sampler.CaptureAsync(cancellationToken).ConfigureAwait(false);
                    observation = new(sample, null);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    observation = new(null, "Hardware sampling is unavailable. Retrying on the next interval.");
                }
                if (!cancellationToken.IsCancellationRequested) Observed?.Invoke(observation);
                await Task.Delay(_interval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally { _sampler.Dispose(); }
    }
}
