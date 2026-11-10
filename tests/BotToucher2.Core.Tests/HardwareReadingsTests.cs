using BotToucher2.Core.Hardware;
using Xunit;
namespace BotToucher2.Core.Tests;
public sealed class HardwareReadingsTests
{
    [Theory]
    [InlineData(null, null, false, "unavailable")]
    [InlineData(null, 8192L, true, "8 KB total")]
    [InlineData(1024L, 2048L, true, "1 KB")]
    [InlineData(0L, null, true, "0 B")]
    public void Vram_never_claims_availability_without_a_reading(long? used, long? capacity, bool available, string value)
    {
        var reading = HardwareReadings.Vram(used, capacity);
        Assert.Equal(available, reading.Available);
        Assert.Equal(value, reading.Value);
        Assert.Null(reading.Percent); // Usage across adapters must not be divided by one adapter's capacity.
    }
}
