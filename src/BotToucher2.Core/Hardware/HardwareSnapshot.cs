// Adapted from BT1 6c30c11, HardwareSnapshot.cs; namespace only.
namespace BotToucher2.Core.Hardware;

public sealed record HardwareMetricSnapshot
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required string Value { get; init; }
    public string? Detail { get; init; }
    public double? Percent { get; init; }
    public bool Available { get; init; }
}

public sealed record HardwareSnapshot
{
    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<HardwareMetricSnapshot> Metrics { get; init; } = [];
}
