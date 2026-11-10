using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using System.Windows.Input;
using BotToucher2.App;
using BotToucher2.App.Controls;
using BotToucher2.App.Views;
using BotToucher2.Core.Shell;

internal static class Program
{
    [STAThread] private static int Main()
    {
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.Resources.MergedDictionaries.Add(new ResourceDictionary {
            Source = new Uri("/BotToucher2;component/Theme.xaml", UriKind.Relative) });
        var result = 1;
        var window = new MainWindow(new ShellState(), "Preference recovery notice fixture");
        application.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            try
            {
                Check(window.IsVisible && window.IsLoaded, "WPF window launches");
                Check(Find<Border>(window, "NoticeBorder").IsVisible, "Preference notice is visible");
                foreach (var (button, destination) in new[] {
                    ("HomeNav", Destination.Home), ("PacketNav", Destination.PacketCenter),
                    ("DataNav", Destination.Data), ("LabNav", Destination.Lab),
                    ("SettingsNav", Destination.Settings), ("HelpNav", Destination.Help) })
                {
                    Click(window, button);
                    Check(window.State.Destination == destination && Find<ContentControl>(window, "WorkspaceHost").Content is FrameworkElement,
                        "Navigation " + destination);
                }
                Click(window, "PacketNav");
                var packets = (PacketCenterView)Find<ContentControl>(window, "WorkspaceHost").Content;
                var tabs = Find<TabControl>(packets, "PacketTabs");
                tabs.SelectedIndex = 2; Check(window.State.PacketSection == PacketSection.Activity, "Activity destination");
                tabs.SelectedIndex = 0; Check(window.State.PacketSection == PacketSection.Project, "Project destination");
                tabs.SelectedIndex = 1; Click(packets, "OpenUpdateCenter");
                Check(window.State.PacketSection == PacketSection.UpdateCenter && Find<FrameworkElement>(packets, "UpdateSurface").IsVisible,
                    "Update Center destination");
                Click(packets, "BackToPackets"); Check(window.State.PacketSection == PacketSection.Packets, "Return to Packets");
                Click(window, "ToggleLeft"); Check(!window.State.LeftVisible && !Find<FrameworkElement>(window, "LeftRail").IsVisible, "Hide left rail");
                Click(window, "ToggleLeft"); Check(window.State.LeftVisible && Find<FrameworkElement>(window, "LeftRail").IsVisible, "Restore left rail");
                Click(window, "ToggleRight"); Check(!window.State.RightVisible && !Find<FrameworkElement>(window, "RightRail").IsVisible, "Hide right rail");
                Click(window, "ToggleRight"); Check(window.State.RightVisible && Find<FrameworkElement>(window, "RightRail").IsVisible, "Restore right rail");
                Find<TabControl>(window, "RightTabs").SelectedIndex = 1;
                Check(window.State.RightTab == RailTab.Widgets, "Widgets destination");
                Find<TabControl>(window, "RightTabs").SelectedIndex = 0;
                Check(window.State.RightTab == RailTab.Context, "Context destination");
                window.Width = window.MinWidth; window.Height = window.MinHeight; window.UpdateLayout();
                Check(Find<ContentControl>(window, "WorkspaceHost").ActualWidth >= 260, "Minimum-size workspace remains usable");
                Find<ColumnDefinition>(window, "LeftColumn").Width = new GridLength(240); window.UpdateLayout();
                Find<GridSplitter>(window, "LeftSplitter").RaiseEvent(new DragCompletedEventArgs(60, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
                Check(window.State.LeftWidth == 240, "Left splitter records width");
                Find<ColumnDefinition>(window, "RightColumn").Width = new GridLength(320); window.UpdateLayout();
                Find<GridSplitter>(window, "RightSplitter").RaiseEvent(new DragCompletedEventArgs(40, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
                Check(window.State.RightWidth == 320, "Right splitter records width");
                var keyboardSplitter = Find<GridSplitter>(window, "LeftSplitter");
                keyboardSplitter.Focus();
                var priorWidth = window.State.LeftWidth;
                var source = PresentationSource.FromVisual(window) ?? throw new Exception("No WPF presentation source");
                keyboardSplitter.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Right) { RoutedEvent = Keyboard.KeyDownEvent });
                Check(window.State.LeftWidth > priorWidth, "Keyboard splitter resize updates layout state");
                var resizedWidth = window.State.LeftWidth;
                Click(window, "HomeNav");
                Check(window.State.LeftWidth == resizedWidth, "Navigation preserves keyboard splitter width");
                Check(Find<HardwareMonitorPanel>(window, "HardwarePanel").IsVisible, "Built-in monitor present outside widget tab");
                result = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { window.Close(); application.Shutdown(result); }
        }));
        application.Run(window);
        return result;
    }
    private static T Find<T>(FrameworkElement root, string name) where T : class =>
        root.FindName(name) as T ?? throw new Exception("Missing shell control: " + name);
    private static void Click(FrameworkElement root, string name) =>
        Find<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
