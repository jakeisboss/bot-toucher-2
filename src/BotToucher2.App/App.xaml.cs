using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using BotToucher2.App.Hardware;
using BotToucher2.Core.Activity;
using BotToucher2.Core.Hardware;
using BotToucher2.Core.Shell;
using BotToucher2.Update;
namespace BotToucher2.App;

public partial class App : Application
{
    private readonly CancellationTokenSource _stop = new();
    private Task? _monitorTask;
    private ShellPreferences? _preferences;
    private ActivityLog? _activity;
    private UpdateController? _updates;
    private FileStream? _instance;
    private bool _closing;
    private bool _allowClose;
    private bool _candidateWaiting;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var root = Path.GetFullPath(Option(e.Args, "--bt2-root") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BotToucher2"));
            Directory.CreateDirectory(Path.Combine(root, "control"));
            _instance = new FileStream(Path.Combine(root, "control", "app.instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
            _activity = new ActivityLog(root);
            await Record("STARTUP", "Bot Toucher 2 0.1.0 started. No update checks, installs or tests run at startup.");
            var state = Path.Combine(root, "state"); Directory.CreateDirectory(state);
            var preferencesPath = Path.Combine(state, "layout.json");
            var readyToken = Option(e.Args, "--bt2-ready");
            var legacy = Path.Combine(root, "layout.json");
            if (readyToken is null && !File.Exists(preferencesPath) && File.Exists(legacy))
            {
                File.Copy(legacy, preferencesPath, overwrite: false);
                await Record("PREFERENCES_IMPORT", "Imported 0.0 shell preferences; original layout.json retained.");
            }
            _preferences = new ShellPreferences(preferencesPath);
            var loaded = await _preferences.LoadAsync();
            if (loaded.Notice is not null) await Record("PREFERENCES_NOTICE", loaded.Notice, "Warning");
            _updates = new UpdateController(root, _activity);
            var window = new MainWindow(loaded.State, loaded.Notice, root, _activity, _updates);
            MainWindow = window;
            window.Closing += ClosingRequested;
            _updates.HandoffRequested = (mode, stage, worker) =>
            {
                var start = new ProcessStartInfo(Path.Combine(worker, "BotToucher2.Updater.exe")) { UseShellExecute = false, WorkingDirectory = worker };
                start.ArgumentList.Add(mode); start.ArgumentList.Add("--root"); start.ArgumentList.Add(root);
                start.ArgumentList.Add("--wait-pid"); start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (stage.Length != 0) { start.ArgumentList.Add("--stage"); start.ArgumentList.Add(stage); }
                using var helper = Process.Start(start) ?? throw new IOException("Could not start the external updater; the app remains open.");
                window.Close();
            };
            var monitor = new HardwareMonitor(new WindowsHardwareSampler(), TimeSpan.FromSeconds(2));
            string? lastHardwareState = null;
            monitor.Observed += observation =>
            {
                if (_stop.IsCancellationRequested) return;
                var hardwareState = observation.Notice is null ? "ready" : observation.Notice;
                if (hardwareState != lastHardwareState)
                {
                    lastHardwareState = hardwareState;
                    _ = Record(observation.Notice is null ? "HARDWARE_READY" : "HARDWARE_ERROR", observation.Notice ?? "Hardware monitor initialized; unsupported readings are reported unavailable.", observation.Notice is null ? "Info" : "Warning");
                }
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { if (!_closing) window.SetHardwareObservation(observation); }));
            };
            _monitorTask = Task.Run(() => monitor.RunAsync(_stop.Token));
            if (readyToken is not null)
            {
                if (readyToken.Length != 32 || readyToken.Any(c => !Uri.IsHexDigit(c) || char.IsUpper(c))) throw new InvalidDataException("Invalid candidate readiness token.");
                _candidateWaiting = true; window.IsEnabled = false;
                window.Loaded += async (_, _) => await AcknowledgeReady(root, readyToken, window);
            }
            window.Show();
        }
        catch (Exception ex)
        {
            _stop.Cancel();
            if (_monitorTask is not null) { try { await _monitorTask; } catch (Exception) { } }
            await Record("STARTUP_ERROR", ex.Message, "Error");
            MessageBox.Show("Bot Toucher 2 could not start. " + ex.Message, "Bot Toucher 2", MessageBoxButton.OK, MessageBoxImage.Error);
            _updates?.Dispose(); _instance?.Dispose(); _stop.Dispose(); Shutdown(1);
        }
    }
    private async Task AcknowledgeReady(string root, string token, MainWindow window)
    {
        try
        {
            var readiness = Path.Combine(root, "updates", "readiness"); Directory.CreateDirectory(readiness);
            await using (var stream = new FileStream(Path.Combine(readiness, token + ".ready"), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(token); await stream.WriteAsync(bytes); stream.Flush(flushToDisk: true);
            }
            // Read-only protocol wait: only the helper may commit or recover a transaction.
            var engine = new UpdateEngine(root);
            while (!_closing)
            {
                var status = await Task.Run(() => engine.StatusAsync());
                if (!status.Pending)
                {
                    if (status.AppDirectory is null || !Path.GetFullPath(status.AppDirectory).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("The helper did not select this candidate as the current install.");
                    var statePath = Path.Combine(root, "state", "layout.json");
                    var legacyPath = Path.Combine(root, "layout.json");
                    if (!File.Exists(statePath) && File.Exists(legacyPath))
                    {
                        File.Copy(legacyPath, statePath, overwrite: false);
                        _preferences = new ShellPreferences(statePath);
                        var imported = await _preferences.LoadAsync();
                        window.RestoreLayout(imported.State);
                        if (imported.Notice is not null) window.ShowNotice(imported.Notice);
                        await Record("PREFERENCES_IMPORT", "Imported original 0.0 preferences after candidate commit; original retained.");
                    }
                    _candidateWaiting = false; window.IsEnabled = true;
                    await Record("APPLICATION_READY", "Application initialized and the helper confirmed the active installation.");
                    return;
                }
                await Task.Delay(100, _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex)
        {
            await Record("READINESS_ERROR", ex.Message, "Error");
            window.ShowNotice("Candidate readiness failed: " + ex.Message);
            window.Close();
        }
    }
    private async void ClosingRequested(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_closing || sender is not MainWindow window) return;
        _closing = true; window.BeginShutdown(); _stop.Cancel();
        try { if (_monitorTask is not null) await _monitorTask; }
        catch (Exception ex) { await Record("HARDWARE_SHUTDOWN_ERROR", ex.Message, "Warning"); }
        try
        {
            var notice = _candidateWaiting || _preferences is null ? null : await _preferences.SaveAsync(window.State);
            if (notice is not null) { await Record("PREFERENCES_SAVE_NOTICE", notice, "Warning"); MessageBox.Show(window, notice, "Layout preferences", MessageBoxButton.OK, MessageBoxImage.Information); }
            await Record("SHUTDOWN", "Shell shutdown completed; hardware sampling drained.");
        }
        finally
        {
            _updates?.Dispose(); _instance?.Dispose(); _stop.Dispose(); _allowClose = true; window.Close(); Shutdown();
        }
    }
    private async Task Record(string code, string message, string severity = "Info")
    { try { if (_activity is not null) await _activity.RecordAsync(code, message, severity); } catch (Exception) { /* Logging failure must not block startup/shutdown. */ } }
    private static string? Option(string[] args, string key)
    {
        var index = Array.IndexOf(args, key);
        if (index < 0) return null;
        if (index + 1 >= args.Length) throw new ArgumentException("Missing value for " + key);
        return args[index + 1];
    }
}
