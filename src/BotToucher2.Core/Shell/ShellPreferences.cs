using System.Text.Json;
namespace BotToucher2.Core.Shell;

public sealed record PreferenceLoad(ShellState State, string? Notice);

/// <summary>A session-scoped store. Unsafe or externally changed files are preserved.</summary>
public sealed class ShellPreferences(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private byte[]? _baseline;
    private bool _loaded;
    private bool _safeToSave;
    public async Task<PreferenceLoad> LoadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_loaded) throw new InvalidOperationException("Preferences have already been loaded.");
            _loaded = true;
            try
            {
                _baseline = await ReadExistingAsync();
                var state = _baseline is null ? new ShellState() :
                    JsonSerializer.Deserialize<ShellState>(_baseline, Options) ?? throw new JsonException("Empty layout.");
                state.Validate();
                _safeToSave = true;
                return new(state, null);
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or IOException or UnauthorizedAccessException)
            {
                _safeToSave = false;
                return new(new ShellState(), "Layout preferences could not be loaded. Defaults are in use; the original file is preserved and saving is disabled for this session.");
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<string?> SaveAsync(ShellState state)
    {
        state.Validate();
        await _gate.WaitAsync();
        string? temporary = null;
        try
        {
            if (!_loaded || !_safeToSave)
                return "Layout was not saved because its original preferences are unsafe. The existing file was preserved.";
            var bytes = JsonSerializer.SerializeToUtf8Bytes(state, Options);
            try
            {
                var current = await ReadExistingAsync();
                if (!SameBytes(current, _baseline))
                {
                    _safeToSave = false;
                    return "Layout preferences changed outside this application. The file was preserved; restart to load those changes.";
                }
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllBytesAsync(temporary, bytes);
                // Check again after preparing the replacement, before the atomic rename.
                if (!SameBytes(await ReadExistingAsync(), _baseline))
                {
                    _safeToSave = false;
                    return "Layout preferences changed outside this application and were preserved.";
                }
                File.Move(temporary, path, overwrite: _baseline is not null);
                _baseline = bytes;
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _safeToSave = false;
                return "Layout could not be saved. Existing preferences were preserved.";
            }
        }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Nonfatal temporary file. */ }
            }
            _gate.Release();
        }
    }
    private async Task<byte[]?> ReadExistingAsync()
    {
        try { return await File.ReadAllBytesAsync(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
    private static bool SameBytes(byte[]? a, byte[]? b) =>
        a is null ? b is null : b is not null && a.AsSpan().SequenceEqual(b);
}
