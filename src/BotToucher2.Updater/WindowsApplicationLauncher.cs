using System.Diagnostics;
using BotToucher2.Update;
namespace BotToucher2.Updater;

/// <summary>OS/process boundary only; the shared engine owns the transaction.</summary>
public sealed class WindowsApplicationLauncher(string root) : IApplicationLauncher, IDisposable
{
    private Process? _candidate;
    public bool HasActiveApplication => _candidate is not null && !_candidate.HasExited;
    public async Task<bool> LaunchAsync(string appDirectory, string transactionToken, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual BT2 relaunch/readiness requires Windows.");
        if (transactionToken.Length != 32 || transactionToken.Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("Invalid readiness token.");
        if (HasActiveApplication) throw new InvalidOperationException("A candidate is already running.");
        _candidate?.Dispose();
        var readiness = Path.Combine(root, "updates", "readiness"); Directory.CreateDirectory(readiness);
        var marker = Path.Combine(readiness, transactionToken + ".ready");
        if (File.Exists(marker)) throw new IOException("Readiness marker already exists.");
        var start = new ProcessStartInfo(Path.Combine(appDirectory, "BotToucher2.exe")) { UseShellExecute = false, WorkingDirectory = appDirectory };
        start.ArgumentList.Add("--bt2-root"); start.ArgumentList.Add(root);
        start.ArgumentList.Add("--bt2-ready"); start.ArgumentList.Add(transactionToken);
        start.ArgumentList.Add("--bt2-helper-pid"); start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _candidate = Process.Start(start) ?? throw new IOException("Could not launch BT2.");
        try
        {
            var limit = DateTime.UtcNow.AddSeconds(45);
            while (!_candidate.HasExited && DateTime.UtcNow < limit)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(marker) && await File.ReadAllTextAsync(marker, cancellationToken) == transactionToken)
                { File.Delete(marker); return true; }
                await Task.Delay(100, cancellationToken);
            }
            await StopAsync(CancellationToken.None); return false;
        }
        catch { await StopAsync(CancellationToken.None); throw; }
    }
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_candidate is null) return;
        if (!_candidate.HasExited) { _candidate.Kill(entireProcessTree: true); await _candidate.WaitForExitAsync(cancellationToken); }
        _candidate.Dispose(); _candidate = null;
    }
    public void Dispose() { _candidate?.Dispose(); _candidate = null; } // Success leaves the app running.
}
