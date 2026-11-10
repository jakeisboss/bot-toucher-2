using System.Security.Cryptography;
using System.Text;

namespace BotToucher2.Update;

public sealed record InventoryFile(string Path, long Size, string Sha256);
public sealed record UpdateOperation(string Path, string Kind, string? AssetName, long Size, string? Sha256);
public sealed record TransitionManifest(string Version, string? SourceVersion, string? SourceId, string TargetId, InventoryFile[] Source, InventoryFile[] Target, UpdateOperation[] Operations, long RequiredBytes, int MinimumBootstrapProtocol = 1);

public static class UpdatePlanner
{
    public static TransitionManifest CreateManifest(string version, IEnumerable<InventoryFile>? source, IEnumerable<InventoryFile> target, string? sourceVersion = null)
    {
        if (!Version.TryParse(version, out _) || version.Length > 64) throw new InvalidDataException("Invalid version.");
        if ((source is null) != (sourceVersion is null) || (sourceVersion is not null && !Version.TryParse(sourceVersion, out _))) throw new InvalidDataException("Invalid source version.");
        var s = Validate(source ?? Array.Empty<InventoryFile>()); var t = Validate(target);
        var old = s.ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
        var next = t.ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
        var operations = new List<UpdateOperation>();
        foreach (var file in t)
        {
            old.TryGetValue(file.Path, out var before);
            var kind = before is null ? "add" : before == file ? "unchanged" : "replace";
            operations.Add(new(file.Path, kind, kind == "unchanged" ? null : file.Sha256, file.Size, file.Sha256));
        }
        foreach (var file in s.Where(x => !next.ContainsKey(x.Path))) operations.Add(new(file.Path, "delete", null, 0, null));
        return new(version, sourceVersion, source is null ? null : Identity(s), Identity(t), s, t, operations.ToArray(), operations.Where(x => x.Kind is "add" or "replace").Sum(x => x.Size));
    }

    public static string Identity(IEnumerable<InventoryFile> files)
    {
        var canonical = string.Concat(Validate(files).Select(x => $"{x.Path}\0{x.Size}\0{x.Sha256}\n"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public static InventoryFile[] Inventory(string directory)
    {
        var root = Path.GetFullPath(directory);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var files = new List<InventoryFile>(); var pending = new Stack<string>(); pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Reparse points are forbidden.");
            foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
            {
                var attrs = File.GetAttributes(entry);
                if ((attrs & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Reparse points are forbidden.");
                if ((attrs & FileAttributes.Directory) != 0) pending.Push(entry);
                else { var info = new FileInfo(entry); using var stream = File.OpenRead(entry); files.Add(new(Path.GetRelativePath(root, entry).Replace('\\', '/'), info.Length, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant())); }
            }
        }
        return Validate(files);
    }

    internal static InventoryFile[] InventoryState(string directory)
    {
        var files = new List<InventoryFile>(); var pending = new Stack<string>(); pending.Push(directory);
        while (pending.Count > 0)
        {
            var dir = pending.Pop(); if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Reparse point in state snapshot.");
            foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
            {
                var attrs = File.GetAttributes(entry); if ((attrs & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Reparse point in state snapshot.");
                if ((attrs & FileAttributes.Directory) != 0) pending.Push(entry);
                else { using var stream = File.OpenRead(entry); files.Add(new("app/" + Path.GetRelativePath(directory, entry).Replace('\\', '/'), new FileInfo(entry).Length, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant())); }
            }
        }
        return Validate(files);
    }
    internal static InventoryFile[] Validate(IEnumerable<InventoryFile> files)
    {
        var result = files.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
        if (result.Length > 8192 || result.Sum(x => x.Size) > 2L * 1024 * 1024 * 1024) throw new InvalidDataException("Inventory exceeds limit.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in result)
        {
            var parts = f.Path.Split('/');
            if (parts.Length < 2 || parts[0] is not ("app" or "engine") || f.Path.Contains('\\') || parts.Any(InvalidSegment) || !names.Add(f.Path) || f.Size < 0 || f.Size > 512L * 1024 * 1024 || f.Sha256.Length != 64 || f.Sha256.Any(c => !Uri.IsHexDigit(c)) || f.Sha256 != f.Sha256.ToLowerInvariant()) throw new InvalidDataException("Invalid inventory entry: " + f.Path);
        }
        return result;
    }
    private static bool InvalidSegment(string s)
    {
        if (string.IsNullOrWhiteSpace(s) || s is "." or ".." || s.EndsWith(' ') || s.EndsWith('.') || s.Any(c => c < 32 || "<>:\"|?*".Contains(c))) return true;
        var stem = s.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '1' and <= '9');
    }
}
