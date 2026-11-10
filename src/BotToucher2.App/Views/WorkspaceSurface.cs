using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace BotToucher2.App.Views;

/// <summary>A plain destination surface, not a widget host or workload container.</summary>
public sealed class WorkspaceSurface : UserControl
{
    public WorkspaceSurface(string title, string description, bool workspace = false)
    {
        var panel = new DockPanel();
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        header.Children.Add(new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = description, Margin = new Thickness(0, 8, 0, 0), Foreground = (Brush)FindResource("Muted") });
        DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        if (workspace)
        {
            panel.Children.Add(new Border {
                BorderBrush = (Brush)FindResource("Border"), BorderThickness = new Thickness(1), Padding = new Thickness(24),
                Child = new TextBlock { Text = "Workspace is empty", Foreground = (Brush)FindResource("Muted"),
                    VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center }
            });
        }
        else panel.Children.Add(new Grid());
        Content = panel;
    }
}
