using BotToucher2.Updater;
using BotToucher2.Update;
using Xunit;
namespace BotToucher2.Release.Tests;
public sealed class DispatchTests
{
    [Theory]
    [InlineData("apply")][InlineData("install")][InlineData("install-release")][InlineData("rollback")][InlineData("recover")][InlineData("full-recover")]
    public void MutationsFromManagedVersionRequireWorker(string command)
    {
        var root = Path.Combine(Path.GetTempPath(), "bt2-dispatch-test");
        Assert.True(ExternalCommandDispatch.RequiresWorker(command, root, Path.Combine(root, "install", "versions", "old", "engine")));
        Assert.False(ExternalCommandDispatch.RequiresWorker(command, root, root + "-bootstrap"));
        Assert.False(ExternalCommandDispatch.RequiresWorker("status", root, Path.Combine(root, "install", "versions", "old", "engine")));
    }
    [Fact]
    public void ArgumentsPreservePathsAndValuesAndBindAbsoluteRoot()
    {
        var root = Path.GetFullPath("space root");
        var args = ExternalCommandDispatch.WorkerArguments(new[] { "install", "--manifest", "a file.json", "--assets", "a b", "--root", "space root" }, root, new string('a', 32), 123);
        Assert.Equal(new[] { "install", "--manifest", Path.GetFullPath("a file.json"), "--assets", Path.GetFullPath("a b"), "--root", root, "--dispatch-token", new string('a', 32), "--wait-dispatch-pid", "123" }, args);
    }
    [Fact]
    public void WorkerStartLeavesManagedCallerDirectoryAndResolvesOnlyFilesystemArguments()
    {
        var root = Path.Combine(Path.GetTempPath(), "bt2-dispatch-root");
        var caller = Path.Combine(root, "install", "versions", "old", "engine");
        var worker = Path.Combine(root, "control", "workers", "verified", "engine");
        var forwarded = ExternalCommandDispatch.WorkerArguments(new[] { "install", "--manifest", "../release.json", "--assets", "../assets", "--stage", "opaque-id", "--wait-pid", "123", "--notes", "notes/keep", "--confirm", "state-saved" }, "../../../..", new string('d', 32), 456, caller);
        var start = ExternalCommandDispatch.WorkerStartInfo(worker, forwarded);
        Assert.Equal(worker, start.WorkingDirectory); Assert.Equal(Path.Combine(worker, "BotToucher2.Updater.exe"), start.FileName);
        Assert.Equal(new[] { "install", "--manifest", Path.GetFullPath("../release.json", caller), "--assets", Path.GetFullPath("../assets", caller), "--stage", "opaque-id", "--wait-pid", "123", "--notes", "notes/keep", "--confirm", "state-saved", "--root", root, "--dispatch-token", new string('d', 32), "--wait-dispatch-pid", "456" }, start.ArgumentList);
    }
    [Fact]
    public async Task DurableResultsHaveOwnedPathsAndPreserveFailureDetails()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var token = new string('a', 32);
        Assert.Throws<InvalidDataException>(() => ExternalCommandDispatch.ResultPath(root, "../escape"));
        await ExternalCommandDispatch.WriteResultAsync(root, token, new("Failed", 1, "output", "candidate failed"));
        var result = await ExternalCommandDispatch.ReadResultAsync(root, token);
        Assert.Equal("Failed", result!.State); Assert.Equal(1, result.ExitCode); Assert.Equal("candidate failed", result.Error); Assert.Equal("output", result.Output);
    }
    [Theory]
    [InlineData("app")][InlineData("engine")]
    public async Task PackageRejectsOutputInsideInputBeforeCreatingBundle(string role)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var input = Path.Combine(root, role); Directory.CreateDirectory(input); var output = Path.Combine(input, "output");
        var result = await Program.Main(new[] { "package", "--app", Path.Combine(root, "app"), "--engine", Path.Combine(root, "engine"), "--out", output, "--version", "0.1", "--root", Path.Combine(root, "managed") });
        Assert.Equal(1, result); Assert.False(Directory.Exists(output));
    }
    [Fact]
    public async Task ManagedDispatchVerifiesWorkerAndStandaloneBootstrapSurvivesDamagedSelectedEngine()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input");
        Directory.CreateDirectory(Path.Combine(input, "app")); Directory.CreateDirectory(Path.Combine(input, "engine"));
        File.WriteAllText(Path.Combine(input, "app", "BotToucher2.exe"), "app"); File.WriteAllText(Path.Combine(input, "engine", "BotToucher2.Updater.exe"), "engine");
        var managed = Path.Combine(root, "managed"); var engine = new UpdateEngine(managed);
        var manifest = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input));
        await engine.ApplyAsync(await engine.StageAsync(manifest, new TestAssets(input, manifest)), new TestLauncher());
        var status = await engine.StatusAsync();
        File.WriteAllText(Path.Combine(status.AppDirectory!, "BotToucher2.exe"), "damaged app");
        var worker = await ExternalCommandDispatch.WorkerForAsync("full-recover", managed, status.EngineDirectory!);
        Assert.NotNull(worker); Assert.False(ExternalCommandDispatch.IsWithin(worker!, Path.Combine(managed, "install")));
        Assert.Null(await ExternalCommandDispatch.WorkerForAsync("full-recover", managed, worker!));
        var token = new string('b', 32); var args = ExternalCommandDispatch.WorkerArguments(new[] { "full-recover" }, managed, token, 123);
        await Assert.ThrowsAsync<InvalidDataException>(() => ExternalCommandDispatch.ValidateWorkerAsync(managed, status.EngineDirectory!, token, 123, args));
        File.WriteAllText(Path.Combine(status.EngineDirectory!, "BotToucher2.Updater.exe"), "damaged engine");
        await Assert.ThrowsAsync<InvalidDataException>(() => ExternalCommandDispatch.WorkerForAsync("full-recover", managed, status.EngineDirectory!));
        await Assert.ThrowsAsync<InvalidDataException>(() => ExternalCommandDispatch.WorkerForAsync("full-recover", managed, worker!));
        Assert.Null(await ExternalCommandDispatch.WorkerForAsync("full-recover", managed, input));
    }
    [Fact]
    public async Task WorkerHandoffRejectsForgedArgumentsBeforeWaitingOrMutation()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input");
        Directory.CreateDirectory(Path.Combine(input, "app")); Directory.CreateDirectory(Path.Combine(input, "engine"));
        File.WriteAllText(Path.Combine(input, "app", "BotToucher2.exe"), "app"); File.WriteAllText(Path.Combine(input, "engine", "BotToucher2.Updater.exe"), "engine");
        var managed = Path.Combine(root, "managed"); var engine = new UpdateEngine(managed);
        var manifest = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input));
        await engine.ApplyAsync(await engine.StageAsync(manifest, new TestAssets(input, manifest)), new TestLauncher());
        var worker = await engine.PrepareWorkerAsync(); var token = new string('c', 32);
        var args = ExternalCommandDispatch.WorkerArguments(new[] { "recover" }, managed, token, 123);
        var folder = Path.Combine(managed, "control", "commands"); Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, token + ".request.json"), System.Text.Json.JsonSerializer.Serialize(new { ParentPid = 123, Arguments = args, WorkerDirectory = worker }));
        await ExternalCommandDispatch.ValidateWorkerAsync(managed, worker, token, 123, args);
        var changed = args.ToArray(); changed[0] = "rollback";
        await Assert.ThrowsAsync<InvalidDataException>(() => ExternalCommandDispatch.ValidateWorkerAsync(managed, worker, token, 123, changed));
        await Assert.ThrowsAsync<InvalidDataException>(() => ExternalCommandDispatch.ValidateWorkerAsync(managed, worker, token, 456, args));
    }
    [Theory]
    [InlineData("recover", "Completed", 0)]
    [InlineData("rollback", "Failed", 1)]
    public async Task ManagedCliExitsBeforeRealWorkerPublishesFinalResult(string command, string state, int exitCode)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "input");
        Directory.CreateDirectory(Path.Combine(input, "app")); var sourceEngine = Path.Combine(input, "engine"); Directory.CreateDirectory(sourceEngine);
        File.WriteAllText(Path.Combine(input, "app", "BotToucher2.exe"), "unused application");
        foreach (var name in new[] { "BotToucher2.Updater.dll", "BotToucher2.Updater.deps.json", "BotToucher2.Updater.runtimeconfig.json", "BotToucher2.Update.dll", "BotToucher2.Core.dll" })
            File.Copy(Path.Combine(AppContext.BaseDirectory, name), Path.Combine(sourceEngine, name));
        File.Copy(Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "BotToucher2.Updater.exe" : "BotToucher2.Updater"), Path.Combine(sourceEngine, "BotToucher2.Updater.exe"));
        var managed = Path.Combine(root, "managed"); var engine = new UpdateEngine(managed);
        var manifest = UpdatePlanner.CreateManifest("0.1", null, UpdatePlanner.Inventory(input));
        await engine.ApplyAsync(await engine.StageAsync(manifest, new TestAssets(input, manifest)), new TestLauncher());
        var executable = Path.Combine((await engine.StatusAsync()).EngineDirectory!, "BotToucher2.Updater.exe");
        // The production package targets Windows. Give its native test-host apphost Unix execute permission solely for this portable subprocess test.
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var preparedWorker = await engine.PrepareWorkerAsync();
            File.SetUnixFileMode(Path.Combine(preparedWorker, "BotToucher2.Updater.exe"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        var start = new System.Diagnostics.ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! };
        start.Environment["DOTNET_ROOT"] = Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
        start.ArgumentList.Add(command); start.ArgumentList.Add("--root"); start.ArgumentList.Add(Path.GetRelativePath(start.WorkingDirectory, managed));
        start.ArgumentList.Add("--manifest"); start.ArgumentList.Add("../relative manifest.json");
        start.ArgumentList.Add("--assets"); start.ArgumentList.Add("../relative assets");
        using var process = System.Diagnostics.Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(process.ExitCode == 0, await stderr.WaitAsync(TimeSpan.FromSeconds(30)));
        var accepted = System.Text.Json.JsonDocument.Parse(await stdout.WaitAsync(TimeSpan.FromSeconds(30))).RootElement;
        Assert.Equal("Accepted", accepted.GetProperty("State").GetString());
        var path = accepted.GetProperty("ResultPath").GetString()!;
        var request = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(path.Replace(".result.json", ".request.json"))).RootElement;
        var forwarded = request.GetProperty("Arguments").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Contains(Path.GetFullPath("../relative manifest.json", start.WorkingDirectory), forwarded);
        Assert.Contains(Path.GetFullPath("../relative assets", start.WorkingDirectory), forwarded);
        Assert.Contains(managed, forwarded);
        var token = Path.GetFileName(path).Split('.')[0]; ExternalCommandResult? result = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (result?.State is not ("Completed" or "Failed")) { result = await ExternalCommandDispatch.ReadResultAsync(managed, token); await Task.Delay(10, timeout.Token); }
        Assert.Equal(state, result.State); Assert.Equal(exitCode, result.ExitCode);
        if (exitCode != 0) Assert.Contains("No rollback is available", result.Error);
        Assert.False((await engine.StatusAsync()).Pending);
    }
    private sealed class TestLauncher : IApplicationLauncher
    {
        public bool IsRunning => true;
        public Task<bool> LaunchAsync(string directory, string token, CancellationToken ct) => Task.FromResult(true);
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class TestAssets(string input, TransitionManifest manifest) : IAssetSource
    {
        public Task<Stream> OpenAsync(string name, CancellationToken ct) => Task.FromResult<Stream>(File.OpenRead(Path.Combine(input, manifest.Target.First(x => x.Sha256 == name).Path)));
    }
}
