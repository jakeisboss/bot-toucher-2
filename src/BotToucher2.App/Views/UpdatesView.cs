using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace BotToucher2.App.Views;
public sealed class UpdatesView : UserControl
{
    private readonly UpdateController? _controller;
    private readonly TextBlock _status = new() { Text = "Updates have not been checked." };
    private readonly Button _check = new() { Content = "Check for updates" };
    private readonly Button _install = new() { Content = "Install update", IsEnabled = false };
    private readonly Button _rollback = new() { Content = "Roll back", IsEnabled = false };
    private readonly TextBlock _currentVersion = new(), _currentState = new(), _previousVersion = new(), _previousState = new(), _transaction = new(), _outcome = new(), _transactionDetails = new();
    public UpdatesView(UpdateController? controller)
    {
        _controller = controller;
        NameScope.SetNameScope(this, new NameScope());
        RegisterName("CurrentVersionValue", _currentVersion); RegisterName("PreviousVersionValue", _previousVersion);
        RegisterName("PreviousAvailabilityValue", _previousState); RegisterName("LastTransactionValue", _transaction); RegisterName("TransactionOutcomeValue", _outcome); RegisterName("RollbackButton", _rollback);
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Updates", FontSize = 20, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Manual updates from GitHub Releases. Nothing downloads or installs automatically.", Margin = new Thickness(0, 10, 0, 16) });
        panel.Children.Add(_status); panel.Children.Add(_check); panel.Children.Add(_install);
        panel.Children.Add(new TextBlock { Text = "Rollback / Installed Versions", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 22, 0, 10) });
        panel.Children.Add(Record("Current", _currentVersion, _currentState));
        panel.Children.Add(Record("Previous known-good", _previousVersion, _previousState, _rollback));
        panel.Children.Add(Record("Last recorded transaction", _transaction, _outcome, _transactionDetails));
        panel.Children.Add(new TextBlock { Text = "One previous known-good version is retained before 1.0. Rollback restores its saved state and preserves the state being replaced.", Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 8, 0, 0) });
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        ShowInstallation();
        Loaded += async (_, _) => await RunAsync("status");
        _check.Click += async (_, _) => await RunAsync("check");
        _install.Click += async (_, _) =>
        {
            if (MessageBox.Show(Window.GetWindow(this), "Download, verify and install this update? BT2 will close and relaunch after staging completes.", "Install update", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                await RunAsync("install");
        };
        _rollback.Click += async (_, _) =>
        {
            if (MessageBox.Show(Window.GetWindow(this), $"Restore previous known-good {_controller?.InstallationStatus?.Previous?.Version ?? "version"} and its state snapshot? Your current state will be preserved in a reported recovery snapshot.", "Roll back", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
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
            ShowInstallation();
        }
    }
    private Border Record(string heading, TextBlock version, TextBlock state, UIElement? extra = null)
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = heading, Foreground = (Brush)FindResource("Accent"), FontWeight = FontWeights.SemiBold });
        version.FontSize = 20; version.Margin = new Thickness(0, 7, 0, 5); body.Children.Add(version);
        state.Foreground = (Brush)FindResource("Muted"); body.Children.Add(state);
        if (extra is not null) { if (extra is FrameworkElement element) element.Margin = new Thickness(0, 10, 0, 0); body.Children.Add(extra); }
        return new Border { Background = (Brush)FindResource("Bg2"), BorderBrush = (Brush)FindResource("Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 10), Child = body };
    }
    private void ShowInstallation()
    {
        var status = _controller?.InstallationStatus;
        var selectionUnavailable = status is null || (status.Pending && status.Current is null);
        _currentVersion.Text = status is null ? "State unavailable" : status.Current?.Version ?? (status.Pending ? "Selection pending" : "Not a managed install");
        _currentState.Text = status is null ? "Updater state could not be read" : status.Current is not null ? "Installed / verification recorded" : status.Pending ? "Transaction busy or pending; refresh after recovery" : "No installed verification receipt available";
        _previousVersion.Text = selectionUnavailable ? "Previous state unavailable" : status?.Previous?.Version ?? "No previous known-good";
        _previousState.Text = status is null ? "Updater state could not be read" : status.Pending ? "Pending transaction — rollback is unavailable" : status?.Previous is null ? "No rollback available" : status.DisplacedStateDirectory is null ? "Available for rollback; verified again before restoration" : "Retained rollback target; preserved-state requirements are checked on request";
        _rollback.Content = status?.Previous is null ? "Roll back" : "Roll back to " + status.Previous.Version;
        var last = _controller?.LastTransaction;
        _transaction.Text = _controller?.HistoryAvailable != true ? "Transaction record unavailable" : last is null ? "No transaction recorded" : "Target " + last.Version;
        _outcome.Text = last?.Outcome switch { "committed" => "Verified / successful", "failed" => "Failed / not accepted", null => "", _ => "Recorded outcome: " + last.Outcome };
        _transactionDetails.Text = last is null ? "" : $"{last.TimestampUtc.ToLocalTime():g}\nTransaction {last.TransactionId}";
    }

}
