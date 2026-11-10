using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using BotToucher2.App.Views;
using BotToucher2.Core.Hardware;
using BotToucher2.Core.Activity;
using BotToucher2.Core.Shell;
namespace BotToucher2.App;

public partial class MainWindow : Window
{
    private readonly Dictionary<Destination, FrameworkElement> _surfaces;
    private readonly PacketCenterView _packets;
    private readonly SettingsView _settings;
    private bool _updating;
    private bool _ready;
    public ShellState State { get; private set; }
    public MainWindow(ShellState initialState, string? notice, string? root = null, ActivityLog? activity = null, UpdateController? updates = null)
    {
        initialState.Validate();
        State = initialState;
        InitializeComponent();
        root ??= System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BotToucher2");
        activity ??= new ActivityLog(root);
        _packets = new PacketCenterView(activity);
        _packets.SectionSelected += section =>
        {
            if (section == PacketSection.UpdateCenter) OpenUpdates();
            else ApplyState(State with { PacketSection = section });
        };
        _settings = new SettingsView((left, right) => ApplyState(State with { LeftVisible = left, RightVisible = right }), updates);
        var applicationVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "Unavailable";
        _surfaces = new()
        {
            [Destination.Home] = new HomeView(root, activity),
            [Destination.PacketCenter] = _packets,
            [Destination.Data] = new WorkspaceSurface("Data", "Data functionality is reserved for a later version."),
            [Destination.Lab] = new WorkspaceSurface("Lab", "A clean test workspace for future systems.", true),
            [Destination.Settings] = _settings,
            [Destination.Help] = new WorkspaceSurface("Help / About", $"Bot Toucher 2 • Version {applicationVersion}\nWindows shell\n\nCtrl+1 Home • Ctrl+2 Packet Center • Ctrl+3 Data • Ctrl+4 Lab\nCtrl+, Settings • F1 Help\nCtrl+Shift+L toggles the left rail • Ctrl+L toggles the right rail\n\nThe hardware monitor observes this computer. Unsupported metrics are shown as unavailable.")
        };
        LeftSplitter.AddHandler(Keyboard.KeyDownEvent, new KeyEventHandler(RailKeyDown), handledEventsToo: true);
        RightSplitter.AddHandler(Keyboard.KeyDownEvent, new KeyEventHandler(RailKeyDown), handledEventsToo: true);
        _ready = true;
        if (initialState.Destination == Destination.PacketCenter && initialState.PacketSection == PacketSection.UpdateCenter)
            OpenUpdates();
        else ApplyState(initialState);
        if (notice is not null) ShowNotice(notice);
    }
    private void ApplyState(ShellState state)
    {
        state.Validate();
        State = state;
        _updating = true;
        try
        {
            ConfigureRail(LeftColumn, LeftDivider, LeftRail, LeftSplitter, state.LeftVisible, state.LeftWidth, 140);
            ConfigureRail(RightColumn, RightDivider, RightRail, RightSplitter, state.RightVisible, state.RightWidth, 220);
            ToggleLeft.Content = state.LeftVisible ? "Hide left rail" : "Show left rail";
            ToggleRight.Content = state.RightVisible ? "Hide right rail" : "Show right rail";
            _packets.ShowSection(state.PacketSection);
            _settings.ShowState(state);
            WorkspaceHost.Content = _surfaces[state.Destination];
            var title = state.Destination switch { Destination.PacketCenter => "Packet Center", Destination.Help => "Help / About", _ => state.Destination.ToString() };
            ContextText.Text = state.Destination == Destination.PacketCenter ? title + " / " +
                (state.PacketSection == PacketSection.UpdateCenter ? "Update Center" : state.PacketSection.ToString()) : title;
            foreach (var button in new[] { HomeNav, PacketNav, DataNav, LabNav, SettingsNav, HelpNav })
            {
                var selected = button.Tag is string tag && Enum.TryParse<Destination>(tag, out var destination) && destination == state.Destination;
                button.BorderBrush = (Brush)FindResource(selected ? "Accent" : "Border");
                button.BorderThickness = new Thickness(selected ? 2 : 1);
                AutomationProperties.SetItemStatus(button, selected ? "Selected" : "");
            }
        }
        finally { _updating = false; }
    }
    private static void ConfigureRail(ColumnDefinition column, ColumnDefinition divider, FrameworkElement rail,
        GridSplitter splitter, bool visible, double width, double minimum)
    {
        column.MinWidth = visible ? minimum : 0;
        column.Width = new GridLength(visible ? width : 0);
        divider.Width = new GridLength(visible ? 5 : 0);
        rail.Visibility = splitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }
    public void RestoreLayout(ShellState state)
    {
        state.Validate();
        if (state.Destination == Destination.PacketCenter && state.PacketSection == PacketSection.UpdateCenter)
        {
            ApplyState(state with { Destination = Destination.Settings, PacketSection = PacketSection.Packets });
            _settings.OpenUpdates();
        }
        else ApplyState(state);
    }
    public void ShowNotice(string notice)
    {
        NoticeText.Text = notice; NoticeBorder.Visibility = Visibility.Visible;
    }
    public void SetHardwareObservation(HardwareObservation observation) => HardwarePanel.SetObservation(observation);
    public void SetUpdatePending(bool pending)
    {
        // Disable edits while preserving the native window close control.
        if (Content is UIElement content) content.IsEnabled = !pending;
        StatusText.Text = pending ? "Waiting for updater commit — no state edits are enabled" : "Shell ready";
    }
    public void BeginShutdown()
    {
        IsEnabled = false;
        StatusText.Text = "Closing — waiting for hardware sampling and saving layout…";
    }
    private void NavigateClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && Enum.TryParse<Destination>(tag, out var destination))
            ApplyState(State with { Destination = destination });
    }
    private void ToggleLeftClicked(object sender, RoutedEventArgs e) => ApplyState(State with { LeftVisible = !State.LeftVisible });
    private void ToggleRightClicked(object sender, RoutedEventArgs e) => ApplyState(State with { RightVisible = !State.RightVisible });
    public void OpenUpdates()
    {
        ApplyState(State with { Destination = Destination.Settings, PacketSection = PacketSection.Packets });
        _settings.OpenUpdates();
    }
    private void RailDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (!_ready || _updating || e.Canceled) return;
        RecordRailWidths();
    }
    private void RailKeyDown(object sender, KeyEventArgs e)
    {
        // WPF's keyboard splitter path does not raise DragCompleted. Its class handler
        // has already updated column widths when this handled-events-too handler runs.
        if (!_ready || _updating || e.Key is not (Key.Left or Key.Right)) return;
        UpdateLayout();
        RecordRailWidths();
    }
    private void RecordRailWidths()
    {
        ApplyState(State with {
            LeftWidth = State.LeftVisible ? Math.Clamp(LeftColumn.ActualWidth, 140, 300) : State.LeftWidth,
            RightWidth = State.RightVisible ? Math.Clamp(RightColumn.ActualWidth, 220, 360) : State.RightWidth
        });
    }
    private void ShellKeyDown(object sender, KeyEventArgs e)
    {
        if (!_ready) return;
        if (e.Key == Key.F1) { ApplyState(State with { Destination = Destination.Help }); e.Handled = true; return; }
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        if (e.Key == Key.L)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) ToggleLeftClicked(this, e);
            else ToggleRightClicked(this, e);
            e.Handled = true; return;
        }
        Destination? destination = e.Key switch {
            Key.D1 or Key.NumPad1 => Destination.Home, Key.D2 or Key.NumPad2 => Destination.PacketCenter,
            Key.D3 or Key.NumPad3 => Destination.Data, Key.D4 or Key.NumPad4 => Destination.Lab,
            Key.OemComma => Destination.Settings, _ => null
        };
        if (destination is not null) { ApplyState(State with { Destination = destination.Value }); e.Handled = true; }
    }
}
