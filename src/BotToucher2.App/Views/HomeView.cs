using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BotToucher2.Core.Activity;
namespace BotToucher2.App.Views;

/// <summary>Application-owned information/activity, adapted from BT1 without widget plumbing.</summary>
public sealed class HomeView : UserControl
{
    private readonly ActivityLog _activity;
    private readonly ListBox _events = new() { MaxHeight = 250 };
    private readonly TextBlock _message = new();
    private bool _subscribed;
    public HomeView(string root, ActivityLog activity)
    {
        _activity = activity;
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = "Home", FontSize = 24, FontWeight = FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = "Application information and real shell activity.", Margin = new Thickness(0, 6, 0, 18), Foreground = (Brush)FindResource("Muted") });
        var info = new StackPanel();
        var assembly = typeof(HomeView).Assembly;
        var version = assembly.GetName().Version?.ToString(3) ?? "Unavailable";
        var build = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Add(info, "Application", "Bot Toucher 2");
        Add(info, "Current version", version);
        Add(info, "Build state", "Development build — Windows acceptance is reported separately");
        Add(info, "Build/source", build?.Contains('+') == true ? build : "Unavailable (source revision not embedded)");
        Add(info, "Install location", AppContext.BaseDirectory);
        Add(info, "State location", System.IO.Path.Combine(root, "state"));
        Add(info, "Activity log", activity.Path);
        content.Children.Add(Card("APPLICATION INFO", info));
        var recent = new StackPanel();
        recent.Children.Add(_events); recent.Children.Add(_message);
        _events.SelectionChanged += (_, _) => _message.Text = (_events.SelectedItem as ListBoxItem)?.Tag as string ?? "";
        content.Children.Add(Card("RECENT ACTIVITY", recent));
        Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Loaded += (_, _) => { if (!_subscribed) { _activity.Changed += Changed; _subscribed = true; } Refresh(); };
        Unloaded += (_, _) => { if (_subscribed) { _activity.Changed -= Changed; _subscribed = false; } };
    }
    private static void Add(Panel panel, string label, string value)
    {
        var row = new StackPanel { Margin = new Thickness(0, 5, 0, 8) };
        row.Children.Add(new TextBlock { Text = label, Opacity = .7, FontSize = 11 });
        row.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        panel.Children.Add(row);
    }
    private Border Card(string title, UIElement body)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = title, Foreground = (Brush)FindResource("Accent"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
        panel.Children.Add(body);
        return new Border { Background = (Brush)FindResource("Bg1"), BorderBrush = (Brush)FindResource("Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 16), Child = panel };
    }
    private void Changed() => Dispatcher.BeginInvoke(new Action(Refresh));
    private async void Refresh()
    {
        try
        {
            var rows = await _activity.RecentAsync(30);
            if (!IsLoaded) return;
            _events.Items.Clear();
            foreach (var row in rows)
                _events.Items.Add(new ListBoxItem { Content = new TextBlock { Text = $"{row.Timestamp.ToLocalTime():HH:mm:ss}  {row.Severity} / {row.Code}\n{row.Message}", TextWrapping = TextWrapping.Wrap }, Tag = row.Message });
            _message.Text = rows.Count == 0 ? "No activity has been recorded yet." : "Select an event to read its full message.";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { _message.Text = "Activity log is unavailable: " + ex.Message; }
    }
}
