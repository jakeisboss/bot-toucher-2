using System.Windows;
using System.Windows.Controls;
namespace BotToucher2.App.Views;
public sealed class UpdatesView : UserControl
{
    private readonly UpdateController? _controller;
    private readonly TextBlock _status = new() { Text = "Updates have not been checked." };
    private readonly Button _check = new() { Content = "Check for updates" };
    private readonly Button _install = new() { Content = "Install update", IsEnabled = false };
    private readonly Button _rollback = new() { Content = "Roll back", IsEnabled = false };
    public UpdatesView(UpdateController? controller)
    {
        _controller = controller;
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Updates", FontSize = 20, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Manual updates from GitHub Releases. Nothing downloads or installs automatically.", Margin = new Thickness(0, 10, 0, 16) });
        panel.Children.Add(_status); panel.Children.Add(_check); panel.Children.Add(_install);
        panel.Children.Add(new TextBlock { Text = "Rollback restores the previous known-good application and its saved state. Current state is preserved separately before restoration.", Margin = new Thickness(0, 18, 0, 6) });
        panel.Children.Add(_rollback);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Loaded += async (_, _) => await RunAsync("status");
        _check.Click += async (_, _) => await RunAsync("check");
        _install.Click += async (_, _) =>
        {
            if (MessageBox.Show(Window.GetWindow(this), "Download, verify and install this update? BT2 will close and relaunch after staging completes.", "Install update", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                await RunAsync("install");
        };
        _rollback.Click += async (_, _) =>
        {
            if (MessageBox.Show(Window.GetWindow(this), "Restore the previous known-good version and its state snapshot? Your current state will be preserved in a reported recovery snapshot.", "Roll back", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                await RunAsync("rollback");
        };
    }
    private async Task RunAsync(string operation)
    {
        if (_controller is null) { _check.IsEnabled = false; _status.Text = "Update controls are unavailable in this isolated shell fixture."; return; }
        _check.IsEnabled = _install.IsEnabled = _rollback.IsEnabled = false;
        try
        {
            _status.Text = operation is "install" ? "Verifying source and staging required files…" : "Reading update metadata…";
            _status.Text = await _controller.RunAsync(operation);
        }
        catch (Exception ex) { _status.Text = "Update operation failed: " + ex.Message; }
        finally
        {
            _check.IsEnabled = true;
            _install.IsEnabled = _controller.CanInstall;
            _rollback.IsEnabled = _controller.CanRollback;
        }
    }
}
