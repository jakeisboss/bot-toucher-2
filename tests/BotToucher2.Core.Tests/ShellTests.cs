using BotToucher2.Core.Shell;
using Xunit;
namespace BotToucher2.Core.Tests;
public sealed class ShellTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bt2-tests-" + Guid.NewGuid());
    private string StatePath => Path.Combine(_directory, "layout.json");
    public ShellTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);

    [Fact] public void Unknown_navigation_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => (new ShellState { Destination = (Destination)999 }).Validate());
        Assert.Throws<ArgumentException>(() => (new ShellState { PacketSection = (PacketSection)999 }).Validate());
        Assert.Throws<ArgumentException>(() => (new ShellState { RightTab = (RailTab)999 }).Validate());
    }
    [Theory][InlineData(double.NaN)][InlineData(double.PositiveInfinity)][InlineData(-1)][InlineData(5000)]
    public void Unsafe_rail_width_is_rejected(double width)
    {
        Assert.Throws<ArgumentException>(() => (new ShellState { LeftWidth = width }).Validate());
        Assert.Throws<ArgumentException>(() => (new ShellState { RightWidth = width }).Validate());
    }
    [Fact] public async Task Layout_round_trips_without_losing_navigation_or_rail_state()
    {
        var store = new ShellPreferences(StatePath);
        var initial = await store.LoadAsync();
        Assert.Null(initial.Notice);
        var state = new ShellState { Destination = Destination.PacketCenter, PacketSection = PacketSection.UpdateCenter,
            LeftVisible = false, RightVisible = false, LeftWidth = 220, RightWidth = 320, RightTab = RailTab.Widgets };
        Assert.Null(await store.SaveAsync(state));
        var loaded = await new ShellPreferences(StatePath).LoadAsync();
        Assert.Equal(state, loaded.State);
        Assert.Null(loaded.Notice);
    }
    [Theory][InlineData("broken JSON")][InlineData("null")][InlineData("{\"SchemaVersion\":99}")]
    [InlineData("{\"Destination\":999}")][InlineData("{\"LeftWidth\":0}")]
    public async Task Invalid_preferences_show_notice_use_defaults_and_preserve_original(string bytes)
    {
        await File.WriteAllTextAsync(StatePath, bytes);
        var store = new ShellPreferences(StatePath);
        var loaded = await store.LoadAsync();
        Assert.Equal(new ShellState(), loaded.State);
        Assert.False(string.IsNullOrWhiteSpace(loaded.Notice));
        Assert.NotNull(await store.SaveAsync(new ShellState { LeftVisible = false }));
        Assert.Equal(bytes, await File.ReadAllTextAsync(StatePath));
    }
    [Fact] public async Task Externally_changed_preferences_are_not_overwritten()
    {
        var store = new ShellPreferences(StatePath);
        await store.LoadAsync();
        await File.WriteAllTextAsync(StatePath, "external content");
        Assert.NotNull(await store.SaveAsync(new ShellState()));
        Assert.Equal("external content", await File.ReadAllTextAsync(StatePath));
    }
    [Fact] public async Task Unreadable_preferences_are_nonfatal_and_never_replaced()
    {
        Directory.CreateDirectory(StatePath);
        var store = new ShellPreferences(StatePath);
        Assert.NotNull((await store.LoadAsync()).Notice);
        Assert.NotNull(await store.SaveAsync(new ShellState()));
        Assert.True(Directory.Exists(StatePath));
    }
}
