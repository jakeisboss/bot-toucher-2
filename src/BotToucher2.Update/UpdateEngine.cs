using System.Security.Cryptography;
using System.Text.Json;

namespace BotToucher2.Update;

public interface IAssetSource { Task<Stream> OpenAsync(string assetName, CancellationToken cancellationToken); }
public interface IApplicationLauncher { bool IsRunning { get; } Task<bool> LaunchAsync(string appDirectory, string transactionToken, CancellationToken cancellationToken); Task StopAsync(CancellationToken cancellationToken); }
public sealed record UpdateHistoryEntry(string TransactionId, string Version, string Outcome, DateTimeOffset TimestampUtc);
public sealed record InstallationIdentity(string Version, string InventoryId);
public sealed record UpdateStatus(InstallationIdentity? Current, InstallationIdentity? Previous, string? AppDirectory, string? EngineDirectory, bool Pending, string? DisplacedStateDirectory = null);
internal sealed record Receipt(InstallationIdentity Identity, InventoryFile[] Inventory);
internal sealed record InstallRecord(Receipt Current, Receipt? Previous, string TransactionId, string? PreviousStateSnapshot, Receipt? Anchor = null, string? DisplacedStateSnapshot = null, string? PreviousReceiptId = null, string? AnchorReceiptId = null);
internal sealed record Journal(string TransactionId, InstallRecord? Before, Receipt Candidate, string StateSnapshot, bool Recovery = false, bool HadCurrentBundle = false, string? StageId = null, bool HadTargetBundle = false);
internal sealed record CommitReceipt(InstallRecord Record, string RecordDigest, string ReadinessToken);
internal sealed record HistoryFile(UpdateHistoryEntry[] Entries, string Digest);
internal sealed record JournalFile(Journal Journal, string Digest);
internal sealed record Snapshot(InventoryFile[] Inventory, string Digest);
internal sealed record ResolvedSnapshot(string SnapshotId, string Digest);
internal sealed record Stage(TransitionManifest Manifest, string Digest, bool Recovery, string? ExpectedCurrent);

public sealed class UpdateEngine
{
    private readonly string root; private readonly Action<string>? events; private readonly Action<string>? checkpoint;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = false };
    public UpdateEngine(string root, Action<string>? events = null, Action<string>? checkpoint = null) { this.root = Path.GetFullPath(root); this.events = events; this.checkpoint = checkpoint; }
    private string CurrentPath => Path.Combine(root, "install", "current.json");
    private string JournalPath => Path.Combine(root, "updates", "transactions", "pending.json");
    private static string Guard(string path)
    {
        var full = Path.GetFullPath(path); var cursor = Path.GetPathRoot(full)!;
        foreach (var part in full[cursor.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            cursor = Path.Combine(cursor, part);
            try { if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Reparse points in managed paths are forbidden: " + cursor); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return full;
    }
    private string Bundle(string id) => Guard(Path.Combine(root, "install", "versions", id));
    private string StageRoot(string id) => Guard(Path.Combine(root, "updates", "staging", id));
    private static string Token() => Guid.NewGuid().ToString("N");
    private static void CheckToken(string value) { if (value.Length != 32 || value.Any(c => !Uri.IsHexDigit(c) || char.IsUpper(c))) throw new InvalidDataException("Invalid owned transaction identifier."); }
    private static void CheckStageId(string stageId) { if (stageId.Length != 97 || stageId[32] != '-' || stageId.Where((_, i) => i != 32).Any(c => !Uri.IsHexDigit(c) || char.IsUpper(c))) throw new InvalidDataException("Invalid stage identifier."); }
    private static string Digest<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, Json))).ToLowerInvariant();
    private static async Task<byte[]?> ReadMetadata(string path, CancellationToken ct)
    {
        Guard(path);
        try
        {
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
            if (file.Length > 4 * 1024 * 1024) throw new InvalidDataException("Metadata exceeds limit.");
            var bytes = new byte[checked((int)file.Length)]; await file.ReadExactlyAsync(bytes, ct); return bytes;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
    private static T DecodeMetadata<T>(byte[] bytes) => JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new InvalidDataException("Metadata contains null instead of a record.");
    private static async Task<T?> Read<T>(string path, CancellationToken ct)
    {
        var bytes = await ReadMetadata(path, ct); return bytes is null ? default : DecodeMetadata<T>(bytes);
    }
    private static async Task Write<T>(string path, T value, CancellationToken ct) { Guard(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!); Guard(path); var temp = path + "." + Token() + ".tmp"; try { await using (var f = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) { await JsonSerializer.SerializeAsync(f, value, Json, ct); if (f.Length > 4 * 1024 * 1024) throw new InvalidDataException("Metadata exceeds limit."); await f.FlushAsync(ct); f.Flush(true); }
        // Windows MoveFileEx(REPLACE_EXISTING) can reject an open destination even
        // when readers share deletion. ReplaceFile uses the compatible replacement
        // protocol; the mutation lease guarantees only one publisher for this path.
        if (File.Exists(path)) File.Replace(temp, path, null);
        else File.Move(temp, path);
        } finally { if (File.Exists(temp)) File.Delete(temp); } }
    private FileStream Lease() { var path = Guard(Path.Combine(root, "updates", "lease.lock")); Directory.CreateDirectory(Path.GetDirectoryName(path)!); return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
    private FileStream AppLease() { var path = Guard(Path.Combine(root, "control", "app.instance.lock")); Directory.CreateDirectory(Path.GetDirectoryName(path)!); return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
    private async Task<InstallRecord?> LoadCurrent(CancellationToken ct)
    {
        var record = await Read<InstallRecord>(CurrentPath, ct);
        return await ValidateCurrent(record, ct);
    }
    private async Task<InstallRecord?> ValidateCurrent(InstallRecord? record, CancellationToken ct)
    {
        if (record is null) return null;
        CheckToken(record.TransactionId);
        ValidateReceipt(record.Current); if (record.Previous is not null) ValidateReceipt(record.Previous); if (record.Anchor is not null) ValidateReceipt(record.Anchor);
        if (record.PreviousStateSnapshot is not null) CheckToken(record.PreviousStateSnapshot); if (record.DisplacedStateSnapshot is not null) CheckToken(record.DisplacedStateSnapshot);
        var receipt = await Read<CommitReceipt>(Path.Combine(root, "updates", "transactions", record.TransactionId + ".receipt.json"), ct) ?? throw new InvalidDataException("Current receipt missing.");
        CheckToken(receipt.ReadinessToken);
        if (receipt.RecordDigest != Digest(receipt.Record) || receipt.RecordDigest != Digest(record)) throw new InvalidDataException("Current record differs from its receipt.");
        await ValidateReferencedReceipt(record.PreviousReceiptId, record.Previous, ct);
        await ValidateReferencedReceipt(record.AnchorReceiptId, record.Anchor, ct);
        return record;
    }
    private async Task ValidateReferencedReceipt(string? id, Receipt? expected, CancellationToken ct)
    {
        if ((id is null) != (expected is null)) throw new InvalidDataException("Receipt reference is incomplete.");
        if (id is null) return;
        CheckToken(id); var stored = await Read<CommitReceipt>(Path.Combine(root, "updates", "transactions", id + ".receipt.json"), ct) ?? throw new InvalidDataException("Referenced receipt missing.");
        CheckToken(stored.ReadinessToken);
        if (stored.Record.TransactionId != id || stored.RecordDigest != Digest(stored.Record) || Digest(stored.Record.Current) != Digest(expected)) throw new InvalidDataException("Referenced receipt mismatch.");
    }
    private static void ValidateReceipt(Receipt receipt)
    {
        if (!Version.TryParse(receipt.Identity.Version, out _) || receipt.Identity.InventoryId != UpdatePlanner.Identity(receipt.Inventory) || !UpdatePlanner.Validate(receipt.Inventory).SequenceEqual(receipt.Inventory)) throw new InvalidDataException("Invalid inventory receipt.");
    }
    private static bool IsNewer(string target, string source)
    {
        static (int, int, int, int) Parts(string value) { var version = Version.Parse(value); return (version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision)); }
        return Parts(target).CompareTo(Parts(source)) > 0;
    }
    private static bool IsAnchor(Receipt receipt)
    {
        var version = Version.Parse(receipt.Identity.Version);
        return version.Major == 1 && version.Minor == 0 && version.Build <= 0 && version.Revision <= 0;
    }
    private static Receipt? SelectAnchor(InstallRecord? before, Receipt candidate) => before?.Anchor ?? (IsAnchor(candidate) ? candidate : null);
    private static string? SelectAnchorId(InstallRecord? before, Receipt candidate, string transaction) => before?.AnchorReceiptId ?? (IsAnchor(candidate) ? transaction : null);
    private async Task Prune(InstallRecord? before, InstallRecord after, CancellationToken ct)
    {
        var obsolete = before?.Previous;
        if (obsolete is not null && obsolete.Identity.InventoryId != after.Current.Identity.InventoryId && obsolete.Identity.InventoryId != after.Previous?.Identity.InventoryId && obsolete.Identity.InventoryId != after.Anchor?.Identity.InventoryId)
        {
            var path = Bundle(obsolete.Identity.InventoryId);
            var deleting = Guard(Path.Combine(root, "updates", "deleting", after.TransactionId, obsolete.Identity.InventoryId));
            if (Directory.Exists(path))
            {
                if (Directory.Exists(deleting)) throw new InvalidDataException("Conflicting deletion evidence.");
                Verify(path, obsolete); Directory.CreateDirectory(Path.GetDirectoryName(deleting)!); Directory.Move(path, deleting); checkpoint?.Invoke("prune-moved");
            }
            if (Directory.Exists(deleting))
            {
                var expected = obsolete.Inventory.ToDictionary(x => x.Path, StringComparer.Ordinal);
                foreach (var file in UpdatePlanner.Inventory(deleting)) if (!expected.TryGetValue(file.Path, out var declared) || file != declared) throw new InvalidDataException("Deletion evidence differs from receipt.");
                DeleteTree(deleting);
            }
        }
        var oldReceiptId = before?.PreviousReceiptId;
        if (oldReceiptId is not null && oldReceiptId != after.TransactionId && oldReceiptId != after.PreviousReceiptId && oldReceiptId != after.AnchorReceiptId)
        {
            var receiptPath = Guard(Path.Combine(root, "updates", "transactions", oldReceiptId + ".receipt.json"));
            if (File.Exists(receiptPath)) { await ValidateReferencedReceipt(oldReceiptId, before!.Previous, ct); File.Delete(receiptPath); }
        }
        var supersededCurrentId = before?.TransactionId;
        if (supersededCurrentId is not null && supersededCurrentId != after.TransactionId && supersededCurrentId != after.PreviousReceiptId && supersededCurrentId != after.AnchorReceiptId)
        {
            var path = Guard(Path.Combine(root, "updates", "transactions", supersededCurrentId + ".receipt.json"));
            if (File.Exists(path)) { await ValidateReferencedReceipt(supersededCurrentId, before!.Current, ct); File.Delete(path); }
        }
        var oldSnapshot = before?.PreviousStateSnapshot;
        if (oldSnapshot is not null && oldSnapshot != after.PreviousStateSnapshot && oldSnapshot != after.DisplacedStateSnapshot && oldSnapshot != before?.DisplacedStateSnapshot)
            await DeleteSnapshot(oldSnapshot, ct);
        var displaced = before?.DisplacedStateSnapshot;
        if (displaced is not null && displaced != after.DisplacedStateSnapshot && displaced != after.PreviousStateSnapshot && await IsResolvedForCleanup(displaced, ct))
        {
            await DeleteSnapshot(displaced, ct); File.Delete(ResolvedPath(displaced));
        }
    }
    private string ResolvedPath(string id) { CheckToken(id); return Guard(Path.Combine(root, "updates", "rollback", "resolved", id + ".json")); }
    private async Task<Snapshot> ReadSnapshot(string id, CancellationToken ct)
    {
        var dir = SnapshotRoot(id); var receipt = await Read<Snapshot>(Path.Combine(dir, "snapshot.json"), ct) ?? throw new InvalidDataException("State snapshot missing.");
        if (receipt.Digest != Digest(receipt.Inventory) || !UpdatePlanner.InventoryState(Path.Combine(dir, "app")).SequenceEqual(receipt.Inventory)) throw new InvalidDataException("State snapshot mismatch.");
        return receipt;
    }
    private async Task<bool> IsResolved(string id, CancellationToken ct)
    {
        var marker = await Read<ResolvedSnapshot>(ResolvedPath(id), ct); if (marker is null) return false;
        var snapshot = await ReadSnapshot(id, ct);
        if (marker.SnapshotId != id || marker.Digest != snapshot.Digest) throw new InvalidDataException("State resolution marker mismatch.");
        return true;
    }
    private async Task<bool> IsResolvedForCleanup(string id, CancellationToken ct)
    {
        var marker = await Read<ResolvedSnapshot>(ResolvedPath(id), ct); if (marker is null) return false;
        if (marker.SnapshotId != id || marker.Digest.Length != 64 || marker.Digest.Any(c => !Uri.IsHexDigit(c) || char.IsUpper(c))) throw new InvalidDataException("Invalid state resolution marker.");
        var metadata = await Read<Snapshot>(Path.Combine(SnapshotRoot(id), "snapshot.json"), ct);
        if (metadata is not null && (metadata.Digest != marker.Digest || metadata.Digest != Digest(metadata.Inventory))) throw new InvalidDataException("State resolution metadata mismatch.");
        return true;
    }
    private async Task DeleteSnapshot(string id, CancellationToken ct)
    {
        var dir = SnapshotRoot(id); if (!Directory.Exists(dir)) return;
        var metadata = Path.Combine(dir, "snapshot.json"); var app = Path.Combine(dir, "app");
        var entries = Directory.EnumerateFileSystemEntries(dir).Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);
        if (entries.Any(x => x is not ("app" or "snapshot.json"))) throw new InvalidDataException("Unexpected snapshot evidence.");
        if (!File.Exists(metadata))
        {
            if (entries.Count != 0) throw new InvalidDataException("Snapshot metadata missing during cleanup.");
            Directory.Delete(dir); return;
        }
        var receipt = await Read<Snapshot>(metadata, ct) ?? throw new InvalidDataException("Snapshot metadata missing.");
        if (receipt.Digest != Digest(receipt.Inventory)) throw new InvalidDataException("Snapshot digest mismatch.");
        if (Directory.Exists(app))
        {
            var expected = receipt.Inventory.ToDictionary(x => x.Path, StringComparer.Ordinal);
            foreach (var file in UpdatePlanner.InventoryState(app)) if (!expected.TryGetValue(file.Path, out var declared) || file != declared) throw new InvalidDataException("Snapshot cleanup bytes differ.");
            DeleteTree(app); checkpoint?.Invoke("snapshot-app-deleted");
        }
        File.Delete(metadata); Directory.Delete(dir);
    }
    private async Task CleanupTemporarySnapshot(Journal journal, InstallRecord? current)
    {
        if (journal.StateSnapshot == current?.PreviousStateSnapshot || journal.StateSnapshot == current?.DisplacedStateSnapshot) return;
        try { await DeleteSnapshot(journal.StateSnapshot, CancellationToken.None); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException) { events?.Invoke("Temporary state snapshot retained: " + error.Message); }
    }
    public async Task AcknowledgeDisplacedStateAsync(CancellationToken cancellationToken = default)
    {
        using var lease = Lease(); if (File.Exists(JournalPath)) throw new InvalidOperationException("Recovery is required.");
        var current = await LoadCurrent(cancellationToken) ?? throw new InvalidOperationException("No installation exists.");
        var id = current.DisplacedStateSnapshot ?? throw new InvalidOperationException("No displaced state exists.");
        var snapshot = await ReadSnapshot(id, cancellationToken);
        await Write(ResolvedPath(id), new ResolvedSnapshot(id, snapshot.Digest), cancellationToken);
        events?.Invoke("Displaced state acknowledged for later rotation.");
    }
    private static void DeleteTree(string path, Action? afterEntry = null)
    {
        Guard(path);
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Reparse point in cleanup target.");
            if ((attributes & FileAttributes.Directory) != 0) DeleteTree(entry, afterEntry); else File.Delete(entry); afterEntry?.Invoke();
        }
        Directory.Delete(path);
    }
    private async Task Commit(InstallRecord record, string readinessToken, IApplicationLauncher launcher, CancellationToken ct)
    {
        CheckToken(readinessToken); ValidateReceipt(record.Current);
        var receipt = new CommitReceipt(record, Digest(record), readinessToken);
        await Write(Path.Combine(root, "updates", "transactions", record.TransactionId + ".receipt.json"), receipt, ct);
        checkpoint?.Invoke("receipt-written");
        if (!launcher.IsRunning) throw new InvalidOperationException("Candidate exited before commit.");
        await Write(CurrentPath, record, ct);
    }
    public async Task<IReadOnlyList<UpdateHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        var file = await Read<HistoryFile>(Path.Combine(root, "updates", "transactions", "history.json"), cancellationToken);
        if (file is null) return Array.Empty<UpdateHistoryEntry>();
        if (file.Entries.Length > 100 || file.Digest != Digest(file.Entries)) throw new InvalidDataException("Update history is invalid.");
        return file.Entries;
    }
    private async Task RecordHistory(Journal journal, string outcome, CancellationToken ct)
    {
        var entries = (await GetHistoryAsync(ct)).Where(x => x.TransactionId != journal.TransactionId).Append(new UpdateHistoryEntry(journal.TransactionId, journal.Candidate.Identity.Version, outcome, DateTimeOffset.UtcNow)).TakeLast(100).ToArray();
        await Write(Path.Combine(root, "updates", "transactions", "history.json"), new HistoryFile(entries, Digest(entries)), ct);
    }
    private async Task WriteJournal(Journal journal, CancellationToken ct) => await Write(JournalPath, new JournalFile(journal, Digest(journal)), ct);
    private async Task<Journal?> LoadJournal(CancellationToken ct)
    {
        var stored = await Read<JournalFile>(JournalPath, ct); if (stored is null) return null;
        var journal = stored.Journal;
        if (stored.Digest != Digest(journal)) throw new InvalidDataException("Journal digest mismatch.");
        CheckToken(journal.TransactionId); CheckToken(journal.StateSnapshot); if (journal.StageId is not null) CheckStageId(journal.StageId);
        if (journal.TransactionId != journal.StateSnapshot) throw new InvalidDataException("Journal snapshot is not bound to transaction.");
        ValidateReceipt(journal.Candidate);
        return journal;
    }
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
        // The record is the commit authority. Bracket its receipt reads and pending flag
        // with another record read; pruning may remove the old references meanwhile.
        // Never take the mutation lease: a candidate polls while its helper owns it.
        for (var attempt = 0; attempt < 8; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = await ReadMetadata(CurrentPath, cancellationToken);
            var pendingBefore = File.Exists(JournalPath);
            InstallRecord? record = null; Exception? failure = null;
            try
            {
                checkpoint?.Invoke("status-current-read");
                record = await ValidateCurrent(before is null ? null : DecodeMetadata<InstallRecord>(before), cancellationToken);
                checkpoint?.Invoke("status-validated");
            }
            catch (Exception error) when (error is IOException or InvalidDataException or JsonException) { failure = error; }
            var pendingAfter = File.Exists(JournalPath);
            var after = await ReadMetadata(CurrentPath, cancellationToken);
            if (pendingBefore != pendingAfter || !((before is null && after is null) || (before is not null && after is not null && before.AsSpan().SequenceEqual(after)))) continue;
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            return new(record?.Current.Identity, record?.Previous?.Identity, record is null ? null : Path.Combine(Bundle(record.Current.Identity.InventoryId), "app"), record is null ? null : Path.Combine(Bundle(record.Current.Identity.InventoryId), "engine"), pendingAfter, record?.DisplacedStateSnapshot is null ? null : Path.Combine(SnapshotRoot(record.DisplacedStateSnapshot), "app"));
        }
        // A continuously changing installation is busy, never a false nonpending old selection.
        return new(null, null, null, null, true);
    }

    public async Task VerifyCurrentAsync(CancellationToken cancellationToken = default)
    {
        using var lease = Lease();
        if (File.Exists(JournalPath)) throw new InvalidOperationException("Recovery is required.");
        var current = await LoadCurrent(cancellationToken) ?? throw new InvalidOperationException("No managed installation exists.");
        Verify(Bundle(current.Current.Identity.InventoryId), current.Current);
    }
    public async Task<string> PrepareWorkerAsync(CancellationToken cancellationToken = default)
    {
        using var lease = Lease();
        var current = await LoadCurrent(cancellationToken) ?? throw new InvalidOperationException("No managed installation exists.");
        var receipt = current.Current;
        if (receipt.Identity.InventoryId != UpdatePlanner.Identity(receipt.Inventory)) throw new InvalidDataException("Invalid selected inventory identity.");
        var expected = receipt.Inventory.Where(x => x.Path.StartsWith("engine/", StringComparison.Ordinal)).ToArray();
        if (expected.Length == 0) throw new InvalidDataException("Selected installation has no engine inventory.");
        var source = Path.Combine(Bundle(receipt.Identity.InventoryId), "engine");
        static InventoryFile[] EngineFiles(string path) => UpdatePlanner.InventoryState(path).Select(x => x with { Path = "engine/" + x.Path[4..] }).ToArray();
        if (!EngineFiles(source).SequenceEqual(expected)) throw new InvalidDataException("Selected updater engine is damaged.");
        var workersRoot = Guard(Path.Combine(root, "control", "workers")); Directory.CreateDirectory(workersRoot);
        var engineId = UpdatePlanner.Identity(expected);
        var workerParent = Guard(Path.Combine(workersRoot, engineId)); var worker = Guard(Path.Combine(workerParent, "engine"));
        var pendingPrefix = ".pending-" + engineId + "-";
        foreach (var pending in Directory.EnumerateDirectories(workersRoot, pendingPrefix + "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(pending); var suffix = name[pendingPrefix.Length..];
            if (suffix.Length != 32 || suffix.Any(c => !Uri.IsHexDigit(c) || char.IsUpper(c))) continue;
            QuarantineWorker(workersRoot, pending);
        }
        if (Directory.Exists(workerParent))
        {
            var valid = false;
            try { valid = Directory.Exists(worker) && Directory.EnumerateFileSystemEntries(workerParent).All(path => Path.GetFileName(path) == "engine") && EngineFiles(worker).SequenceEqual(expected); }
            catch (InvalidDataException) { }
            if (!valid) QuarantineWorker(workersRoot, workerParent);
        }
        if (!Directory.Exists(workerParent))
        {
            var pending = Guard(Path.Combine(workersRoot, pendingPrefix + Token()));
            var temporaryEngine = Path.Combine(pending, "engine");
            CopyTree(source, temporaryEngine, () => checkpoint?.Invoke("worker-copy-file"));
            if (!EngineFiles(temporaryEngine).SequenceEqual(expected) || !EngineFiles(source).SequenceEqual(expected)) throw new InvalidDataException("Worker copy failed verification.");
            Directory.Move(pending, workerParent);
        }
        if (!EngineFiles(worker).SequenceEqual(expected) || !EngineFiles(source).SequenceEqual(expected)) throw new InvalidDataException("Worker copy failed verification.");
        return worker;
    }
    private static void QuarantineWorker(string workersRoot, string source)
    {
        Guard(source); var quarantine = Guard(Path.Combine(workersRoot, "quarantine", Token()));
        Directory.CreateDirectory(Path.GetDirectoryName(quarantine)!); Directory.Move(source, quarantine);
    }
    public Task<string> StageAsync(TransitionManifest manifest, IAssetSource assets, CancellationToken cancellationToken = default) => StageCore(manifest, assets, false, cancellationToken);
    public Task<string> StageRecoveryAsync(TransitionManifest manifest, IAssetSource assets, CancellationToken cancellationToken = default) => StageCore(manifest, assets, true, cancellationToken);
    private async Task<string> StageCore(TransitionManifest manifest, IAssetSource assets, bool recovery, CancellationToken cancellationToken)
    {
        using var lease = Lease(); ValidateManifest(manifest);
        if (File.Exists(JournalPath)) throw new InvalidOperationException("Recovery is required.");
        var current = await LoadCurrent(cancellationToken);
        if (recovery) { if (current is null || manifest.SourceId is not null) throw new InvalidDataException("Recovery requires an existing installation and full manifest."); }
        else { if ((current?.Current.Identity.InventoryId != manifest.SourceId || current?.Current.Identity.Version != manifest.SourceVersion)) throw new InvalidDataException("Source identity mismatch."); if (current is not null) { if (!IsNewer(manifest.Version, current.Current.Identity.Version)) throw new InvalidDataException("Normal update target must be newer than its source."); Verify(Bundle(current.Current.Identity.InventoryId), current.Current); } }
        var requiredDiskBytes = checked(manifest.Target.Sum(file => file.Size) + 2L * 1024 * 1024);
        var drive = new DriveInfo(Path.GetPathRoot(root)!);
        if (drive.AvailableFreeSpace < requiredDiskBytes) throw new IOException("Insufficient disk space for a complete staged target.");
        var expectedCurrent = recovery ? Digest(current) : null;
        var stageDigest = Digest(new { manifest, recovery, expectedCurrent });
        var id = Token() + "-" + stageDigest; var dir = StageRoot(id); var target = Path.Combine(dir, "bundle"); Directory.CreateDirectory(target);
        try
        {
        var downloaded = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var operation in manifest.Operations.Where(x => x.Kind != "delete"))
        {
            var destination = SafeFile(target, operation.Path); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using (var output = new FileStream(destination, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.Asynchronous, PreallocationSize = operation.Size }))
            await using (var input = operation.Kind == "unchanged" ? File.OpenRead(SafeFile(Bundle(current!.Current.Identity.InventoryId), operation.Path)) : downloaded.TryGetValue(operation.AssetName!, out var existing) ? File.OpenRead(existing) : await assets.OpenAsync(operation.AssetName!, cancellationToken))
            {
                var buffer = new byte[65536]; long count = 0; int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0) { count += read; if (count > operation.Size) throw new InvalidDataException("Asset exceeds declared size."); await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken); }
                if (count != operation.Size) throw new InvalidDataException("Asset size mismatch.");
            }
            await using (var verifyStream = File.OpenRead(destination))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(verifyStream, cancellationToken)).ToLowerInvariant();
                if (hash != operation.Sha256) throw new InvalidDataException("Asset hash mismatch.");
            }
            if (operation.Kind != "unchanged") downloaded.TryAdd(operation.AssetName!, destination);
        }
        Verify(target, new(new(manifest.Version, manifest.TargetId), manifest.Target));
        await Write(Path.Combine(dir, "stage.json"), new Stage(manifest, stageDigest, recovery, expectedCurrent), cancellationToken); events?.Invoke("Update staged."); return id;
        }
        catch
        {
            try { CleanupFailedStage(dir, manifest); } catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException or InvalidDataException) { events?.Invoke("Failed stage evidence retained: " + cleanup.Message); }
            throw;
        }
    }
    private static void CleanupFailedStage(string dir, TransitionManifest manifest)
    {
        Guard(dir); if (!Directory.Exists(dir)) return;
        var bundle = Path.Combine(dir, "bundle");
        if (Directory.EnumerateFileSystemEntries(dir).Select(Path.GetFileName).Any(name => name is not ("bundle" or "stage.json"))) throw new InvalidDataException("Unexpected failed-stage evidence.");
        if (Directory.Exists(bundle))
        {
            var declared = manifest.Target.ToDictionary(x => x.Path, StringComparer.Ordinal);
            foreach (var file in UpdatePlanner.Inventory(bundle)) if (!declared.TryGetValue(file.Path, out var expected) || file.Size > expected.Size) throw new InvalidDataException("Unexpected failed-stage file.");
        }
        DeleteTree(dir);
    }
    public async Task ApplyAsync(string stageId, IApplicationLauncher launcher, CancellationToken cancellationToken = default)
    {
        CheckStageId(stageId);
        using var lease = Lease(); using var appLease = AppLease();
        if (File.Exists(JournalPath)) throw new InvalidOperationException("Recovery is required.");
        var stage = await Read<Stage>(Path.Combine(StageRoot(stageId), "stage.json"), cancellationToken) ?? throw new InvalidDataException("Stage not found.");
        ValidateManifest(stage.Manifest); if (Digest(new { manifest = stage.Manifest, recovery = stage.Recovery, expectedCurrent = stage.ExpectedCurrent }) != stage.Digest || stageId[33..] != stage.Digest) throw new InvalidDataException("Stage digest mismatch.");
        var before = await LoadCurrent(cancellationToken);
        if (stage.Recovery) { if (before is null || stage.Manifest.SourceId is not null || Digest(before) != stage.ExpectedCurrent) throw new InvalidDataException("Recovery source changed."); }
        else { if ((before?.Current.Identity.InventoryId != stage.Manifest.SourceId || before?.Current.Identity.Version != stage.Manifest.SourceVersion)) throw new InvalidDataException("Source identity mismatch."); if (before is not null) { if (!IsNewer(stage.Manifest.Version, before.Current.Identity.Version)) throw new InvalidDataException("Normal update target must be newer than its source."); Verify(Bundle(before.Current.Identity.InventoryId), before.Current); } }
        var candidate = new Receipt(new(stage.Manifest.Version, stage.Manifest.TargetId), stage.Manifest.Target);
        Verify(Path.Combine(StageRoot(stageId), "bundle"), candidate);
        var transaction = Token(); var snapshot = await CaptureState(transaction, cancellationToken);
        var sameRecovery = stage.Recovery && before!.Current.Identity.InventoryId == candidate.Identity.InventoryId;
        var journal = new Journal(transaction, before, candidate, snapshot, stage.Recovery, sameRecovery && Directory.Exists(Bundle(candidate.Identity.InventoryId)), stageId, Directory.Exists(Bundle(candidate.Identity.InventoryId))); await WriteJournal(journal, cancellationToken); checkpoint?.Invoke("journaled");
        var bundle = Bundle(candidate.Identity.InventoryId); Directory.CreateDirectory(Path.GetDirectoryName(bundle)!);
        if (sameRecovery && journal.HadCurrentBundle)
        {
            var healthy = true; try { Verify(bundle, before!.Current); } catch (InvalidDataException) { healthy = false; }
            if (!healthy) { var old = Quarantine(journal, "old"); Directory.CreateDirectory(Path.GetDirectoryName(old)!); Directory.Move(bundle, old); checkpoint?.Invoke("quarantined"); }
        }
        if (stage.Recovery && !sameRecovery && before is not null)
        {
            var prior = Bundle(before.Current.Identity.InventoryId);
            if (Directory.Exists(prior))
            {
                var retained = before.Current.Identity.InventoryId == before.Previous?.Identity.InventoryId || before.Current.Identity.InventoryId == before.Anchor?.Identity.InventoryId;
                if (retained) Verify(prior, before.Current);
                else { var evidence = Quarantine(journal, "old-current"); Directory.CreateDirectory(Path.GetDirectoryName(evidence)!); Directory.Move(prior, evidence); checkpoint?.Invoke("quarantined-current"); }
            }
        }
        if (Directory.Exists(bundle)) Verify(bundle, candidate); else Directory.Move(Path.Combine(StageRoot(stageId), "bundle"), bundle);
        Verify(bundle, candidate); checkpoint?.Invoke("published"); appLease.Dispose();
        bool ready; var readinessToken = Token();
        try { ready = await launcher.LaunchAsync(Path.Combine(bundle, "app"), readinessToken, cancellationToken); }
        catch { await Abort(journal, launcher, CancellationToken.None); throw; }
        if (!ready) { await Abort(journal, launcher, CancellationToken.None); events?.Invoke("Candidate readiness failed."); throw new InvalidOperationException("Candidate did not become ready."); }
        var committed = new InstallRecord(candidate, stage.Recovery ? before?.Previous : before?.Current, transaction, stage.Recovery ? before?.PreviousStateSnapshot : before is null ? null : snapshot, SelectAnchor(before, candidate), before?.DisplacedStateSnapshot, stage.Recovery ? before?.PreviousReceiptId : before?.TransactionId, SelectAnchorId(before, candidate, transaction));
        try { if (!launcher.IsRunning) throw new InvalidOperationException("Candidate exited before commit."); await Commit(committed, readinessToken, launcher, cancellationToken); }
        catch { await StopAndAbortIfUncommitted(journal, launcher); throw; }
        checkpoint?.Invoke("committed");
        await RecordHistory(journal, "committed", CancellationToken.None); await Prune(before, committed, CancellationToken.None); await CleanupStage(journal, CancellationToken.None); File.Delete(JournalPath); await CleanupTemporarySnapshot(journal, committed); events?.Invoke("Update committed.");
    }
    private async Task CleanupStage(Journal journal, CancellationToken ct)
    {
        if (journal.StageId is null) return;
        CheckStageId(journal.StageId); var dir = StageRoot(journal.StageId); if (!Directory.Exists(dir)) return;
        var entries = Directory.EnumerateFileSystemEntries(dir).Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);
        if (entries.Any(x => x is not ("bundle" or "stage.json"))) throw new InvalidDataException("Unexpected staged evidence.");
        var metadata = Path.Combine(dir, "stage.json"); var bundle = Path.Combine(dir, "bundle");
        if (!File.Exists(metadata)) { if (entries.Count != 0) throw new InvalidDataException("Staged metadata missing."); Directory.Delete(dir); return; }
        var stage = await Read<Stage>(metadata, ct) ?? throw new InvalidDataException("Staged metadata missing.");
        if (Digest(new { manifest = stage.Manifest, recovery = stage.Recovery, expectedCurrent = stage.ExpectedCurrent }) != stage.Digest || stage.Digest != journal.StageId[33..]) throw new InvalidDataException("Staged metadata mismatch.");
        if (Directory.Exists(bundle))
        {
            var expected = journal.Candidate.Inventory.ToDictionary(x => x.Path, StringComparer.Ordinal);
            foreach (var file in UpdatePlanner.Inventory(bundle)) if (!expected.TryGetValue(file.Path, out var declared) || file != declared) throw new InvalidDataException("Staged cleanup bytes differ.");
            DeleteTree(bundle);
        }
        File.Delete(metadata); Directory.Delete(dir);
    }
    private string SnapshotRoot(string id) { CheckToken(id); return Guard(Path.Combine(root, "updates", "snapshots", id)); }
    private static void CopyTree(string source, string destination, Action? afterChunk = null)
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
                else { if (++count > 8192 || (bytes += new FileInfo(entry).Length) > 2L * 1024 * 1024 * 1024) throw new InvalidDataException("State exceeds snapshot limit."); if (afterChunk is null) File.Copy(entry, target); else { using var input = File.OpenRead(entry); using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None); var buffer = new byte[65536]; int read; while ((read = input.Read(buffer)) > 0) { output.Write(buffer, 0, read); afterChunk(); } } }
            }
        }
    }
    private async Task<string> CaptureState(string id, CancellationToken ct)
    {
        var dir = SnapshotRoot(id); Directory.CreateDirectory(dir);
        CopyTree(Guard(Path.Combine(root, "state")), Path.Combine(dir, "app"));
        var inventory = UpdatePlanner.Inventory(dir); await Write(Path.Combine(dir, "snapshot.json"), new Snapshot(inventory, Digest(inventory)), ct); return id;
    }
    private string RestoreWorkspace(string transaction)
    {
        CheckToken(transaction); return Guard(Path.Combine(root, "updates", "restores", transaction));
    }
    private void CleanupSupersededState(string transaction, string targetId, InventoryFile[] sourceInventory)
    {
        CheckToken(targetId); var displaced = Guard(Path.Combine(RestoreWorkspace(transaction), targetId + "-superseded"));
        if (!Directory.Exists(displaced)) return;
        var expected = sourceInventory.ToDictionary(x => x.Path, StringComparer.Ordinal);
        foreach (var file in UpdatePlanner.InventoryState(displaced)) if (!expected.TryGetValue(file.Path, out var declared) || file != declared) throw new InvalidDataException("Superseded state differs from its snapshot.");
        DeleteTree(displaced, () => checkpoint?.Invoke("state-cleanup-entry"));
    }
    private void CleanupRestoreAttempts(string transaction, string snapshotId, InventoryFile[] inventory)
    {
        CheckToken(snapshotId); var workspace = RestoreWorkspace(transaction); if (!Directory.Exists(workspace)) return;
        var prefix = snapshotId + "-attempt-"; var expected = inventory.ToDictionary(x => x.Path, StringComparer.Ordinal);
        foreach (var attempt in Directory.EnumerateDirectories(workspace, prefix + "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(attempt); var suffix = name[prefix.Length..];
            if (suffix.Length != 32 || suffix.Any(c => !Uri.IsHexDigit(c) || char.IsUpper(c))) continue;
            foreach (var file in UpdatePlanner.InventoryState(attempt))
            {
                if (!expected.TryGetValue(file.Path, out var declared) || file.Size > declared.Size) throw new InvalidDataException("Unexpected restore attempt evidence.");
                var relative = file.Path[4..].Replace('/', Path.DirectorySeparatorChar);
                using var partial = File.OpenRead(Path.Combine(attempt, relative)); using var complete = File.OpenRead(Path.Combine(SnapshotRoot(snapshotId), "app", relative));
                var left = new byte[65536]; var right = new byte[65536]; int read;
                while ((read = partial.Read(left, 0, left.Length)) > 0)
                {
                    complete.ReadExactly(right.AsSpan(0, read));
                    if (!left.AsSpan(0, read).SequenceEqual(right.AsSpan(0, read))) throw new InvalidDataException("Restore attempt bytes differ from snapshot.");
                }
            }
            DeleteTree(attempt, () => checkpoint?.Invoke("state-attempt-cleanup-entry"));
        }
    }
    private async Task RestoreState(string id, string sourceSnapshot, string transaction, CancellationToken ct)
    {
        var desired = await ReadSnapshot(id, ct); var source = await ReadSnapshot(sourceSnapshot, ct);
        var workspace = RestoreWorkspace(transaction); Directory.CreateDirectory(workspace);
        var prepared = Guard(Path.Combine(workspace, id + "-attempt-" + Token()));
        CopyTree(Path.Combine(SnapshotRoot(id), "app"), prepared, () => { ct.ThrowIfCancellationRequested(); checkpoint?.Invoke("state-copy-file"); });
        if (!UpdatePlanner.InventoryState(prepared).SequenceEqual(desired.Inventory)) throw new InvalidDataException("Prepared state differs from its snapshot.");
        var state = Guard(Path.Combine(root, "state"));
        var superseded = Guard(Path.Combine(workspace, id + "-superseded"));
        if (Directory.Exists(state) && UpdatePlanner.InventoryState(state).SequenceEqual(desired.Inventory))
        {
            DeleteTree(prepared); CleanupSupersededState(transaction, id, source.Inventory); CleanupRestoreAttempts(transaction, id, desired.Inventory); return;
        }
        if (Directory.Exists(state))
        {
            if (UpdatePlanner.InventoryState(state).SequenceEqual(source.Inventory) && !Directory.Exists(superseded)) Directory.Move(state, superseded);
            else
            {
                var evidence = Guard(Path.Combine(root, "updates", "recovery", transaction, id + "-" + Token()));
                Directory.CreateDirectory(Path.GetDirectoryName(evidence)!); Directory.Move(state, evidence); events?.Invoke("Unexpected state retained at " + evidence);
            }
            checkpoint?.Invoke("state-displaced");
        }
        Directory.Move(prepared, state); checkpoint?.Invoke("state-published");
        CleanupSupersededState(transaction, id, source.Inventory); CleanupRestoreAttempts(transaction, id, desired.Inventory);
    }
    private string Quarantine(Journal journal, string name)
    {
        CheckToken(journal.TransactionId);
        return Guard(Path.Combine(root, "updates", "quarantine", journal.TransactionId, name));
    }
    private void RestoreQuarantinedBundle(Journal journal)
    {
        if (!journal.Recovery || journal.Before is null) return;
        if (journal.Before.Current.Identity.InventoryId != journal.Candidate.Identity.InventoryId)
        {
            var evidence = Quarantine(journal, "old-current"); var original = Bundle(journal.Before.Current.Identity.InventoryId);
            if (Directory.Exists(evidence)) { if (Directory.Exists(original)) throw new InvalidDataException("Conflicting recovery evidence."); Directory.Move(evidence, original); }
            return;
        }
        var old = Quarantine(journal, "old"); var bundle = Bundle(journal.Candidate.Identity.InventoryId);
        if (Directory.Exists(old) || !journal.HadCurrentBundle)
        {
            if (Directory.Exists(bundle))
            {
                Verify(bundle, journal.Candidate); var displaced = Quarantine(journal, "candidate");
                if (Directory.Exists(displaced)) throw new InvalidDataException("Recovery candidate evidence already exists.");
                Directory.CreateDirectory(Path.GetDirectoryName(displaced)!); Directory.Move(bundle, displaced);
            }
            if (Directory.Exists(old)) Directory.Move(old, bundle);
        }
    }
    private void CleanupUncommittedCandidate(Journal journal)
    {
        if (journal.StageId is null || journal.HadTargetBundle) return;
        var id = journal.Candidate.Identity.InventoryId;
        if (id == journal.Before?.Current.Identity.InventoryId || id == journal.Before?.Previous?.Identity.InventoryId || id == journal.Before?.Anchor?.Identity.InventoryId) return;
        var bundle = Bundle(id); var deleting = Guard(Path.Combine(root, "updates", "deleting", journal.TransactionId, "candidate"));
        if (Directory.Exists(bundle))
        {
            if (Directory.Exists(deleting)) throw new InvalidDataException("Conflicting candidate cleanup evidence.");
            Verify(bundle, journal.Candidate); Directory.CreateDirectory(Path.GetDirectoryName(deleting)!); Directory.Move(bundle, deleting);
        }
        if (Directory.Exists(deleting))
        {
            var expected = journal.Candidate.Inventory.ToDictionary(x => x.Path, StringComparer.Ordinal);
            foreach (var file in UpdatePlanner.Inventory(deleting)) if (!expected.TryGetValue(file.Path, out var declared) || file != declared) throw new InvalidDataException("Candidate cleanup bytes differ.");
            DeleteTree(deleting);
        }
    }
    private async Task StopAndAbortIfUncommitted(Journal journal, IApplicationLauncher launcher)
    {
        await launcher.StopAsync(CancellationToken.None);
        var current = await LoadCurrent(CancellationToken.None);
        if (current?.TransactionId != journal.TransactionId)
        {
            if (Digest(current) != Digest(journal.Before)) throw new InvalidDataException("Commit outcome does not match journal.");
            await Abort(journal, launcher, CancellationToken.None);
        }
    }
    private async Task CleanupUncommittedReceipt(Journal journal, CancellationToken ct)
    {
        var path = Guard(Path.Combine(root, "updates", "transactions", journal.TransactionId + ".receipt.json"));
        var receipt = await Read<CommitReceipt>(path, ct); if (receipt is null) return;
        if (receipt.Record.TransactionId != journal.TransactionId || receipt.RecordDigest != Digest(receipt.Record) || Digest(receipt.Record.Current) != Digest(journal.Candidate)) throw new InvalidDataException("Uncommitted receipt differs from journal.");
        File.Delete(path);
    }
    private async Task Abort(Journal journal, IApplicationLauncher launcher, CancellationToken ct)
    {
        RestoreQuarantinedBundle(journal);
        CleanupUncommittedCandidate(journal);
        await RestoreState(journal.StateSnapshot, journal.StateSnapshot, journal.TransactionId, ct);
        if (journal.Before?.PreviousStateSnapshot is not null) { var source = await ReadSnapshot(journal.StateSnapshot, ct); CleanupSupersededState(journal.TransactionId, journal.Before.PreviousStateSnapshot, source.Inventory); var target = await ReadSnapshot(journal.Before.PreviousStateSnapshot, ct); CleanupRestoreAttempts(journal.TransactionId, journal.Before.PreviousStateSnapshot, target.Inventory); }
        await RecordHistory(journal, "failed", ct);
        if (!journal.Recovery && journal.Before is not null) { Verify(Bundle(journal.Before.Current.Identity.InventoryId), journal.Before.Current); if (!await launcher.LaunchAsync(Path.Combine(Bundle(journal.Before.Current.Identity.InventoryId), "app"), Token(), ct)) throw new InvalidOperationException("Previous application did not become ready; recovery remains pending."); }
        await CleanupStage(journal, ct); await CleanupUncommittedReceipt(journal, ct); File.Delete(JournalPath); await CleanupTemporarySnapshot(journal, journal.Before);
    }
    public async Task RollbackAsync(IApplicationLauncher launcher, CancellationToken cancellationToken = default)
    {
        using var lease = Lease(); using var appLease = AppLease();
        if (File.Exists(JournalPath)) throw new InvalidOperationException("Recovery is required.");
        var before = await LoadCurrent(cancellationToken) ?? throw new InvalidOperationException("No installation exists.");
        if (before.DisplacedStateSnapshot is not null && !await IsResolved(before.DisplacedStateSnapshot, cancellationToken)) throw new InvalidOperationException("Displaced state must be resolved before another rollback.");
        if (before.Previous is null || before.PreviousStateSnapshot is null) throw new InvalidOperationException("No rollback is available.");
        Verify(Bundle(before.Current.Identity.InventoryId), before.Current); Verify(Bundle(before.Previous.Identity.InventoryId), before.Previous);
        var transaction = Token(); var snapshot = await CaptureState(transaction, cancellationToken);
        var journal = new Journal(transaction, before, before.Previous, snapshot); await WriteJournal(journal, cancellationToken);
        await RestoreState(before.PreviousStateSnapshot, snapshot, transaction, cancellationToken); checkpoint?.Invoke("rollback-restored"); appLease.Dispose();
        bool ready; var readinessToken = Token();
        try { ready = await launcher.LaunchAsync(Path.Combine(Bundle(before.Previous.Identity.InventoryId), "app"), readinessToken, cancellationToken); }
        catch { await Abort(journal, launcher, CancellationToken.None); throw; }
        if (!ready) { await Abort(journal, launcher, CancellationToken.None); throw new InvalidOperationException("Rollback candidate did not become ready."); }
        var committed = new InstallRecord(before.Previous, before.Current, transaction, snapshot, SelectAnchor(before, before.Previous), snapshot, before.TransactionId, SelectAnchorId(before, before.Previous, transaction));
        try { if (!launcher.IsRunning) throw new InvalidOperationException("Candidate exited before commit."); await Commit(committed, readinessToken, launcher, cancellationToken); }
        catch { await StopAndAbortIfUncommitted(journal, launcher); throw; }
        checkpoint?.Invoke("rollback-committed");
        await RecordHistory(journal, "committed", CancellationToken.None); await Prune(before, committed, CancellationToken.None); await CleanupStage(journal, CancellationToken.None); File.Delete(JournalPath); events?.Invoke("Rollback committed; displaced state retained.");
    }
    public async Task RecoverAsync(IApplicationLauncher launcher, CancellationToken cancellationToken = default)
    {
        using var lease = Lease(); using var appLease = AppLease(); var journal = await LoadJournal(cancellationToken); if (journal is null) return;
        var current = await LoadCurrent(cancellationToken);
        if (current?.TransactionId == journal.TransactionId)
        {
            if (Digest(current.Current) != Digest(journal.Candidate)) throw new InvalidDataException("Committed candidate differs from journal.");
            Verify(Bundle(current.Current.Identity.InventoryId), current.Current); appLease.Dispose();
            if (!await launcher.LaunchAsync(Path.Combine(Bundle(current.Current.Identity.InventoryId), "app"), Token(), cancellationToken)) throw new InvalidOperationException("Committed application did not become ready; recovery remains pending.");
            try
            {
                await RecordHistory(journal, "committed", cancellationToken); await Prune(journal.Before, current, cancellationToken); await CleanupStage(journal, cancellationToken);
                if (!launcher.IsRunning) throw new InvalidOperationException("Committed application exited during recovery.");
                File.Delete(JournalPath); await CleanupTemporarySnapshot(journal, current); return;
            }
            catch { await launcher.StopAsync(CancellationToken.None); throw; }
        }
        if (Digest(current) != Digest(journal.Before)) throw new InvalidDataException("Recovery state mismatch; evidence retained.");
        appLease.Dispose(); await Abort(journal, launcher, cancellationToken);
    }
}
