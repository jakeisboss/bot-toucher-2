using BotToucher2.Core.Hardware;
using Xunit;
namespace BotToucher2.Core.Tests;
public sealed class HardwareTests
{
    [Fact] public async Task Close_cancels_inflight_capture_and_disposes_only_after_it_finishes()
    {
        var sampler = new HeldSampler();
        using var stop = new CancellationTokenSource();
        var monitor = new HardwareMonitor(sampler, TimeSpan.FromMilliseconds(1));
        var run = monitor.RunAsync(stop.Token);
        await sampler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        stop.Cancel();
        Assert.True(sampler.CaptureToken.IsCancellationRequested);
        Assert.False(sampler.Disposed);
        Assert.False(run.IsCompleted);
        sampler.Release.SetResult();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(sampler.Disposed);
        Assert.Equal(1, sampler.Calls);
    }
    [Fact] public async Task Sampler_failure_is_reported_and_next_sample_still_arrives()
    {
        var sampler = new RecoveringSampler();
        using var stop = new CancellationTokenSource();
        var observations = new List<HardwareObservation>();
        var monitor = new HardwareMonitor(sampler, TimeSpan.FromMilliseconds(1));
        monitor.Observed += observation =>
        {
            observations.Add(observation);
            if (observation.Snapshot is not null) stop.Cancel();
        };
        await monitor.RunAsync(stop.Token).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, observations.Count);
        Assert.Null(observations[0].Snapshot);
        Assert.NotNull(observations[0].Notice);
        Assert.Equal("CPU", Assert.Single(observations[1].Snapshot!.Metrics).Label);
        Assert.Null(observations[1].Notice);
        Assert.True(sampler.Disposed);
    }
    [Fact] public async Task Cancellation_before_start_still_disposes_sampler_without_capturing()
    {
        var sampler = new HeldSampler();
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await new HardwareMonitor(sampler, TimeSpan.FromSeconds(2)).RunAsync(stop.Token);
        Assert.True(sampler.Disposed);
        Assert.Equal(0, sampler.Calls);
    }
    [Fact] public async Task Monitor_cannot_be_started_twice_or_overlap_sampling()
    {
        var sampler = new HeldSampler();
        using var stop = new CancellationTokenSource();
        var monitor = new HardwareMonitor(sampler, TimeSpan.FromMilliseconds(1));
        var run = monitor.RunAsync(stop.Token);
        await sampler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<InvalidOperationException>(() => monitor.RunAsync(stop.Token));
        Assert.Equal(1, sampler.Calls);
        stop.Cancel(); sampler.Release.SetResult(); await run;
    }
    private sealed class HeldSampler : IHardwareSampler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken CaptureToken { get; private set; }
        public bool Disposed { get; private set; }
        public int Calls { get; private set; }
        public async Task<HardwareSnapshot> CaptureAsync(CancellationToken token)
        {
            Calls++; CaptureToken = token; Started.TrySetResult();
            await Release.Task; // Simulates native sampling that must drain even after cancellation.
            return new HardwareSnapshot();
        }
        public void Dispose() => Disposed = true;
    }
    private sealed class RecoveringSampler : IHardwareSampler
    {
        private int _calls;
        public bool Disposed { get; private set; }
        public Task<HardwareSnapshot> CaptureAsync(CancellationToken token)
        {
            if (++_calls == 1) throw new IOException("Counter unavailable");
            return Task.FromResult(new HardwareSnapshot { Metrics = new[] {
                new HardwareMetricSnapshot { Key = "cpu", Label = "CPU", Value = "sampling…", Available = true } } });
        }
        public void Dispose() => Disposed = true;
    }
}
