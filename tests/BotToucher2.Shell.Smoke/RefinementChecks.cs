using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BotToucher2.App;
using BotToucher2.App.Views;
using BotToucher2.Core.Activity;
using BotToucher2.Update;

internal static class RefinementChecks
{
    public static async Task RunAsync(MainWindow shell)
    {
        var failures = new List<string>();
        void Expect(bool value, string message) { Console.WriteLine((value ? "PASS: " : "FAIL: ") + message); if (!value) failures.Add(message); }
        Expect((shell.Background as SolidColorBrush)?.Color == ((SolidColorBrush)shell.FindResource("Bg0")).Color, "Derived main window uses navy background");
        Expect(typeof(MainWindow).Assembly.GetName().Version?.ToString(3) == "0.1.2", "Application reports 0.1.2");
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BT2-ui-state-" + Guid.NewGuid().ToString("N"));
        var input = System.IO.Path.Combine(root, "input");
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(input, "app")); System.IO.Directory.CreateDirectory(System.IO.Path.Combine(input, "engine"));
        System.IO.File.WriteAllText(System.IO.Path.Combine(input, "app", "BotToucher2.exe"), "inert test inventory, never executed");
        System.IO.File.WriteAllText(System.IO.Path.Combine(input, "engine", "BotToucher2.Updater.exe"), "inert test inventory, never executed");
        var engineRoot = System.IO.Path.Combine(root, "installed"); var engine = new UpdateEngine(engineRoot);
        using var controller = new UpdateController(engineRoot, new ActivityLog(engineRoot));
        var view = new UpdatesView(controller); var window = new Window { Content = view, Width = 780, Height = 760, Background = (Brush)shell.FindResource("Bg0") };
        window.Show();
        try
        {
            await controller.RunAsync("status");
            await Reload(view, window);
            Expect(Value(view, "CurrentVersionValue") == "Not a managed install", "Empty install has honest unavailable current state");
            Expect(Value(view, "LastTransactionValue") == "No transaction recorded", "Empty history has no fake events");
            var first = UpdatePlanner.CreateManifest("0.1.1", null, UpdatePlanner.Inventory(input));
            await engine.ApplyAsync(await engine.StageAsync(first, new Assets(input, first)), new Launcher(true));
            System.IO.File.WriteAllText(System.IO.Path.Combine(input, "app", "data"), "second");
            var second = UpdatePlanner.CreateManifest("0.1.2", first.Target, UpdatePlanner.Inventory(input), first.Version);
            await engine.ApplyAsync(await engine.StageAsync(second, new Assets(input, second)), new Launcher(true));
            await Reload(view, window);
            Expect(Value(view, "CurrentVersionValue") == "0.1.2", "Current version comes from verified receipt");
            Expect(Value(view, "PreviousVersionValue") == "0.1.1", "Rollback target comes from previous known-good receipt");
            Expect(Value(view, "LastTransactionValue").Contains("0.1.2") && Value(view, "TransactionOutcomeValue").Contains("successful"), "Committed transaction displays recorded target/outcome");
            Expect((view.FindName("RollbackButton") as Button)?.IsEnabled == true, "Rollback action enabled for actual previous version");
            Capture(window, "Updates-installed");
            await engine.RollbackAsync(new Launcher(true)); await Reload(view, window);
            Expect(Value(view, "CurrentVersionValue") == "0.1.1" && Value(view, "PreviousVersionValue") == "0.1.2", "UI follows real rollback selection");
            await engine.AcknowledgeDisplacedStateAsync();
            System.IO.File.WriteAllText(System.IO.Path.Combine(input, "app", "data"), "failed target");
            var failed = UpdatePlanner.CreateManifest("0.1.3", first.Target, UpdatePlanner.Inventory(input), first.Version);
            try { await engine.ApplyAsync(await engine.StageAsync(failed, new Assets(input, failed)), new Launcher(false)); } catch (InvalidOperationException) { }
            await Reload(view, window);
            Expect(Value(view, "CurrentVersionValue") == "0.1.1", "Failed target does not replace installed display");
            Expect(Value(view, "LastTransactionValue").Contains("0.1.3") && Value(view, "TransactionOutcomeValue").Contains("Failed"), "Failed transaction is not presented as verified success");
        }
        finally { window.Close(); }
        foreach (var name in new[] { "HomeNav", "LabNav", "SettingsNav", "PacketNav", "DataNav", "HelpNav" })
        {
            ((Button)shell.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            shell.UpdateLayout(); Capture(shell, name);
        }
        if (failures.Count > 0) throw new Exception("0.1.2 regressions: " + string.Join("; ", failures));
    }
    private static string Value(UpdatesView view, string name) => (view.FindName(name) as TextBlock)?.Text ?? "";
    private static async Task Reload(UpdatesView view, Window window)
    {
        window.Content = null; window.Content = view; window.UpdateLayout();
        await Task.Delay(250); // Allow the actual Loaded metadata-only refresh.
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }
    private static void Capture(Window window, string name)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var dir = System.IO.Path.GetFullPath("artifacts/0.1.2-ui"); System.IO.Directory.CreateDirectory(dir);
        using var file = System.IO.File.Create(System.IO.Path.Combine(dir, name + ".png")); encoder.Save(file);
    }
    private sealed class Launcher(bool success) : IApplicationLauncher
    { public bool IsRunning => true; public Task<bool> LaunchAsync(string path, string token, CancellationToken ct) => Task.FromResult(success); public Task StopAsync(CancellationToken ct) => Task.CompletedTask; }
    private sealed class Assets(string input, TransitionManifest manifest) : IAssetSource
    { public Task<System.IO.Stream> OpenAsync(string name, CancellationToken ct) => Task.FromResult<System.IO.Stream>(System.IO.File.OpenRead(System.IO.Path.Combine(input, manifest.Target.First(f => f.Sha256 == name).Path))); }
}
