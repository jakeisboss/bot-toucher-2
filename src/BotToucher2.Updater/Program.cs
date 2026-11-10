using System.Diagnostics;
using System.Text.Json;
using BotToucher2.Core.Activity;
using BotToucher2.Update;
using BotToucher2.Updater;

public static class Program
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static async Task<int> Main(string[] args)
    {
        try { return await DispatchAsync(args); }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }
    private static async Task<int> DispatchAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "--help") return await RunAsync(args);
        var options = Parse(args.Skip(1).ToArray());
        var root = Path.GetFullPath(options.GetValueOrDefault("root") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BotToucher2"));
        if (options.TryGetValue("dispatch-token", out var token))
        {
            if (!int.TryParse(Required(options, "wait-dispatch-pid"), out var parentPid)) throw new ArgumentException("Invalid dispatch parent PID.");
            await ExternalCommandDispatch.ValidateWorkerAsync(root, AppContext.BaseDirectory, token, parentPid, args);
            using var output = new StringWriter(); using var error = new StringWriter();
            var originalOut = Console.Out; var originalError = Console.Error;
            Console.SetOut(output); Console.SetError(error);
            var code = 1;
            try
            {
                await ExternalCommandDispatch.WriteResultAsync(root, token, new("Ready", null, "", ""));
                await ExternalCommandDispatch.WaitForExitAsync(parentPid);
                code = await RunAsync(args);
            }
            catch (Exception ex) { error.WriteLine(ex); }
            finally { Console.SetOut(originalOut); Console.SetError(originalError); }
            await ExternalCommandDispatch.WriteResultAsync(root, token, new(code == 0 ? "Completed" : "Failed", code, output.ToString(), error.ToString()));
            try { await new ActivityLog(root).RecordAsync(code == 0 ? "UPDATE_COMMAND" : "UPDATE_ERROR", "External command " + args[0] + (code == 0 ? " completed. " : " failed: " + error + " ") + "Result: " + ExternalCommandDispatch.ResultPath(root, token), code == 0 ? "Info" : "Error"); } catch (Exception) { /* Durable result is authoritative for this command outcome. */ }
            return code;
        }
        if (options.ContainsKey("wait-dispatch-pid")) throw new ArgumentException("Dispatch token required.");
        var worker = await ExternalCommandDispatch.WorkerForAsync(args[0], root, AppContext.BaseDirectory);
        if (worker is not null)
        {
            string result;
            try { result = await ExternalCommandDispatch.StartAsync(root, worker, args); }
            catch (Exception ex)
            {
                try { await new ActivityLog(root).RecordAsync("UPDATE_ERROR", ex.ToString(), "Error"); } catch (Exception) { }
                throw;
            }
            Print(new { State = "Accepted", Message = "Handoff accepted; operation has NOT completed. Read the result file for final exit code and errors.", ResultPath = result });
            return 0; // Release loaded managed binaries before the worker mutates/prunes.
        }
        return await RunAsync(args);
    }
    private static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "--help")
        {
            Console.WriteLine("BT2 updater: status | check | install-release | full-recover --manifest FILE --assets DIR | install --manifest FILE --assets DIR | stage --manifest FILE --assets DIR | apply --stage ID | rollback | recover | acknowledge-state --confirm state-saved | launch | package --app DIR --engine DIR --out DIR --version X [--source MANIFEST]. Optional --root DIR; apply/rollback accept --wait-pid PID. No operation runs automatically.");
            return 0;
        }
        var options = Parse(args.Skip(1).ToArray());
        var root = Path.GetFullPath(options.GetValueOrDefault("root") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BotToucher2"));
        var log = new ActivityLog(root);
        var messages = new List<string>();
        var engine = new UpdateEngine(root, message => { messages.Add(message); Console.Error.WriteLine(message); });
        using var launcher = new WindowsApplicationLauncher(root);
        try
        {
            var command = args[0];
            if (command == "package") { Package(options); return 0; }
            if (command is "apply" or "rollback" or "recover") await WaitForParent(options);
            switch (command)
            {
                case "status": Print(await engine.StatusAsync()); break;
                case "check": await CheckAsync(engine); break;
                case "install-release":
                    using (var http = GitHubReleaseClient.CreateHttpClient())
                    {
                        var release = await new GitHubReleaseClient(http).CheckAsync(await engine.StatusAsync()) ?? throw new InvalidOperationException("No compatible release available.");
                        var downloaded = await engine.StageAsync(release.Manifest, release.Assets);
                        await engine.ApplyAsync(downloaded, launcher);
                    }
                    break;
                case "stage":
                case "install":
                    var manifest = await ReadManifest(Required(options, "manifest"));
                    var stage = await engine.StageAsync(manifest, new DirectoryAssets(Required(options, "assets")));
                    if (command == "install") await engine.ApplyAsync(stage, launcher); else Console.WriteLine(stage);
                    break;
                case "full-recover":
                    var full = await ReadManifest(Required(options, "manifest"));
                    var recoveryStage = await engine.StageRecoveryAsync(full, new DirectoryAssets(Required(options, "assets")));
                    await engine.ApplyAsync(recoveryStage, launcher);
                    break;
                case "apply": await engine.ApplyAsync(Required(options, "stage"), launcher); break;
                case "rollback": await engine.RollbackAsync(launcher); break;
                case "recover": await engine.RecoverAsync(launcher); break;
                case "acknowledge-state":
                    if (Required(options, "confirm") != "state-saved") throw new ArgumentException("First save the displaced state externally, then use --confirm state-saved. Later transactions may rotate it.");
                    await engine.AcknowledgeDisplacedStateAsync();
                    break;
                case "launch":
                    var status = await engine.StatusAsync();
                    if (status.Pending) throw new InvalidOperationException("A pending update requires explicit recover before launch.");
                    var directory = status.AppDirectory ?? throw new InvalidOperationException("No managed install. Use explicit install with an initial release manifest.");
                    if (!await launcher.LaunchAsync(directory, Guid.NewGuid().ToString("N"), CancellationToken.None)) throw new IOException("BT2 did not become ready.");
                    break;
                default: throw new ArgumentException("Unknown command. Use help.");
            }
            foreach (var message in messages) await log.RecordAsync("UPDATE", message);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            try { foreach (var message in messages) await log.RecordAsync("UPDATE", message); await log.RecordAsync("UPDATE_ERROR", ex.ToString(), "Error"); } catch (Exception) { /* The original error remains visible on stderr. */ }
            if (options.ContainsKey("wait-pid"))
            {
                var relaunched = launcher.HasActiveApplication;
                try
                {
                    var status = await engine.StatusAsync();
                    if (!relaunched && !status.Pending && status.AppDirectory is not null)
                    {
                        await engine.VerifyCurrentAsync();
                        relaunched = await launcher.LaunchAsync(status.AppDirectory, Guid.NewGuid().ToString("N"), CancellationToken.None);
                    }
                }
                catch (Exception recoveryError)
                {
                    try { await log.RecordAsync("RELAUNCH_ERROR", recoveryError.Message, "Error"); } catch (Exception) { }
                }
                WindowsErrorReport.Show("BT2 update failed: " + ex.Message + (relaunched ? "\nThe previous application was relaunched." : "\nOpen the external updater and run status/recover. No unverified installation was launched.") + "\nDetails: " + log.Path);
            }
            return 1;
        }
    }
    private static Dictionary<string, string> Parse(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 == args.Length || !result.TryAdd(args[i][2..], args[i + 1]))
                throw new ArgumentException("Expected unique --name value options.");
        }
        return result;
    }
    private static string Required(Dictionary<string, string> options, string key) => options.GetValueOrDefault(key) ?? throw new ArgumentException("Missing --" + key);
    private static async Task<TransitionManifest> ReadManifest(string path)
    {
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Manifest too large.");
        return JsonSerializer.Deserialize<TransitionManifest>(await File.ReadAllBytesAsync(path)) ?? throw new InvalidDataException("Empty manifest.");
    }
    private static void Print<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value, Json));
    private static async Task WaitForParent(Dictionary<string, string> options)
    {
        if (!options.TryGetValue("wait-pid", out var value)) return;
        if (!int.TryParse(value, out var pid) || pid <= 0 || pid == Environment.ProcessId) throw new ArgumentException("Invalid parent PID.");
        try { using var parent = Process.GetProcessById(pid); await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2)); }
        catch (ArgumentException) { /* Already exited. */ }
    }
    private static void Package(Dictionary<string, string> options)
    {
        var output = Path.GetFullPath(Required(options, "out"));
        var app = Path.GetFullPath(Required(options, "app")); var updater = Path.GetFullPath(Required(options, "engine"));
        if (ExternalCommandDispatch.IsWithin(output, app) || ExternalCommandDispatch.IsWithin(app, output) || ExternalCommandDispatch.IsWithin(output, updater) || ExternalCommandDispatch.IsWithin(updater, output))
            throw new IOException("Package output and input directories must not overlap.");
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Package output must be empty.");
        var bundle = Path.Combine(output, "bundle"); Directory.CreateDirectory(bundle);
        Copy(Required(options, "app"), Path.Combine(bundle, "app")); Copy(Required(options, "engine"), Path.Combine(bundle, "engine"));
        var target = UpdatePlanner.Inventory(bundle);
        if (!target.Any(f => f.Path == "app/BotToucher2.exe") || !target.Any(f => f.Path == "engine/BotToucher2.Updater.exe"))
            throw new InvalidDataException("Package requires both Windows app and updater entry points.");
        TransitionManifest? source = options.TryGetValue("source", out var file) ? ReadManifest(file).GetAwaiter().GetResult() : null;
        var manifest = UpdatePlanner.CreateManifest(Required(options, "version"), source?.Target, target, source?.Version);
        var assets = Path.Combine(output, "assets"); Directory.CreateDirectory(assets);
        foreach (var op in manifest.Operations.Where(o => o.AssetName is not null).DistinctBy(o => o.AssetName))
            File.Copy(Path.Combine(bundle, op.Path.Replace('/', Path.DirectorySeparatorChar)), Path.Combine(assets, op.AssetName!));
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, Json);
        File.WriteAllBytes(Path.Combine(output, "transition.json"), manifestBytes);
        var manifestAsset = "transition-" + (manifest.SourceId ?? "initial") + ".json";
        File.WriteAllBytes(Path.Combine(output, manifestAsset), manifestBytes);
        var descriptor = new ReleaseTransition(manifest.SourceId, manifest.SourceVersion, manifestAsset,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(manifestBytes)).ToLowerInvariant(), manifestBytes.Length, manifest.RequiredBytes);
        File.WriteAllText(Path.Combine(output, "bt2-release.json"), JsonSerializer.Serialize(new ReleaseMetadata(1, "bot-toucher-2", manifest.Version,
            options.GetValueOrDefault("notes") ?? "BT2 release " + manifest.Version, new[] { descriptor }), Json));
        Console.WriteLine($"Target bytes: {target.Sum(f => f.Size)}; required payload bytes: {manifest.RequiredBytes}; unchanged/deleted payload: 0. Upload compiled assets to GitHub Releases only.");
    }
    private static void Copy(string from, string to)
    {
        if ((File.GetAttributes(from) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Reparse path rejected.");
        Directory.CreateDirectory(to);
        foreach (var path in Directory.EnumerateFileSystemEntries(from))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Reparse path rejected.");
            if (Directory.Exists(path)) Copy(path, Path.Combine(to, Path.GetFileName(path)));
            else File.Copy(path, Path.Combine(to, Path.GetFileName(path)));
        }
    }
    private static Task CheckAsync(UpdateEngine engine) => ReleaseCommands.CheckAsync(engine);
    private sealed class DirectoryAssets(string directory) : IAssetSource
    {
        public Task<Stream> OpenAsync(string assetName, CancellationToken cancellationToken)
        {
            if (assetName.Length != 64 || assetName.Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("Invalid asset name.");
            var path = Path.Combine(Path.GetFullPath(directory), assetName);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Reparse asset rejected.");
            return Task.FromResult<Stream>(File.OpenRead(path));
        }
    }
}
