using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Media;
using BotToucher2.Core.Activity;
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
        application.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () =>
        {
            try
            {
                Check(window.IsVisible && window.IsLoaded, "WPF window launches");
                Check(Find<Border>(window, "NoticeBorder").IsVisible, "Preference notice is visible");
                Click(window, "HomeNav");
                window.UpdateLayout();
                var home = (HomeView)Find<ContentControl>(window, "WorkspaceHost").Content;
                var homeText = Descendants<TextBlock>(home).Select(text => text.Text).ToArray();
                Check(homeText.Contains("APPLICATION INFO") && homeText.Contains("RECENT ACTIVITY"), "Home information and activity surfaces present");
                Check(homeText.Contains(AppContext.BaseDirectory), "Application Info displays actual install location");
                Check(homeText.Contains(typeof(HomeView).Assembly.GetName().Version!.ToString(3)), "Application Info displays actual assembly version");
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
                Check(window.State.Destination == Destination.Settings, "Old update entry links to Settings");
                Click(window, "ToggleLeft"); Check(!window.State.LeftVisible && !Find<FrameworkElement>(window, "LeftRail").IsVisible, "Hide left rail");
                Click(window, "ToggleLeft"); Check(window.State.LeftVisible && Find<FrameworkElement>(window, "LeftRail").IsVisible, "Restore left rail");
                Click(window, "ToggleRight"); Check(!window.State.RightVisible && !Find<FrameworkElement>(window, "RightRail").IsVisible, "Hide right rail");
                Click(window, "ToggleRight"); Check(window.State.RightVisible && Find<FrameworkElement>(window, "RightRail").IsVisible, "Restore right rail");
                Check(window.FindName("RightTabs") is null, "Right rail has no widget tab");
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
                var activity = new ActivityLog(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bt2-feed-" + Guid.NewGuid().ToString("N")));
                await activity.RecordAsync("ERROR", "Diagnostic details must remain selected", "Error");
                var feed = new ActivityFeed(activity); var feedWindow = new Window { Content = feed, Width = 500, Height = 400 };
                feedWindow.Show();
                var list = Descendants<ListBox>(feed).Single();
                await Until(() => list.Items.Count == 1);
                list.SelectedIndex = 0; var selected = list.SelectedItem;
                await Task.Delay(2300); // Cross the real two-second DispatcherTimer refresh.
                Check(ReferenceEquals(selected, list.SelectedItem), "Unchanged activity refresh retains selected row");
                await activity.RecordAsync("START", "New activity arrived");
                await Until(() => list.Items.Count == 2);
                Check(Equals(((ListBoxItem)list.SelectedItem).Tag, ((ListBoxItem)selected).Tag), "New activity preserves the selected diagnostic");
                Check(Descendants<TextBlock>(feed).Any(text => text.Text == "Diagnostic details must remain selected"), "Selected full diagnostic remains readable");
                var selectedBeforeLock = list.SelectedItem;
                using (var lockedLog = new System.IO.FileStream(activity.Path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.None))
                    await Until(() => Descendants<TextBlock>(feed).Any(text => text.Text.StartsWith("Activity log is unavailable:", StringComparison.Ordinal)));
                await Until(() => Descendants<TextBlock>(feed).Any(text => text.Text == "Diagnostic details must remain selected"));
                Check(ReferenceEquals(selectedBeforeLock, list.SelectedItem), "Successful unchanged activity read clears error without rebuilding rows");
                feedWindow.Close();
                result = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { window.Close(); application.Shutdown(result); }
        }));
        application.Run(window);
        return result;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found) yield return found;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(25);
        Check(condition(), "Activity refresh completed within five seconds");
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
