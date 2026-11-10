using System.Diagnostics;
using BotToucher2.Update;
namespace BotToucher2.Updater;

/// <summary>OS boundary only. The shared engine owns all update/rollback state.</summary>
public sealed class WindowsApplicationLauncher(string root) : IApplicationLauncher
{
    public async Task<bool> LaunchAsync(string appDirectory, string transactionToken, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual BT2 relaunch/readiness requires Windows.");
        if (transactionToken.Length != 32 || transactionToken.Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("Invalid readiness token.");
        var readiness = Path.Combine(root, "updates", "readiness"); Directory.CreateDirectory(readiness);
        var marker = Path.Combine(readiness, transactionToken + ".ready");
        if (File.Exists(marker)) throw new IOException("Readiness marker already exists.");
        var start = new ProcessStartInfo(Path.Combine(appDirectory, "BotToucher2.exe")) { UseShellExecute = false, WorkingDirectory = appDirectory };
        start.ArgumentList.Add("--bt2-root"); start.ArgumentList.Add(root);
        start.ArgumentList.Add("--bt2-ready"); start.ArgumentList.Add(transactionToken);
        using var process = Process.Start(start) ?? throw new IOException("Could not launch BT2.");
        try
        {
            var limit = DateTime.UtcNow.AddSeconds(45);
            while (!process.HasExited && DateTime.UtcNow < limit)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(marker) && await File.ReadAllTextAsync(marker, cancellationToken) == transactionToken)
                { File.Delete(marker); return true; }
                await Task.Delay(100, cancellationToken);
            }
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); }
            return false;
        }
        catch
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); }
            throw;
        }
    }
}
