using System.Text;
using System.Text.Json;
namespace BotToucher2.Core.Activity;

public sealed record ActivityEntry(DateTimeOffset Timestamp, string Code, string Message, string Severity);

/// <summary>One bounded shell/update event stream. No invented events or workload semantics.</summary>
public sealed class ActivityLog(string root)
{
    private const int MaximumBytes = 512 * 1024;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public string Path => System.IO.Path.Combine(root, "logs", "activity.jsonl");
    public event Action? Changed;
    public async Task RecordAsync(string code, string message, string severity = "Info")
    {
        var item = new ActivityEntry(DateTimeOffset.UtcNow, Bound(code, 64), Bound(message, 4096), Bound(severity, 16));
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(item) + "\n");
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            await using var file = await OpenWriterAsync().ConfigureAwait(false);
            if (file.Length > MaximumBytes)
            {
                file.Seek(-MaximumBytes / 2, SeekOrigin.End);
                using var reader = new StreamReader(file, Encoding.UTF8, leaveOpen: true);
                await reader.ReadLineAsync();
                var tail = await reader.ReadToEndAsync();
                file.SetLength(0); file.Position = 0;
                await file.WriteAsync(Encoding.UTF8.GetBytes(tail));
            }
            file.Seek(0, SeekOrigin.End);
            await file.WriteAsync(bytes);
            await file.FlushAsync();
        }
        finally { _gate.Release(); }
        Changed?.Invoke();
    }
    public async Task<IReadOnlyList<ActivityEntry>> RecentAsync(int count = 50)
    {
        count = Math.Clamp(count, 1, 256);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(Path)) return Array.Empty<ActivityEntry>();
            await using var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var partial = stream.Length > MaximumBytes;
            if (partial) stream.Seek(-MaximumBytes, SeekOrigin.End);
            using var reader = new StreamReader(stream);
            if (partial) await reader.ReadLineAsync();
            var entries = new Queue<ActivityEntry>();
            while (await reader.ReadLineAsync() is { } line)
            {
                if (line.Length > 8192) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<ActivityEntry>(line);
                    if (entry is null || entry.Code is null || entry.Message is null) continue;
                    entries.Enqueue(entry);
                    if (entries.Count > count) entries.Dequeue();
                }
                catch (JsonException) { /* Incomplete/invalid log rows do not hide valid events. */ }
            }
            return entries.Reverse().ToArray();
        }
        finally { _gate.Release(); }
    }
    private async Task<FileStream> OpenWriterAsync()
    {
        for (var i = 0; ; i++)
        {
            try { return new FileStream(Path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read); }
            catch (IOException) when (i < 20) { await Task.Delay(25).ConfigureAwait(false); }
        }
    }
    private static string Bound(string text, int limit) => text.Length <= limit ? text : text[..limit];
}
