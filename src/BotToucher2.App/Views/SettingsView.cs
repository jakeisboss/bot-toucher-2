using System.Windows;
using System.Windows.Controls;
using BotToucher2.Core.Shell;
namespace BotToucher2.App.Views;
public sealed class SettingsView : UserControl
{
    private readonly CheckBox _left = new() { Content = "Show left navigation rail" };
    private readonly CheckBox _right = new() { Content = "Show right rail" };
    private bool _updating;
    public SettingsView(Action<bool, bool> visibilityChanged)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = "Settings", FontSize = 24, FontWeight = FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = "Shell layout", Margin = new Thickness(0, 20, 0, 8) });
        content.Children.Add(_left); content.Children.Add(_right);
        content.Children.Add(new TextBlock { Text = "Navigation, rail visibility and rail widths are saved on close. No workload settings are defined in 0.0.", Margin = new Thickness(0, 16, 0, 0) });
        Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _left.Click += Changed; _right.Click += Changed;
        void Changed(object sender, RoutedEventArgs args)
        {
            if (!_updating) visibilityChanged(_left.IsChecked == true, _right.IsChecked == true);
        }
    }
    public void ShowState(ShellState state)
    {
        _updating = true;
        _left.IsChecked = state.LeftVisible; _right.IsChecked = state.RightVisible;
        _updating = false;
    }
}
