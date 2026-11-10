using System.Diagnostics;
using System.Text.Json;
using BotToucher2.Update;

namespace BotToucher2.Updater;

public sealed record ExternalCommandResult(string State, int? ExitCode, string Output, string Error);
internal sealed record DispatchRequest(int ParentPid, string[] Arguments, string WorkerDirectory);

/// <summary>CLI process handoff only; UpdateEngine remains the sole mutation authority.</summary>
public static class ExternalCommandDispatch
{
    public static bool RequiresWorker(string command, string root, string runningDirectory) =>
        command is "apply" or "install" or "install-release" or "rollback" or "recover" or "full-recover" && IsWithin(runningDirectory, root);

    public static bool IsWithin(string path, string directory)
    {
        var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var child = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return child.Equals(parent, StringComparison.OrdinalIgnoreCase) || child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
    public static async Task<string?> WorkerForAsync(string command, string root, string runningDirectory)
    {
        if (!RequiresWorker(command, root, runningDirectory)) return null; // Explicitly invoked independent bootstrap trust boundary.
        var worker = await new UpdateEngine(root).PrepareWorkerAsync();
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(runningDirectory)).Equals(worker, StringComparison.OrdinalIgnoreCase) ? null : worker;
    }
    public static string[] WorkerArguments(string[] args, string root, string token, int parentPid, string? originalDirectory = null)
    {
        CheckToken(token);
        var callerDirectory = Path.GetFullPath(originalDirectory ?? Environment.CurrentDirectory);
        var result = new List<string> { args[0] };
        for (var i = 1; i < args.Length; i += 2)
        {
            if (args[i] is "--dispatch-token" or "--wait-dispatch-pid") throw new ArgumentException("Reserved dispatch option.");
            if (args[i] == "--root") continue;
            result.Add(args[i]);
            result.Add(args[i] is "--manifest" or "--assets" or "--app" or "--engine" or "--out" or "--source"
                ? Path.GetFullPath(args[i + 1], callerDirectory) : args[i + 1]);
        }
        result.AddRange(new[] { "--root", Path.GetFullPath(root, callerDirectory), "--dispatch-token", token, "--wait-dispatch-pid", parentPid.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        return result.ToArray();
    }
    public static ProcessStartInfo WorkerStartInfo(string worker, string[] forwarded)
    {
        // Both loaded images and the process CWD must be outside mutation targets.
        var start = new ProcessStartInfo(Path.Combine(worker, "BotToucher2.Updater.exe")) { UseShellExecute = false, WorkingDirectory = worker };
        foreach (var arg in forwarded) start.ArgumentList.Add(arg);
        return start;
    }
    private static void CheckToken(string token)
    {
        if (token.Length != 32 || token.Any(c => !Uri.IsHexDigit(c) || char.IsUpper(c))) throw new InvalidDataException("Invalid dispatch token.");
    }
    private static string OwnedPath(string root, string token, string suffix)
    {
        CheckToken(token);
        var path = Path.GetFullPath(Path.Combine(root, "control", "commands", token + suffix));
        for (var cursor = path; cursor is not null; cursor = Path.GetDirectoryName(cursor))
            if (Path.Exists(cursor) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Reparse dispatch path rejected.");
        return path;
    }
    public static string ResultPath(string root, string token) => OwnedPath(root, token, ".result.json");
    public static async Task WriteResultAsync(string root, string token, ExternalCommandResult result)
    {
        var path = ResultPath(root, token); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = OwnedPath(root, token, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { await JsonSerializer.SerializeAsync(file, result); await file.FlushAsync(); file.Flush(true); }
            // Result pollers can hold Ready open while the worker publishes its outcome.
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static async Task<ExternalCommandResult?> ReadResultAsync(string root, string token)
    {
        try
        {
            await using var file = new FileStream(ResultPath(root, token), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (file.Length > 1024 * 1024) throw new InvalidDataException("Dispatch result exceeds limit.");
            return await JsonSerializer.DeserializeAsync<ExternalCommandResult>(file);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
    public static async Task<string> StartAsync(string root, string worker, string[] args)
    {
        var token = Guid.NewGuid().ToString("N"); var forwarded = WorkerArguments(args, root, token, Environment.ProcessId);
        var requestPath = OwnedPath(root, token, ".request.json"); Directory.CreateDirectory(Path.GetDirectoryName(requestPath)!);
        await using (var file = new FileStream(requestPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await JsonSerializer.SerializeAsync(file, new DispatchRequest(Environment.ProcessId, forwarded, worker));
        var start = WorkerStartInfo(worker, forwarded);
        Process? process = null;
        try
        {
            process = Process.Start(start) ?? throw new IOException("Could not start verified updater worker.");
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                var result = await ReadResultAsync(root, token);
                if (result?.State == "Ready") return ResultPath(root, token);
                if (result?.State == "Failed" || process.HasExited) throw new IOException("Updater worker initialization failed: " + (result?.Error ?? "No worker result; exit " + process.ExitCode));
                await Task.Delay(25);
            }
            throw new IOException("Worker initialization timed out; command was not accepted.");
        }
        catch (Exception error)
        {
            // Never leave an unaccepted process waiting to mutate after this parent exits.
            if (process is not null && !process.HasExited) { process.Kill(); await process.WaitForExitAsync(); }
            await WriteResultAsync(root, token, new("Failed", 1, "", error.Message));
            throw new IOException(error.Message + " Result: " + ResultPath(root, token), error);
        }
        finally { process?.Dispose(); }
    }

    public static async Task ValidateWorkerAsync(string root, string runningDirectory, string token, int parentPid, string[] args)
    {
        // A flag alone never authorizes execution: independently re-verify the selected
        // engine, the owned worker path, and the exact handoff arguments before waiting.
        if (!RequiresWorker(args[0], root, runningDirectory) || await WorkerForAsync(args[0], root, runningDirectory) is not null)
            throw new InvalidDataException("Dispatch must run from the independently verified worker.");
        await using var file = new FileStream(OwnedPath(root, token, ".request.json"), FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > 65536) throw new InvalidDataException("Dispatch request exceeds limit.");
        var request = await JsonSerializer.DeserializeAsync<DispatchRequest>(file) ?? throw new InvalidDataException("Missing dispatch request.");
        if (parentPid <= 0 || parentPid == Environment.ProcessId || request.ParentPid != parentPid || !request.Arguments.SequenceEqual(args) || !Path.GetFullPath(runningDirectory).TrimEnd(Path.DirectorySeparatorChar).Equals(request.WorkerDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Dispatch request mismatch.");
    }
    public static async Task WaitForExitAsync(int pid)
    {
        try { using var parent = Process.GetProcessById(pid); await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2)); }
        catch (ArgumentException) { /* Parent already exited. */ }
    }
}
