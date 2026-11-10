using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BotToucher2.Core.Activity;
namespace BotToucher2.App.Views;

/// <summary>Application-owned information/activity, adapted from BT1 without widget plumbing.</summary>
public sealed class HomeView : UserControl
{
    public HomeView(string root, ActivityLog activity)
    {
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
        content.Children.Add(Card("RECENT ACTIVITY", new ActivityFeed(activity)));
        Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
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
}
