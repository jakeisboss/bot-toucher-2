using System.Security.Cryptography;
using System.Text.Json;

namespace BotToucher2.Update;

public interface IAssetSource { Task<Stream> OpenAsync(string assetName, CancellationToken cancellationToken); }
public interface IApplicationLauncher { Task<bool> LaunchAsync(string appDirectory, string transactionToken, CancellationToken cancellationToken); }
public sealed record InstallationIdentity(string Version, string InventoryId);
public sealed record UpdateStatus(InstallationIdentity? Current, InstallationIdentity? Previous, string? AppDirectory, string? EngineDirectory, bool Pending);
internal sealed record Receipt(InstallationIdentity Identity, InventoryFile[] Inventory);
internal sealed record InstallRecord(Receipt Current, Receipt? Previous, string TransactionId, string? PreviousStateSnapshot);
internal sealed record Journal(string TransactionId, InstallRecord? Before, Receipt Candidate, string StateSnapshot, bool Recovery = false);
internal sealed record Snapshot(InventoryFile[] Inventory, string Digest);
internal sealed record Stage(TransitionManifest Manifest, string Digest, bool Recovery, string? ExpectedCurrent);

public sealed class UpdateEngine
{
    private readonly string root; private readonly Action<string>? events;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = false };
    public UpdateEngine(string root, Action<string>? events = null) { this.root = Path.GetFullPath(root); this.events = events; }
    private string CurrentPath => Path.Combine(root, "install", "current.json");
    private string JournalPath => Path.Combine(root, "updates", "transactions", "pending.json");
    private string Bundle(string id) => Path.Combine(root, "install", "versions", id);
    private string StageRoot(string id) => Path.Combine(root, "updates", "staging", id);
    private static string Token() => Guid.NewGuid().ToString("N");
    private static void CheckToken(string value) { if (value.Length != 32 || value.Any(c => !Uri.IsHexDigit(c) || char.IsUpper(c))) throw new InvalidDataException("Invalid owned transaction identifier."); }
    private static string Digest<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, Json))).ToLowerInvariant();
    private static async Task<T?> Read<T>(string path, CancellationToken ct) { if (!File.Exists(path)) return default; var info = new FileInfo(path); if (info.Length > 4 * 1024 * 1024) throw new InvalidDataException("Metadata exceeds limit."); await using var f = File.OpenRead(path); return await JsonSerializer.DeserializeAsync<T>(f, Json, ct); }
    private static async Task Write<T>(string path, T value, CancellationToken ct) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); var temp = path + "." + Token() + ".tmp"; await using (var f = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) { await JsonSerializer.SerializeAsync(f, value, Json, ct); await f.FlushAsync(ct); f.Flush(true); } File.Move(temp, path, true); }
    private FileStream Lease() { var path = Path.Combine(root, "updates", "lease.lock"); Directory.CreateDirectory(Path.GetDirectoryName(path)!); return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
    private FileStream AppLease() { var path = Path.Combine(root, "control", "app.instance.lock"); Directory.CreateDirectory(Path.GetDirectoryName(path)!); return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
    private static void ValidateManifest(TransitionManifest manifest)
    {
        if (manifest.MinimumBootstrapProtocol != 1) throw new InvalidDataException("Unsupported bootstrap protocol.");
        var expected = UpdatePlanner.CreateManifest(manifest.Version, manifest.SourceId is null ? null : manifest.Source, manifest.Target, manifest.SourceVersion);
        if (Digest(expected) != Digest(manifest)) throw new InvalidDataException("Manifest does not match canonical transition.");
    }
    private static void Verify(string path, Receipt receipt)
    {
        if (receipt.Identity.InventoryId != UpdatePlanner.Identity(receipt.Inventory) || !UpdatePlanner.Inventory(path).SequenceEqual(receipt.Inventory)) throw new InvalidDataException("Managed inventory mismatch.");
    }
    private static string SafeFile(string basePath, string relative) => Path.Combine(basePath, relative.Replace('/', Path.DirectorySeparatorChar));
    public async Task<UpdateStatus> StatusAsync(CancellationToken cancellationToken = default)
    {
        var record = await Read<InstallRecord>(CurrentPath, cancellationToken);
        return new(record?.Current.Identity, record?.Previous?.Identity, record is null ? null : Path.Combine(Bundle(record.Current.Identity.InventoryId), "app"), record is null ? null : Path.Combine(Bundle(record.Current.Identity.InventoryId), "engine"), File.Exists(JournalPath));
    }
    public Task<string> StageAsync(TransitionManifest manifest, IAssetSource assets, CancellationToken cancellationToken = default) => StageCore(manifest, assets, false, cancellationToken);
    public Task<string> StageRecoveryAsync(TransitionManifest manifest, IAssetSource assets, CancellationToken cancellationToken = default) => StageCore(manifest, assets, true, cancellationToken);
    private async Task<string> StageCore(TransitionManifest manifest, IAssetSource assets, bool recovery, CancellationToken cancellationToken)
    {
        using var lease = Lease(); ValidateManifest(manifest);
        if (File.Exists(JournalPath)) throw new InvalidOperationException("Recovery is required.");
        var current = await Read<InstallRecord>(CurrentPath, cancellationToken);
        if (recovery) { if (current is null || manifest.SourceId is not null) throw new InvalidDataException("Recovery requires an existing installation and full manifest."); }
        else { if ((current?.Current.Identity.InventoryId != manifest.SourceId || current?.Current.Identity.Version != manifest.SourceVersion)) throw new InvalidDataException("Source identity mismatch."); if (current is not null) Verify(Bundle(current.Current.Identity.InventoryId), current.Current); }
        var expectedCurrent = recovery ? Digest(current) : null;
        var stageDigest = Digest(new { manifest, recovery, expectedCurrent });
        var id = Token() + "-" + stageDigest; var dir = StageRoot(id); var target = Path.Combine(dir, "bundle"); Directory.CreateDirectory(target);
        foreach (var operation in manifest.Operations.Where(x => x.Kind != "delete"))
        {
            var destination = SafeFile(target, operation.Path); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await using var input = operation.Kind == "unchanged" ? File.OpenRead(SafeFile(Bundle(current!.Current.Identity.InventoryId), operation.Path)) : await assets.OpenAsync(operation.AssetName!, cancellationToken);
            var buffer = new byte[65536]; long count = 0; int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0) { count += read; if (count > operation.Size) throw new InvalidDataException("Asset exceeds declared size."); await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken); }
            if (count != operation.Size) throw new InvalidDataException("Asset size mismatch.");
        }
        Verify(target, new(new(manifest.Version, manifest.TargetId), manifest.Target));
        await Write(Path.Combine(dir, "stage.json"), new Stage(manifest, stageDigest, recovery, expectedCurrent), cancellationToken); events?.Invoke("Update staged."); return id;
    }
    public async Task ApplyAsync(string stageId, IApplicationLauncher launcher, CancellationToken cancellationToken = default)
    {
        if (stageId.Length != 97 || stageId[32] != '-' || stageId.Where((_, i) => i != 32).Any(c => !Uri.IsHexDigit(c) || char.IsUpper(c))) throw new InvalidDataException("Invalid stage identifier.");
        using var lease = Lease(); using var appLease = AppLease();
        if (File.Exists(JournalPath)) throw new InvalidOperationException("Recovery is required.");
        var stage = await Read<Stage>(Path.Combine(StageRoot(stageId), "stage.json"), cancellationToken) ?? throw new InvalidDataException("Stage not found.");
        ValidateManifest(stage.Manifest); if (Digest(new { manifest = stage.Manifest, recovery = stage.Recovery, expectedCurrent = stage.ExpectedCurrent }) != stage.Digest || stageId[33..] != stage.Digest) throw new InvalidDataException("Stage digest mismatch.");
        var before = await Read<InstallRecord>(CurrentPath, cancellationToken);
        if (stage.Recovery) { if (before is null || stage.Manifest.SourceId is not null || Digest(before) != stage.ExpectedCurrent) throw new InvalidDataException("Recovery source changed."); if (before.Current.Identity.InventoryId == stage.Manifest.TargetId) throw new InvalidOperationException("Recovery to the same inventory requires quarantine support."); }
        else { if ((before?.Current.Identity.InventoryId != stage.Manifest.SourceId || before?.Current.Identity.Version != stage.Manifest.SourceVersion)) throw new InvalidDataException("Source identity mismatch."); if (before is not null) Verify(Bundle(before.Current.Identity.InventoryId), before.Current); }
        var candidate = new Receipt(new(stage.Manifest.Version, stage.Manifest.TargetId), stage.Manifest.Target);
        Verify(Path.Combine(StageRoot(stageId), "bundle"), candidate);
        var transaction = Token(); var snapshot = await CaptureState(transaction, cancellationToken);
        var journal = new Journal(transaction, before, candidate, snapshot, stage.Recovery); await Write(JournalPath, journal, cancellationToken);
        var bundle = Bundle(candidate.Identity.InventoryId); Directory.CreateDirectory(Path.GetDirectoryName(bundle)!);
        if (Directory.Exists(bundle)) Verify(bundle, candidate); else Directory.Move(Path.Combine(StageRoot(stageId), "bundle"), bundle);
        Verify(bundle, candidate); appLease.Dispose();
        bool ready;
        try { ready = await launcher.LaunchAsync(Path.Combine(bundle, "app"), Token(), cancellationToken); }
        catch { await Abort(journal, launcher, CancellationToken.None); throw; }
        if (!ready) { await Abort(journal, launcher, CancellationToken.None); events?.Invoke("Candidate readiness failed."); throw new InvalidOperationException("Candidate did not become ready."); }
        await Write(Path.Combine(root, "updates", "transactions", transaction + ".receipt.json"), new { candidate, Digest = Digest(candidate) }, cancellationToken);
        await Write(CurrentPath, new InstallRecord(candidate, stage.Recovery ? before?.Previous : before?.Current, transaction, stage.Recovery ? before?.PreviousStateSnapshot : snapshot), cancellationToken);
        File.Delete(JournalPath); events?.Invoke("Update committed.");
    }
    private string SnapshotRoot(string id) { CheckToken(id); return Path.Combine(root, "updates", "snapshots", id); }
    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination); if (!Directory.Exists(source)) return;
        var stack = new Stack<(string Source, string Destination)>(); stack.Push((source, destination)); long bytes = 0; var count = 0;
        while (stack.Count > 0)
        {
            var (from, to) = stack.Pop();
            if ((File.GetAttributes(from) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("State contains a reparse point.");
            foreach (var entry in Directory.EnumerateFileSystemEntries(from))
            {
                var attr = File.GetAttributes(entry); if ((attr & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("State contains a reparse point.");
                var target = Path.Combine(to, Path.GetFileName(entry));
                if ((attr & FileAttributes.Directory) != 0) { Directory.CreateDirectory(target); stack.Push((entry, target)); }
                else { if (++count > 8192 || (bytes += new FileInfo(entry).Length) > 2L * 1024 * 1024 * 1024) throw new InvalidDataException("State exceeds snapshot limit."); File.Copy(entry, target); }
            }
        }
    }
    private async Task<string> CaptureState(string id, CancellationToken ct)
    {
        var dir = SnapshotRoot(id); Directory.CreateDirectory(dir);
        CopyTree(Path.Combine(root, "state"), Path.Combine(dir, "app"));
        var inventory = UpdatePlanner.Inventory(dir); await Write(Path.Combine(dir, "snapshot.json"), new Snapshot(inventory, Digest(inventory)), ct); return id;
    }
    private async Task RestoreState(string id, CancellationToken ct)
    {
        var dir = SnapshotRoot(id); var snapshot = await Read<Snapshot>(Path.Combine(dir, "snapshot.json"), ct) ?? throw new InvalidDataException("Snapshot missing.");
        if (snapshot.Digest != Digest(snapshot.Inventory)) throw new InvalidDataException("Snapshot receipt mismatch.");
        // Inventory only the state subtree; metadata lives alongside it.
        var actual = UpdatePlanner.InventoryState(Path.Combine(dir, "app"));
        if (!actual.SequenceEqual(snapshot.Inventory)) throw new InvalidDataException("Snapshot contents mismatch.");
        var state = Path.Combine(root, "state");
        if (Directory.Exists(state)) { var evidence = Path.Combine(root, "updates", "recovery", Token()); Directory.CreateDirectory(Path.GetDirectoryName(evidence)!); Directory.Move(state, evidence); }
        CopyTree(Path.Combine(dir, "app"), state);
    }
    private async Task Abort(Journal journal, IApplicationLauncher launcher, CancellationToken ct)
    {
        await RestoreState(journal.StateSnapshot, ct);
        if (!journal.Recovery && journal.Before is not null) { Verify(Bundle(journal.Before.Current.Identity.InventoryId), journal.Before.Current); if (!await launcher.LaunchAsync(Path.Combine(Bundle(journal.Before.Current.Identity.InventoryId), "app"), Token(), ct)) throw new InvalidOperationException("Previous application did not become ready; recovery remains pending."); }
        File.Delete(JournalPath);
    }
    public async Task RollbackAsync(IApplicationLauncher launcher, CancellationToken cancellationToken = default)
    {
        using var lease = Lease(); using var appLease = AppLease();
        if (File.Exists(JournalPath)) throw new InvalidOperationException("Recovery is required.");
        var before = await Read<InstallRecord>(CurrentPath, cancellationToken) ?? throw new InvalidOperationException("No installation exists.");
        if (before.Previous is null || before.PreviousStateSnapshot is null) throw new InvalidOperationException("No rollback is available.");
        Verify(Bundle(before.Current.Identity.InventoryId), before.Current); Verify(Bundle(before.Previous.Identity.InventoryId), before.Previous);
        var transaction = Token(); var snapshot = await CaptureState(transaction, cancellationToken);
        var journal = new Journal(transaction, before, before.Previous, snapshot); await Write(JournalPath, journal, cancellationToken);
        await RestoreState(before.PreviousStateSnapshot, cancellationToken); appLease.Dispose();
        bool ready;
        try { ready = await launcher.LaunchAsync(Path.Combine(Bundle(before.Previous.Identity.InventoryId), "app"), Token(), cancellationToken); }
        catch { await Abort(journal, launcher, CancellationToken.None); throw; }
        if (!ready) { await Abort(journal, launcher, CancellationToken.None); throw new InvalidOperationException("Rollback candidate did not become ready."); }
        await Write(Path.Combine(root, "updates", "transactions", transaction + ".receipt.json"), new { candidate = before.Previous, Digest = Digest(before.Previous) }, cancellationToken);
        await Write(CurrentPath, new InstallRecord(before.Previous, before.Current, transaction, snapshot), cancellationToken);
        File.Delete(JournalPath); events?.Invoke("Rollback committed; displaced state retained.");
    }
    public async Task RecoverAsync(IApplicationLauncher launcher, CancellationToken cancellationToken = default)
    {
        using var lease = Lease(); var journal = await Read<Journal>(JournalPath, cancellationToken); if (journal is null) return;
        var current = await Read<InstallRecord>(CurrentPath, cancellationToken);
        if (current?.TransactionId == journal.TransactionId) { Verify(Bundle(current.Current.Identity.InventoryId), current.Current); File.Delete(JournalPath); return; }
        if (Digest(current) != Digest(journal.Before)) throw new InvalidDataException("Recovery state mismatch; evidence retained.");
        await Abort(journal, launcher, cancellationToken);
    }
}
