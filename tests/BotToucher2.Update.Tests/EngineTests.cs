using System.Text;
using BotToucher2.Update;
using Xunit;

namespace BotToucher2.Update.Tests;

public class EngineTests
{
    [Fact]
    public async Task InitialInstallStagesAndCommitsOnlyAfterReadiness()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var input = Path.Combine(root, "input"); Directory.CreateDirectory(Path.Combine(input, "app")); Directory.CreateDirectory(Path.Combine(input, "engine"));
        await File.WriteAllTextAsync(Path.Combine(input, "app", "app.exe"), "application");
        await File.WriteAllTextAsync(Path.Combine(input, "engine", "updater.exe"), "engine");
        var manifest = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input));
        var engine = new UpdateEngine(Path.Combine(root, "install-root"));
        var stage = await engine.StageAsync(manifest, new Assets(input, manifest));
        Assert.Null((await engine.StatusAsync()).Current);
        await engine.ApplyAsync(stage, new Launcher(true));
        var status = await engine.StatusAsync();
        Assert.Equal(manifest.TargetId, status.Current!.InventoryId);
        Assert.Equal("application", await File.ReadAllTextAsync(Path.Combine(status.AppDirectory!, "app.exe")));
    }
    [Fact]
    public async Task FailedReadinessKeepsCurrentIdentity()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); Directory.CreateDirectory(Path.Combine(input, "app"));
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "one");
        var first = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input)); var engine = new UpdateEngine(Path.Combine(root, "root"));
        await engine.ApplyAsync(await engine.StageAsync(first, new Assets(input, first)), new Launcher(true));
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "two");
        var second = UpdatePlanner.CreateManifest("0.2", first.Target, UpdatePlanner.Inventory(input), "0.1");
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.ApplyAsync(engineStage(), new Launcher(false)));
        Assert.Equal(first.TargetId, (await engine.StatusAsync()).Current!.InventoryId);
        string engineStage() => engine.StageAsync(second, new Assets(input, second)).GetAwaiter().GetResult();
    }
    [Fact]
    public async Task FailedCandidateRestoresStateAndRelaunchesPrevious()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); Directory.CreateDirectory(Path.Combine(input, "app"));
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "one");
        var engineRoot = Path.Combine(root, "root"); var engine = new UpdateEngine(engineRoot);
        var first = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input));
        await engine.ApplyAsync(await engine.StageAsync(first, new Assets(input, first)), new Launcher(true));
        Directory.CreateDirectory(Path.Combine(engineRoot, "state")); await File.WriteAllTextAsync(Path.Combine(engineRoot, "state", "prefs"), "before");
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "two");
        var second = UpdatePlanner.CreateManifest("0.2", first.Target, UpdatePlanner.Inventory(input), "0.1");
        var launcher = new FailingCandidate(engineRoot);
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.ApplyAsync(engine.StageAsync(second, new Assets(input, second)).GetAwaiter().GetResult(), launcher));
        Assert.Equal("before", await File.ReadAllTextAsync(Path.Combine(engineRoot, "state", "prefs")));
        Assert.Equal(2, launcher.Calls);
    }
    [Fact]
    public async Task RollbackRestoresPreviousAppAndItsState()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); Directory.CreateDirectory(Path.Combine(input, "app"));
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "one");
        var engineRoot = Path.Combine(root, "root"); var engine = new UpdateEngine(engineRoot);
        var first = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input));
        await engine.ApplyAsync(await engine.StageAsync(first, new Assets(input, first)), new Launcher(true));
        Directory.CreateDirectory(Path.Combine(engineRoot, "state")); await File.WriteAllTextAsync(Path.Combine(engineRoot, "state", "prefs"), "old state");
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "two");
        var second = UpdatePlanner.CreateManifest("0.2", first.Target, UpdatePlanner.Inventory(input), "0.1");
        await engine.ApplyAsync(await engine.StageAsync(second, new Assets(input, second)), new Launcher(true));
        await File.WriteAllTextAsync(Path.Combine(engineRoot, "state", "prefs"), "new state");
        await engine.RollbackAsync(new Launcher(true));
        var status = await engine.StatusAsync(); Assert.Equal(first.TargetId, status.Current!.InventoryId); Assert.Equal(second.TargetId, status.Previous!.InventoryId);
        Assert.Equal("old state", await File.ReadAllTextAsync(Path.Combine(engineRoot, "state", "prefs")));
    }
    [Fact]
    public async Task AlteredStageMetadataCannotBeApplied()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); Directory.CreateDirectory(Path.Combine(input, "app"));
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "one");
        var engineRoot = Path.Combine(root, "root"); var engine = new UpdateEngine(engineRoot);
        var manifest = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input));
        var stage = await engine.StageAsync(manifest, new Assets(input, manifest));
        Assert.Matches("^[0-9a-f]{32}-[0-9a-f]{64}$", stage);
        var path = Path.Combine(engineRoot, "updates", "staging", stage, "stage.json");
        var text = await File.ReadAllTextAsync(path); await File.WriteAllTextAsync(path, text.Replace("0.1", "0.2"));
        await Assert.ThrowsAsync<InvalidDataException>(() => engine.ApplyAsync(stage, new Launcher(true)));
    }
    [Fact]
    public async Task RejectsCorrectInventoryWithWrongSourceVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); Directory.CreateDirectory(Path.Combine(input, "app"));
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "one"); var engine = new UpdateEngine(Path.Combine(root, "root"));
        var first = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input)); await engine.ApplyAsync(await engine.StageAsync(first, new Assets(input, first)), new Launcher(true));
        var wrong = UpdatePlanner.CreateManifest("0.2", first.Target, first.Target, "0.0");
        await Assert.ThrowsAsync<InvalidDataException>(() => engine.StageAsync(wrong, new Assets(input, wrong)));
    }
    [Fact]
    public async Task ChangedInstalledBytesBlockStaging()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); Directory.CreateDirectory(Path.Combine(input, "app"));
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "one"); var engine = new UpdateEngine(Path.Combine(root, "root"));
        var first = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input)); await engine.ApplyAsync(await engine.StageAsync(first, new Assets(input, first)), new Launcher(true));
        var status = await engine.StatusAsync(); await File.WriteAllTextAsync(Path.Combine(status.AppDirectory!, "a"), "tampered");
        var next = UpdatePlanner.CreateManifest("0.2", first.Target, first.Target, "0.1");
        await Assert.ThrowsAsync<InvalidDataException>(() => engine.StageAsync(next, new Assets(input, next)));
    }
    private sealed class FailingCandidate(string root) : IApplicationLauncher
    {
        public int Calls { get; private set; }
        public async Task<bool> LaunchAsync(string appDirectory, string transactionToken, CancellationToken cancellationToken)
        {
            Calls++; if (Calls == 1) { await File.WriteAllTextAsync(Path.Combine(root, "state", "prefs"), "after", cancellationToken); return false; } return true;
        }
    }
    private sealed class Launcher(bool ready) : IApplicationLauncher { public Task<bool> LaunchAsync(string appDirectory, string transactionToken, CancellationToken cancellationToken) => Task.FromResult(ready); }
    private sealed class Assets(string root, TransitionManifest manifest) : IAssetSource
    {
        public Task<Stream> OpenAsync(string assetName, CancellationToken cancellationToken) => Task.FromResult<Stream>(File.OpenRead(Path.Combine(root, manifest.Target.First(f => f.Sha256 == assetName).Path.Replace('/', Path.DirectorySeparatorChar))));
    }
}
