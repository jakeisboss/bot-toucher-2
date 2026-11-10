using System.IO;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using BotToucher2.App.Hardware;
using BotToucher2.Core.Hardware;
using BotToucher2.Core.Shell;
namespace BotToucher2.App;

public partial class App : Application
{
    private readonly CancellationTokenSource _stop = new();
    private Task? _monitorTask;
    private ShellPreferences? _preferences;
    private bool _closing;
    private bool _allowClose;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BotToucher2", "layout.json");
            _preferences = new ShellPreferences(path);
            var loaded = await _preferences.LoadAsync();
            var window = new MainWindow(loaded.State, loaded.Notice);
            MainWindow = window;
            window.Closing += ClosingRequested;
            var monitor = new HardwareMonitor(new WindowsHardwareSampler(), TimeSpan.FromSeconds(2));
            monitor.Observed += observation =>
            {
                if (_stop.IsCancellationRequested) return;
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    if (!_closing) window.SetHardwareObservation(observation);
                }));
            };
            // Application lifetime, never view lifetime. All native work and disposal stay off the dispatcher.
            _monitorTask = Task.Run(() => monitor.RunAsync(_stop.Token));
            window.Show();
        }
        catch (Exception ex)
        {
            _stop.Cancel();
            if (_monitorTask is not null)
            {
                try { await _monitorTask; } catch (Exception) { /* The startup error remains primary. */ }
            }
            MessageBox.Show("Bot Toucher 2 could not start. " + ex.Message, "Bot Toucher 2", MessageBoxButton.OK, MessageBoxImage.Error);
            _stop.Dispose();
            Shutdown(1);
        }
    }
    private async void ClosingRequested(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_closing || sender is not MainWindow window) return;
        _closing = true;
        window.BeginShutdown();
        _stop.Cancel();
        try
        {
            if (_monitorTask is not null) await _monitorTask;
        }
        catch (Exception)
        {
            MessageBox.Show(window, "Hardware monitoring stopped with an error during shutdown.", "Bot Toucher 2", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        try
        {
            var notice = _preferences is null ? null : await _preferences.SaveAsync(window.State);
            if (notice is not null)
                MessageBox.Show(window, notice, "Layout preferences", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            _stop.Dispose();
            _allowClose = true;
            window.Close();
            Shutdown();
        }
    }
}
