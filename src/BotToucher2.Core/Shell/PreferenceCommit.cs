namespace BotToucher2.Core.Shell;

/// <summary>Atomic replacement preserves the actual displaced bytes, even after a comparison race.</summary>
internal static class PreferenceCommit
{
    internal static async Task<string?> ReplaceAsync(string path, string temporary, byte[]? expected)
    {
        if (expected is null)
        {
            File.Move(temporary, path, overwrite: false);
            return null;
        }
        var backup = path + ".conflict-" + Guid.NewGuid().ToString("N");
        // No overwrite-only fallback: filesystems without atomic replace fail safely.
        File.Replace(temporary, path, backup);
        try
        {
            var displaced = await File.ReadAllBytesAsync(backup);
            if (!displaced.AsSpan().SequenceEqual(expected))
                return "Preferences changed during saving. The displaced external version is preserved at: " + backup;
            File.Delete(backup);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "Layout was saved, but the previous preferences could not be verified or removed. They are preserved at: " + backup;
        }
    }
}
