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
        var input = Path.Combine(root, "input"); CreateInput(input); Directory.CreateDirectory(Path.Combine(input, "engine"));
        await File.WriteAllTextAsync(Path.Combine(input, "app", "app.exe"), "application");
        await File.WriteAllTextAsync(Path.Combine(input, "engine", "updater.exe"), "engine");
        var manifest = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input));
        var engine = new UpdateEngine(Path.Combine(root, "install-root"));
        var stage = await engine.StageAsync(manifest, new Assets(input, manifest));
        Assert.Null((await engine.StatusAsync()).Current);
        await engine.ApplyAsync(stage, new Launcher(true));
        Assert.False(Directory.Exists(Path.Combine(root, "install-root", "updates", "staging", stage)));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(root, "install-root", "updates", "snapshots")));
        var status = await engine.StatusAsync();
        Assert.Equal(manifest.TargetId, status.Current!.InventoryId);
        Assert.Equal("application", await File.ReadAllTextAsync(Path.Combine(status.AppDirectory!, "app.exe")));
    }
    [Fact]
    public async Task FailedReadinessKeepsCurrentIdentity()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); CreateInput(input);
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
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); CreateInput(input);
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
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); CreateInput(input);
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
        Assert.NotNull(status.DisplacedStateDirectory);
        Assert.Equal("new state", await File.ReadAllTextAsync(Path.Combine(status.DisplacedStateDirectory!, "prefs")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.RollbackAsync(new Launcher(true)));
        await engine.AcknowledgeDisplacedStateAsync();
        await engine.RollbackAsync(new Launcher(true));
        Assert.Equal(second.TargetId, (await engine.StatusAsync()).Current!.InventoryId);
    }
    [Fact]
    public async Task AlteredStageMetadataCannotBeApplied()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); CreateInput(input);
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
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); CreateInput(input);
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "one"); var engine = new UpdateEngine(Path.Combine(root, "root"));
        var first = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input)); await engine.ApplyAsync(await engine.StageAsync(first, new Assets(input, first)), new Launcher(true));
        var wrong = UpdatePlanner.CreateManifest("0.2", first.Target, first.Target, "0.0");
        await Assert.ThrowsAsync<InvalidDataException>(() => engine.StageAsync(wrong, new Assets(input, wrong)));
    }
    [Fact]
    public async Task ChangedInstalledBytesBlockStaging()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); CreateInput(input);
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "one"); var engine = new UpdateEngine(Path.Combine(root, "root"));
        var first = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input)); await engine.ApplyAsync(await engine.StageAsync(first, new Assets(input, first)), new Launcher(true));
        await engine.VerifyCurrentAsync();
        var status = await engine.StatusAsync(); await File.WriteAllTextAsync(Path.Combine(status.AppDirectory!, "a"), "tampered");
        await Assert.ThrowsAsync<InvalidDataException>(() => engine.VerifyCurrentAsync());
        var next = UpdatePlanner.CreateManifest("0.2", first.Target, first.Target, "0.1");
        await Assert.ThrowsAsync<InvalidDataException>(() => engine.StageAsync(next, new Assets(input, next)));
    }
    [Fact]
    public async Task ExplicitFullRecoveryReplacesDamagedCurrentWithoutPromotingItToRollback()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); CreateInput(input);
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "one"); var engine = new UpdateEngine(Path.Combine(root, "root"));
        var first = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input)); await engine.ApplyAsync(await engine.StageAsync(first, new Assets(input, first)), new Launcher(true));
        var old = await engine.StatusAsync(); await File.WriteAllTextAsync(Path.Combine(old.AppDirectory!, "a"), "tampered");
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "recovered");
        var full = UpdatePlanner.CreateManifest("0.2", null, UpdatePlanner.Inventory(input));
        var stage = await engine.StageRecoveryAsync(full, new Assets(input, full)); await engine.ApplyAsync(stage, new Launcher(true));
        var status = await engine.StatusAsync(); Assert.Equal(full.TargetId, status.Current!.InventoryId); Assert.Null(status.Previous);
        Assert.False(Directory.Exists(old.AppDirectory));
        Assert.Contains(Directory.EnumerateFiles(Path.Combine(root, "root", "updates", "quarantine"), "a", SearchOption.AllDirectories), f => File.ReadAllText(f) == "tampered");
    }
    [Fact]
    public async Task WorkerCopyIsVerifiedAndIndependentOfDamagedApp()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input");
        CreateInput(input); Directory.CreateDirectory(Path.Combine(input, "engine"));
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "app"); await File.WriteAllTextAsync(Path.Combine(input, "engine", "worker"), "worker");
        var engine = new UpdateEngine(Path.Combine(root, "root")); var manifest = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input));
        await engine.ApplyAsync(await engine.StageAsync(manifest, new Assets(input, manifest)), new Launcher(true));
        var status = await engine.StatusAsync(); await File.WriteAllTextAsync(Path.Combine(status.AppDirectory!, "a"), "damaged");
        var worker = await engine.PrepareWorkerAsync(); Assert.Equal("worker", await File.ReadAllTextAsync(Path.Combine(worker, "worker")));
        Assert.Equal(worker, await engine.PrepareWorkerAsync());
        await File.WriteAllTextAsync(Path.Combine(status.EngineDirectory!, "worker"), "damaged");
        await Assert.ThrowsAsync<InvalidDataException>(() => engine.PrepareWorkerAsync());
    }
    [Fact]
    public async Task RejectsReparsePointInStagingControlPath()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input");
        CreateInput(input); await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "a");
        var engineRoot = Path.Combine(root, "root"); Directory.CreateDirectory(Path.Combine(engineRoot, "updates")); var outside = Path.Combine(root, "outside"); Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(Path.Combine(engineRoot, "updates", "staging"), outside);
        var manifest = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input)); var engine = new UpdateEngine(engineRoot);
        await Assert.ThrowsAsync<InvalidDataException>(() => engine.StageAsync(manifest, new Assets(input, manifest)));
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
    }
    [Fact]
    public async Task TamperedCurrentRecordIsRejectedAgainstDurableReceipt()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); CreateInput(input);
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "a"); var engineRoot = Path.Combine(root, "root"); var engine = new UpdateEngine(engineRoot);
        var manifest = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input)); await engine.ApplyAsync(await engine.StageAsync(manifest, new Assets(input, manifest)), new Launcher(true));
        var record = Path.Combine(engineRoot, "install", "current.json"); await File.WriteAllTextAsync(record, (await File.ReadAllTextAsync(record)).Replace("0.1", "0.2"));
        await Assert.ThrowsAsync<InvalidDataException>(() => engine.StatusAsync());
    }
    [Theory]
    [InlineData("published", false)]
    [InlineData("committed", true)]
    public async Task RecoveryDistinguishesInterruptedCommitPoint(string phase, bool committed)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); CreateInput(input);
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "one"); var engineRoot = Path.Combine(root, "root"); var initial = new UpdateEngine(engineRoot);
        var first = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input)); await initial.ApplyAsync(await initial.StageAsync(first, new Assets(input, first)), new Launcher(true));
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "two"); var next = UpdatePlanner.CreateManifest("0.2", first.Target, UpdatePlanner.Inventory(input), "0.1");
        var crashing = new UpdateEngine(engineRoot, checkpoint: p => { if (p == phase) throw new IOException("interrupted"); });
        var stage = await crashing.StageAsync(next, new Assets(input, next)); await Assert.ThrowsAsync<IOException>(() => crashing.ApplyAsync(stage, new Launcher(true)));
        Assert.True((await initial.StatusAsync()).Pending); await initial.RecoverAsync(new Launcher(true));
        var status = await initial.StatusAsync(); Assert.False(status.Pending); Assert.Equal(committed ? next.TargetId : first.TargetId, status.Current!.InventoryId);
    }
    [Fact]
    public async Task RecoveryRejectsAlteredJournalAndKeepsEvidence()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); CreateInput(input);
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "one"); var engineRoot = Path.Combine(root, "root");
        var manifest = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input)); var engine = new UpdateEngine(engineRoot, checkpoint: p => { if (p == "journaled") throw new IOException("interrupted"); });
        var stage = await engine.StageAsync(manifest, new Assets(input, manifest)); await Assert.ThrowsAsync<IOException>(() => engine.ApplyAsync(stage, new Launcher(true)));
        var journal = Path.Combine(engineRoot, "updates", "transactions", "pending.json"); await File.WriteAllTextAsync(journal, (await File.ReadAllTextAsync(journal)).Replace("0.1", "0.2"));
        await Assert.ThrowsAsync<InvalidDataException>(() => new UpdateEngine(engineRoot).RecoverAsync(new Launcher(true))); Assert.True(File.Exists(journal));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameInventoryRecoveryPreservesDamagedEvidenceAcrossInterruption(bool interrupt)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); CreateInput(input);
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "good"); var engineRoot = Path.Combine(root, "root"); var engine = new UpdateEngine(engineRoot);
        var full = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input)); await engine.ApplyAsync(await engine.StageAsync(full, new Assets(input, full)), new Launcher(true));
        var status = await engine.StatusAsync(); await File.WriteAllTextAsync(Path.Combine(status.AppDirectory!, "a"), "damaged");
        var recovering = new UpdateEngine(engineRoot, checkpoint: p => { if (interrupt && p == "published") throw new IOException("interrupt"); });
        var stage = await recovering.StageRecoveryAsync(full, new Assets(input, full));
        if (interrupt) { await Assert.ThrowsAsync<IOException>(() => recovering.ApplyAsync(stage, new Launcher(true))); await engine.RecoverAsync(new Launcher(true)); Assert.Equal("damaged", await File.ReadAllTextAsync(Path.Combine(status.AppDirectory!, "a"))); }
        else { await recovering.ApplyAsync(stage, new Launcher(true)); Assert.Equal("good", await File.ReadAllTextAsync(Path.Combine(status.AppDirectory!, "a"))); var quarantine = Path.Combine(engineRoot, "updates", "quarantine"); Assert.Contains(Directory.EnumerateFiles(quarantine, "a", SearchOption.AllDirectories), f => File.ReadAllText(f) == "damaged"); }
    }
    [Fact]
    public async Task RetainsOnePreviousAndTheOnePointZeroAnchor()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); CreateInput(input); var engineRoot = Path.Combine(root, "root"); var engine = new UpdateEngine(engineRoot);
        TransitionManifest? previous = null; var directories = new List<string>();
        foreach (var version in new[] { "0.1", "0.2", "1.0", "1.1", "1.2" })
        {
            await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), version);
            var next = UpdatePlanner.CreateManifest(version, previous?.Target, UpdatePlanner.Inventory(input), previous?.Version);
            await engine.ApplyAsync(await engine.StageAsync(next, new Assets(input, next)), new Launcher(true));
            directories.Add((await engine.StatusAsync()).AppDirectory!); previous = next;
        }
        Assert.False(Directory.Exists(directories[0])); Assert.False(Directory.Exists(directories[1]));
        Assert.True(Directory.Exists(directories[2])); Assert.True(Directory.Exists(directories[3])); Assert.True(Directory.Exists(directories[4]));
        Assert.Equal(3, Directory.GetFiles(Path.Combine(engineRoot, "updates", "transactions"), "*.receipt.json").Length);
    }
    [Fact]
    public async Task HistoryRecordsCommittedAndFailedTransactions()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); CreateInput(input);
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "one"); var engine = new UpdateEngine(Path.Combine(root, "root"));
        var first = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input)); await engine.ApplyAsync(await engine.StageAsync(first, new Assets(input, first)), new Launcher(true));
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "two"); var next = UpdatePlanner.CreateManifest("0.2", first.Target, UpdatePlanner.Inventory(input), "0.1");
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.ApplyAsync(engine.StageAsync(next, new Assets(input, next)).GetAwaiter().GetResult(), new Launcher(false)));
        var history = await engine.GetHistoryAsync(); Assert.Contains(history, h => h.Outcome == "committed" && h.Version == "0.1"); Assert.Contains(history, h => h.Outcome == "failed" && h.Version == "0.2");
    }
    [Fact]
    public async Task CancellationAfterReadinessStopsCandidateBeforeRestoringPrevious()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); CreateInput(input);
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "one"); var engine = new UpdateEngine(Path.Combine(root, "root"));
        var first = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input)); await engine.ApplyAsync(await engine.StageAsync(first, new Assets(input, first)), new Launcher(true));
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "two"); var next = UpdatePlanner.CreateManifest("0.2", first.Target, UpdatePlanner.Inventory(input), "0.1");
        var stage = await engine.StageAsync(next, new Assets(input, next)); using var cancel = new CancellationTokenSource(); var launcher = new CancelAfterReady(cancel);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.ApplyAsync(stage, launcher, cancel.Token));
        Assert.Equal(1, launcher.Stops); Assert.Equal(2, launcher.Launches); Assert.Equal(first.TargetId, (await engine.StatusAsync()).Current!.InventoryId);
    }
    private sealed class CancelAfterReady(CancellationTokenSource cancel) : IApplicationLauncher
    {
        public bool IsRunning => true;
        public int Stops { get; private set; } public int Launches { get; private set; }
        public Task<bool> LaunchAsync(string directory, string token, CancellationToken ct) { if (++Launches == 1) cancel.Cancel(); return Task.FromResult(true); }
        public Task StopAsync(CancellationToken ct) { Stops++; return Task.CompletedTask; }
    }
    [Fact]
    public async Task InterruptedVersionPruningResumesFromVerifiedQuarantine()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); CreateInput(input); var engineRoot = Path.Combine(root, "root");
        var engine = new UpdateEngine(engineRoot); TransitionManifest? previous = null; string? oldest = null;
        foreach (var version in new[] { "0.1", "0.2" }) { await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), version); var manifest = UpdatePlanner.CreateManifest(version, previous?.Target, UpdatePlanner.Inventory(input), previous?.Version); await engine.ApplyAsync(await engine.StageAsync(manifest, new Assets(input, manifest)), new Launcher(true)); oldest ??= (await engine.StatusAsync()).AppDirectory; previous = manifest; }
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "0.3"); var next = UpdatePlanner.CreateManifest("0.3", previous!.Target, UpdatePlanner.Inventory(input), previous.Version);
        var crash = new UpdateEngine(engineRoot, checkpoint: p => { if (p == "prune-moved") throw new IOException("interrupted"); }); var stage = await crash.StageAsync(next, new Assets(input, next));
        await Assert.ThrowsAsync<IOException>(() => crash.ApplyAsync(stage, new Launcher(true))); await engine.RecoverAsync(new Launcher(true));
        Assert.False(Directory.Exists(oldest)); Assert.False((await engine.StatusAsync()).Pending);
        Assert.DoesNotContain(Directory.EnumerateDirectories(Path.Combine(engineRoot, "updates", "deleting"), "*", SearchOption.AllDirectories), d => File.Exists(Path.Combine(d, "app", "a")));
    }
    [Theory]
    [InlineData("rollback-restored", false)]
    [InlineData("rollback-committed", true)]
    public async Task InterruptedRollbackRespectsCommitPointAndState(string phase, bool committed)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); CreateInput(input); var engineRoot = Path.Combine(root, "root"); var engine = new UpdateEngine(engineRoot);
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "one"); var first = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input)); await engine.ApplyAsync(await engine.StageAsync(first, new Assets(input, first)), new Launcher(true));
        Directory.CreateDirectory(Path.Combine(engineRoot, "state")); await File.WriteAllTextAsync(Path.Combine(engineRoot, "state", "prefs"), "old"); await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "two");
        var second = UpdatePlanner.CreateManifest("0.2", first.Target, UpdatePlanner.Inventory(input), "0.1"); await engine.ApplyAsync(await engine.StageAsync(second, new Assets(input, second)), new Launcher(true));
        await File.WriteAllTextAsync(Path.Combine(engineRoot, "state", "prefs"), "new");
        var crash = new UpdateEngine(engineRoot, checkpoint: p => { if (p == phase) throw new IOException("interrupt"); }); await Assert.ThrowsAsync<IOException>(() => crash.RollbackAsync(new Launcher(true)));
        await engine.RecoverAsync(new Launcher(true)); Assert.Equal(committed ? first.TargetId : second.TargetId, (await engine.StatusAsync()).Current!.InventoryId);
        Assert.Equal(committed ? "old" : "new", await File.ReadAllTextAsync(Path.Combine(engineRoot, "state", "prefs")));
    }
    [Fact]
    public async Task InterruptedSnapshotPruningResumes()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); CreateInput(input); var engineRoot = Path.Combine(root, "root"); var engine = new UpdateEngine(engineRoot); TransitionManifest? previous = null;
        foreach (var version in new[] { "0.1", "0.2" }) { await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), version); var manifest = UpdatePlanner.CreateManifest(version, previous?.Target, UpdatePlanner.Inventory(input), previous?.Version); await engine.ApplyAsync(await engine.StageAsync(manifest, new Assets(input, manifest)), new Launcher(true)); previous = manifest; }
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "0.3"); var next = UpdatePlanner.CreateManifest("0.3", previous!.Target, UpdatePlanner.Inventory(input), previous.Version);
        var crash = new UpdateEngine(engineRoot, checkpoint: p => { if (p == "snapshot-app-deleted") throw new IOException("interrupted"); }); var stage = await crash.StageAsync(next, new Assets(input, next));
        await Assert.ThrowsAsync<IOException>(() => crash.ApplyAsync(stage, new Launcher(true))); await engine.RecoverAsync(new Launcher(true)); Assert.False((await engine.StatusAsync()).Pending);
    }
    [Fact]
    public async Task InterruptedResolvedSnapshotPruningResumes()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); CreateInput(input); var engineRoot = Path.Combine(root, "root"); var engine = new UpdateEngine(engineRoot); TransitionManifest? previous = null;
        foreach (var version in new[] { "0.1", "0.2" }) { await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), version); var manifest = UpdatePlanner.CreateManifest(version, previous?.Target, UpdatePlanner.Inventory(input), previous?.Version); await engine.ApplyAsync(await engine.StageAsync(manifest, new Assets(input, manifest)), new Launcher(true)); previous = manifest; }
        await engine.RollbackAsync(new Launcher(true)); await engine.AcknowledgeDisplacedStateAsync();
        var crash = new UpdateEngine(engineRoot, checkpoint: p => { if (p == "snapshot-app-deleted") throw new IOException("interrupted"); });
        await Assert.ThrowsAsync<IOException>(() => crash.RollbackAsync(new Launcher(true))); await engine.RecoverAsync(new Launcher(true)); Assert.False((await engine.StatusAsync()).Pending);
    }
    [Fact]
    public async Task IdenticalAssetsAcrossAppAndEngineAreTransferredOnce()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); CreateInput(input); Directory.CreateDirectory(Path.Combine(input, "engine"));
        await File.WriteAllTextAsync(Path.Combine(input, "app", "runtime"), "same bytes"); await File.WriteAllTextAsync(Path.Combine(input, "engine", "runtime"), "same bytes");
        var manifest = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input)); var assets = new CountingAssets(input, manifest); var engine = new UpdateEngine(Path.Combine(root, "root"));
        await engine.StageAsync(manifest, assets); Assert.Equal(11, manifest.RequiredBytes); Assert.Equal(2, assets.Opens);
    }
    private sealed class CountingAssets(string root, TransitionManifest manifest) : IAssetSource
    {
        public int Opens { get; private set; }
        public Task<Stream> OpenAsync(string name, CancellationToken ct) { Opens++; return Task.FromResult<Stream>(File.OpenRead(Path.Combine(root, manifest.Target.First(f => f.Sha256 == name).Path.Replace('/', Path.DirectorySeparatorChar)))); }
    }
    [Fact]
    public async Task FailedPayloadHashLeavesNoOwnedPartialStage()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); CreateInput(input);
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "good"); var manifest = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input)); var engineRoot = Path.Combine(root, "root");
        await Assert.ThrowsAsync<InvalidDataException>(() => new UpdateEngine(engineRoot).StageAsync(manifest, new BadAssets()));
        var stages = Path.Combine(engineRoot, "updates", "staging"); Assert.Empty(Directory.EnumerateDirectories(stages));
    }
    private sealed class BadAssets : IAssetSource { public Task<Stream> OpenAsync(string name, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("evil"))); }
    [Fact]
    public async Task ExitedCandidateIsNotCommittedAfterReadiness()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); CreateInput(input);
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "one"); var engine = new UpdateEngine(Path.Combine(root, "root"));
        var first = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input)); await engine.ApplyAsync(await engine.StageAsync(first, new Assets(input, first)), new Launcher(true));
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "two"); var next = UpdatePlanner.CreateManifest("0.2", first.Target, UpdatePlanner.Inventory(input), "0.1"); var launcher = new ExitedLauncher();
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.ApplyAsync(engine.StageAsync(next, new Assets(input, next)).GetAwaiter().GetResult(), launcher));
        Assert.Equal(first.TargetId, (await engine.StatusAsync()).Current!.InventoryId); Assert.Equal(1, launcher.Stops); Assert.Equal(2, launcher.Launches);
    }
    private sealed class ExitedLauncher : IApplicationLauncher
    {
        public bool IsRunning => false; public int Stops { get; private set; } public int Launches { get; private set; }
        public Task<bool> LaunchAsync(string directory, string token, CancellationToken ct) { Launches++; return Task.FromResult(true); }
        public Task StopAsync(CancellationToken ct) { Stops++; return Task.CompletedTask; }
    }
    [Fact]
    public async Task InstallRollbackAndReapplyRetainsDisplacedState()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); CreateInput(input); var engineRoot = Path.Combine(root, "root"); var engine = new UpdateEngine(engineRoot);
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "one"); var first = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input)); await engine.ApplyAsync(await engine.StageAsync(first, new Assets(input, first)), new Launcher(true));
        Directory.CreateDirectory(Path.Combine(engineRoot, "state")); await File.WriteAllTextAsync(Path.Combine(engineRoot, "state", "prefs"), "old");
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "two"); var next = UpdatePlanner.CreateManifest("0.2", first.Target, UpdatePlanner.Inventory(input), "0.1"); await engine.ApplyAsync(await engine.StageAsync(next, new Assets(input, next)), new Launcher(true));
        await File.WriteAllTextAsync(Path.Combine(engineRoot, "state", "prefs"), "new"); await engine.RollbackAsync(new Launcher(true)); var displaced = (await engine.StatusAsync()).DisplacedStateDirectory!;
        await engine.ApplyAsync(await engine.StageAsync(next, new Assets(input, next)), new Launcher(true)); var status = await engine.StatusAsync(); Assert.Equal(next.TargetId, status.Current!.InventoryId); Assert.Equal(first.TargetId, status.Previous!.InventoryId); Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(displaced, "prefs")));
    }
    [Fact]
    public async Task ApplyRefusesWhileAppOwnsInstanceLock()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); CreateInput(input);
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "a"); var engineRoot = Path.Combine(root, "root"); var engine = new UpdateEngine(engineRoot); var manifest = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input)); var stage = await engine.StageAsync(manifest, new Assets(input, manifest));
        Directory.CreateDirectory(Path.Combine(engineRoot, "control")); using var appLock = new FileStream(Path.Combine(engineRoot, "control", "app.instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        await Assert.ThrowsAsync<IOException>(() => engine.ApplyAsync(stage, new Launcher(true))); Assert.False((await engine.StatusAsync()).Pending);
    }
    [Fact]
    public async Task FailedCandidateDoesNotLeaveAnAcceptedVersionDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); CreateInput(input); var engineRoot = Path.Combine(root, "root"); var engine = new UpdateEngine(engineRoot);
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "one"); var first = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input)); await engine.ApplyAsync(await engine.StageAsync(first, new Assets(input, first)), new Launcher(true));
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "two"); var second = UpdatePlanner.CreateManifest("0.2", first.Target, UpdatePlanner.Inventory(input), "0.1");
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.ApplyAsync(engine.StageAsync(second, new Assets(input, second)).GetAwaiter().GetResult(), new FailOnceLauncher()));
        Assert.False(Directory.Exists(Path.Combine(engineRoot, "install", "versions", second.TargetId))); Assert.Equal(first.TargetId, (await engine.StatusAsync()).Current!.InventoryId);
    }
    private sealed class FailOnceLauncher : IApplicationLauncher
    {
        private int calls; public bool IsRunning => true;
        public Task<bool> LaunchAsync(string directory, string token, CancellationToken ct) => Task.FromResult(++calls > 1);
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
    [Fact]
    public async Task CandidateExitAfterReceiptPreventsCommit()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); CreateInput(input); var engineRoot = Path.Combine(root, "root"); var engine = new UpdateEngine(engineRoot);
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "one"); var first = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input)); await engine.ApplyAsync(await engine.StageAsync(first, new Assets(input, first)), new Launcher(true));
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "two"); var second = UpdatePlanner.CreateManifest("0.2", first.Target, UpdatePlanner.Inventory(input), "0.1"); var launcher = new MutableLauncher();
        var closing = new UpdateEngine(engineRoot, checkpoint: p => { if (p == "receipt-written") launcher.Running = false; }); var stage = await closing.StageAsync(second, new Assets(input, second));
        await Assert.ThrowsAsync<InvalidOperationException>(() => closing.ApplyAsync(stage, launcher)); Assert.Equal(first.TargetId, (await engine.StatusAsync()).Current!.InventoryId); Assert.Equal(1, launcher.Stops);
        Assert.Single(Directory.GetFiles(Path.Combine(engineRoot, "updates", "transactions"), "*.receipt.json"));
    }
    [Fact]
    public async Task NormalStageRejectsSameOrOlderVersionEvenWithDifferentInventory()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); CreateInput(input); var engine = new UpdateEngine(Path.Combine(root, "root"));
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "one"); var first = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input)); await engine.ApplyAsync(await engine.StageAsync(first, new Assets(input, first)), new Launcher(true));
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "two"); var same = UpdatePlanner.CreateManifest("0.1.0", first.Target, UpdatePlanner.Inventory(input), "0.1");
        await Assert.ThrowsAsync<InvalidDataException>(() => engine.StageAsync(same, new Assets(input, same)));
    }
    private sealed class MutableLauncher : IApplicationLauncher
    {
        public bool Running { get; set; } = true; public bool IsRunning => Running; public int Stops { get; private set; }
        public Task<bool> LaunchAsync(string directory, string token, CancellationToken ct) => Task.FromResult(true);
        public Task StopAsync(CancellationToken ct) { Stops++; return Task.CompletedTask; }
    }
    [Theory]
    [InlineData("state-copy-file")]
    [InlineData("state-displaced")]
    [InlineData("state-cleanup-entry")]
    public async Task InterruptedStateRestoreCanBeRetriedRepeatedly(string phase)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); CreateInput(input); var engineRoot = Path.Combine(root, "root"); var engine = new UpdateEngine(engineRoot);
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "one"); var first = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input)); await engine.ApplyAsync(await engine.StageAsync(first, new Assets(input, first)), new Launcher(true));
        Directory.CreateDirectory(Path.Combine(engineRoot, "state")); await File.WriteAllTextAsync(Path.Combine(engineRoot, "state", "one"), "old1"); await File.WriteAllTextAsync(Path.Combine(engineRoot, "state", "two"), "old2");
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "two"); var second = UpdatePlanner.CreateManifest("0.2", first.Target, UpdatePlanner.Inventory(input), "0.1"); await engine.ApplyAsync(await engine.StageAsync(second, new Assets(input, second)), new Launcher(true));
        await File.WriteAllTextAsync(Path.Combine(engineRoot, "state", "one"), "new1"); await File.WriteAllTextAsync(Path.Combine(engineRoot, "state", "two"), "new2");
        var interrupted = new UpdateEngine(engineRoot, checkpoint: p => { if (p == phase) throw new IOException("interrupt"); }); await Assert.ThrowsAsync<IOException>(() => interrupted.RollbackAsync(new Launcher(true)));
        var retry = new UpdateEngine(engineRoot, checkpoint: p => { if (p == "state-copy-file") throw new IOException("retry interrupt"); }); await Assert.ThrowsAsync<IOException>(() => retry.RecoverAsync(new Launcher(true)));
        await engine.RecoverAsync(new Launcher(true)); Assert.False((await engine.StatusAsync()).Pending);
        Assert.Equal("new1", await File.ReadAllTextAsync(Path.Combine(engineRoot, "state", "one"))); Assert.Equal("new2", await File.ReadAllTextAsync(Path.Combine(engineRoot, "state", "two")));
    }
    [Fact]
    public async Task CommittedRecoveryRelaunchesAndKeepsJournalIfLaunchFails()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); CreateInput(input); var engineRoot = Path.Combine(root, "root");
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "one"); var manifest = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input));
        var crash = new UpdateEngine(engineRoot, checkpoint: p => { if (p == "committed") throw new IOException("interrupt"); }); var stage = await crash.StageAsync(manifest, new Assets(input, manifest)); await Assert.ThrowsAsync<IOException>(() => crash.ApplyAsync(stage, new Launcher(true)));
        var engine = new UpdateEngine(engineRoot); var failed = new CountingLauncher(false); await Assert.ThrowsAsync<InvalidOperationException>(() => engine.RecoverAsync(failed)); Assert.Equal(1, failed.Calls); Assert.True((await engine.StatusAsync()).Pending);
        var succeeded = new CountingLauncher(true); await engine.RecoverAsync(succeeded); Assert.Equal(1, succeeded.Calls); Assert.False((await engine.StatusAsync()).Pending); Assert.Equal(manifest.TargetId, (await engine.StatusAsync()).Current!.InventoryId);
    }
    private sealed class CountingLauncher(bool ready) : IApplicationLauncher
    {
        public int Calls { get; private set; } public bool IsRunning => ready;
        public Task<bool> LaunchAsync(string directory, string token, CancellationToken ct) { Calls++; return Task.FromResult(ready); }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
    [Fact]
    public async Task InterruptedWorkerCopyIsQuarantinedAndRebuilt()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); CreateInput(input); Directory.CreateDirectory(Path.Combine(input, "engine"));
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "app"); await File.WriteAllTextAsync(Path.Combine(input, "engine", "one"), new string('x', 100000)); await File.WriteAllTextAsync(Path.Combine(input, "engine", "two"), "two");
        var engineRoot = Path.Combine(root, "root"); var engine = new UpdateEngine(engineRoot); var manifest = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input)); await engine.ApplyAsync(await engine.StageAsync(manifest, new Assets(input, manifest)), new Launcher(true));
        var crash = new UpdateEngine(engineRoot, checkpoint: p => { if (p == "worker-copy-file") throw new IOException("interrupt"); }); await Assert.ThrowsAsync<IOException>(() => crash.PrepareWorkerAsync());
        var worker = await engine.PrepareWorkerAsync(); Assert.Equal(new string('x', 100000), await File.ReadAllTextAsync(Path.Combine(worker, "one"))); Assert.Equal("two", await File.ReadAllTextAsync(Path.Combine(worker, "two")));
        Assert.NotEmpty(Directory.EnumerateDirectories(Path.Combine(engineRoot, "control", "workers", "quarantine")));
    }
    [Fact]
    public async Task RepeatedFullRecoveryPrunesSupersededReceipts()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); CreateInput(input);
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "one"); var engineRoot = Path.Combine(root, "root"); var engine = new UpdateEngine(engineRoot); var full = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input));
        await engine.ApplyAsync(await engine.StageAsync(full, new Assets(input, full)), new Launcher(true));
        for (var i = 0; i < 3; i++) await engine.ApplyAsync(await engine.StageRecoveryAsync(full, new Assets(input, full)), new Launcher(true));
        Assert.Single(Directory.GetFiles(Path.Combine(engineRoot, "updates", "transactions"), "*.receipt.json"));
    }
    [Fact]
    public async Task InterruptedRestoreAttemptCleanupCanBeRetried()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); CreateInput(input); var engineRoot = Path.Combine(root, "root"); var engine = new UpdateEngine(engineRoot);
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "one"); var first = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input)); await engine.ApplyAsync(await engine.StageAsync(first, new Assets(input, first)), new Launcher(true));
        Directory.CreateDirectory(Path.Combine(engineRoot, "state")); await File.WriteAllTextAsync(Path.Combine(engineRoot, "state", "a"), new string('a', 100000)); await File.WriteAllTextAsync(Path.Combine(engineRoot, "state", "b"), "old");
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "two"); var second = UpdatePlanner.CreateManifest("0.2", first.Target, UpdatePlanner.Inventory(input), "0.1"); await engine.ApplyAsync(await engine.StageAsync(second, new Assets(input, second)), new Launcher(true));
        await File.WriteAllTextAsync(Path.Combine(engineRoot, "state", "b"), "new");
        var crash = new UpdateEngine(engineRoot, checkpoint: p => { if (p == "state-copy-file") throw new IOException("interrupt copy"); }); await Assert.ThrowsAsync<IOException>(() => crash.RollbackAsync(new Launcher(true)));
        var crashAgain = new UpdateEngine(engineRoot, checkpoint: p => { if (p == "state-attempt-cleanup-entry") throw new IOException("interrupt cleanup"); }); await Assert.ThrowsAsync<IOException>(() => crashAgain.RecoverAsync(new Launcher(true)));
        await engine.RecoverAsync(new Launcher(true)); Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(engineRoot, "state", "b")));
        Assert.DoesNotContain(Directory.EnumerateDirectories(Path.Combine(engineRoot, "updates", "restores"), "*", SearchOption.AllDirectories), path => Path.GetFileName(path).Contains("-attempt-", StringComparison.Ordinal));
    }
    [Fact]
    public async Task MissingUpdaterEntrypointIsRejectedBeforeAssetReadsAndPreservesRollback()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); CreateInput(input); var engine = new UpdateEngine(Path.Combine(root, "root"));
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "one"); var first = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input)); await engine.ApplyAsync(await engine.StageAsync(first, new Assets(input, first)), new Launcher(true));
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "two"); var second = UpdatePlanner.CreateManifest("0.2", first.Target, UpdatePlanner.Inventory(input), "0.1"); await engine.ApplyAsync(await engine.StageAsync(second, new Assets(input, second)), new Launcher(true));
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "three"); var third = UpdatePlanner.CreateManifest("0.3", second.Target, UpdatePlanner.Inventory(input), "0.2");
        var invalid = third with { Target = third.Target.Where(x => x.Path != "engine/BotToucher2.Updater.exe").ToArray() }; var assets = new CountingAssets(input, third);
        await Assert.ThrowsAsync<InvalidDataException>(() => engine.StageAsync(invalid, assets)); Assert.Equal(0, assets.Opens);
        var status = await engine.StatusAsync(); Assert.Equal(second.TargetId, status.Current!.InventoryId); Assert.Equal(first.TargetId, status.Previous!.InventoryId);
    }
    [Fact]
    public async Task IncompleteDeterministicWorkerIsPreservedAndRebuilt()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); CreateInput(input); var engineRoot = Path.Combine(root, "root"); var engine = new UpdateEngine(engineRoot);
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "app"); await File.WriteAllTextAsync(Path.Combine(input, "engine", "dependency"), "dependency"); var manifest = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input)); await engine.ApplyAsync(await engine.StageAsync(manifest, new Assets(input, manifest)), new Launcher(true));
        var worker = await engine.PrepareWorkerAsync(); File.Delete(Path.Combine(worker, "dependency"));
        Assert.Equal(worker, await engine.PrepareWorkerAsync()); Assert.Equal("dependency", await File.ReadAllTextAsync(Path.Combine(worker, "dependency")));
        Assert.NotEmpty(Directory.EnumerateDirectories(Path.Combine(engineRoot, "control", "workers", "quarantine")));
    }
    [Fact]
    public async Task InterruptedFullRecoveryReturnsDamagedCurrentFromQuarantine()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); CreateInput(input); var engineRoot = Path.Combine(root, "root"); var engine = new UpdateEngine(engineRoot);
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "one"); var first = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input)); await engine.ApplyAsync(await engine.StageAsync(first, new Assets(input, first)), new Launcher(true));
        var old = await engine.StatusAsync(); await File.WriteAllTextAsync(Path.Combine(old.AppDirectory!, "a"), "damage"); await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "recovery");
        var target = UpdatePlanner.CreateManifest("0.2", null, UpdatePlanner.Inventory(input)); var crash = new UpdateEngine(engineRoot, checkpoint: p => { if (p == "quarantined-current") throw new IOException("interrupt"); });
        var stage = await crash.StageRecoveryAsync(target, new Assets(input, target)); await Assert.ThrowsAsync<IOException>(() => crash.ApplyAsync(stage, new Launcher(true)));
        await engine.RecoverAsync(new Launcher(true)); Assert.False((await engine.StatusAsync()).Pending); Assert.Equal("damage", await File.ReadAllTextAsync(Path.Combine(old.AppDirectory!, "a")));
    }
    [Fact]
    public async Task ExistingRecoveryEvidenceDoesNotBlockRetryOrDiscardPartialState()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); CreateInput(input); var engineRoot = Path.Combine(root, "root"); var engine = new UpdateEngine(engineRoot);
        await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "one"); var first = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input)); await engine.ApplyAsync(await engine.StageAsync(first, new Assets(input, first)), new Launcher(true));
        Directory.CreateDirectory(Path.Combine(engineRoot, "state")); await File.WriteAllTextAsync(Path.Combine(engineRoot, "state", "prefs"), "old"); await File.WriteAllTextAsync(Path.Combine(input, "app", "a"), "two");
        var second = UpdatePlanner.CreateManifest("0.2", first.Target, UpdatePlanner.Inventory(input), "0.1"); await engine.ApplyAsync(await engine.StageAsync(second, new Assets(input, second)), new Launcher(true)); await File.WriteAllTextAsync(Path.Combine(engineRoot, "state", "prefs"), "new");
        var crash = new UpdateEngine(engineRoot, checkpoint: p => { if (p == "rollback-restored") throw new IOException("interrupt"); }); await Assert.ThrowsAsync<IOException>(() => crash.RollbackAsync(new Launcher(true)));
        var journal = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(engineRoot, "updates", "transactions", "pending.json"))); var id = journal.RootElement.GetProperty("Journal").GetProperty("TransactionId").GetString()!;
        var evidence = Path.Combine(engineRoot, "updates", "recovery", id); Directory.CreateDirectory(evidence); await File.WriteAllTextAsync(Path.Combine(evidence, "original-evidence"), "preserve"); await File.WriteAllTextAsync(Path.Combine(engineRoot, "state", "prefs"), "partial");
        await engine.RecoverAsync(new Launcher(true)); Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(engineRoot, "state", "prefs"))); Assert.Equal("preserve", await File.ReadAllTextAsync(Path.Combine(evidence, "original-evidence")));
        Assert.Contains(Directory.EnumerateFiles(evidence, "prefs", SearchOption.AllDirectories), file => File.ReadAllText(file) == "partial");
    }
    [Fact]
    public async Task OversizedAtomicMetadataWritePreservesExistingFile()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); var path = Path.Combine(root, "record.json"); await File.WriteAllTextAsync(path, "original");
        var write = typeof(UpdateEngine).GetMethod("Write", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.MakeGenericMethod(typeof(string));
        var operation = (Task)write.Invoke(null, new object[] { path, new string('x', 4 * 1024 * 1024), CancellationToken.None })!;
        await Assert.ThrowsAsync<InvalidDataException>(() => operation); Assert.Equal("original", await File.ReadAllTextAsync(path));
    }
    [Fact]
    public async Task NullCurrentRecordIsRejectedInsteadOfTreatedAsInitialInstall()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input"); CreateInput(input); var engineRoot = Path.Combine(root, "root"); var engine = new UpdateEngine(engineRoot);
        var manifest = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input)); await engine.ApplyAsync(await engine.StageAsync(manifest, new Assets(input, manifest)), new Launcher(true));
        await File.WriteAllTextAsync(Path.Combine(engineRoot, "install", "current.json"), "null");
        await Assert.ThrowsAsync<InvalidDataException>(() => engine.StatusAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => engine.StageAsync(manifest, new Assets(input, manifest)));
    }
    private static void CreateInput(string input)
    {
        Directory.CreateDirectory(Path.Combine(input, "app")); Directory.CreateDirectory(Path.Combine(input, "engine"));
        File.WriteAllBytes(Path.Combine(input, "app", "BotToucher2.exe"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(input, "engine", "BotToucher2.Updater.exe"), new byte[] { 1 });
    }
    private sealed class FailingCandidate(string root) : IApplicationLauncher
    {
        public bool IsRunning => true;
        public int Calls { get; private set; }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
        public async Task<bool> LaunchAsync(string appDirectory, string transactionToken, CancellationToken cancellationToken)
        {
            Calls++; if (Calls == 1) { await File.WriteAllTextAsync(Path.Combine(root, "state", "prefs"), "after", cancellationToken); return false; } return true;
        }
    }
    private sealed class Launcher(bool ready) : IApplicationLauncher { public bool IsRunning => true; public Task StopAsync(CancellationToken ct) => Task.CompletedTask; public Task<bool> LaunchAsync(string appDirectory, string transactionToken, CancellationToken cancellationToken) => Task.FromResult(ready); }
    private sealed class Assets(string root, TransitionManifest manifest) : IAssetSource
    {
        public Task<Stream> OpenAsync(string assetName, CancellationToken cancellationToken) => Task.FromResult<Stream>(File.OpenRead(Path.Combine(root, manifest.Target.First(f => f.Sha256 == assetName).Path.Replace('/', Path.DirectorySeparatorChar))));
    }
}
