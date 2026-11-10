using BotToucher2.Core.Activity;
using Xunit;
namespace BotToucher2.Core.Tests;
public sealed class ActivityTests : IDisposable
{
    private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bt2-activity-" + Guid.NewGuid());
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    [Fact] public async Task Recent_events_are_real_newest_first_and_bounded()
    {
        var log = new ActivityLog(_root);
        Assert.Empty(await log.RecentAsync());
        await log.RecordAsync("START", "Application started");
        await log.RecordAsync("UPDATE_ERROR", "Hash mismatch", "Error");
        var events = await log.RecentAsync(1);
        Assert.Equal("UPDATE_ERROR", Assert.Single(events).Code);
        Assert.Equal("Error", events[0].Severity);
        Assert.Equal("Hash mismatch", events[0].Message);
        Assert.Equal(2, (await new ActivityLog(_root).RecentAsync()).Count);
    }
    [Fact] public async Task Concurrent_records_remain_complete_JSON_events()
    {
        var log = new ActivityLog(_root);
        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => log.RecordAsync("E" + i, "event")));
        Assert.Equal(20, (await log.RecentAsync()).Select(e => e.Code).Distinct().Count());
    }
    [Fact] public async Task Malformed_log_tail_does_not_erase_valid_activity()
    {
        var log = new ActivityLog(_root);
        await log.RecordAsync("START", "started");
        await File.AppendAllTextAsync(log.Path, "invalid tail\n");
        Assert.Equal("START", Assert.Single(await log.RecentAsync()).Code);
    }
}
