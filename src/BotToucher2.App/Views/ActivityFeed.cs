using System.Windows;
using System.Windows.Controls;
using BotToucher2.Core.Activity;
namespace BotToucher2.App.Views;
/// <summary>One presentation component for the shared bounded shell/update stream.</summary>
public sealed class ActivityFeed : UserControl
{
    private readonly ActivityLog _activity;
    private readonly ListBox _events = new() { MaxHeight = 350 };
    private readonly TextBlock _message = new();
    private bool _subscribed;
    public ActivityFeed(ActivityLog activity)
    {
        _activity = activity;
        var panel = new StackPanel(); panel.Children.Add(_events); panel.Children.Add(_message); Content = panel;
        _events.SelectionChanged += (_, _) => _message.Text = (_events.SelectedItem as ListBoxItem)?.Tag as string ?? "";
        Loaded += (_, _) => { if (!_subscribed) { _activity.Changed += Changed; _subscribed = true; } Refresh(); };
        Unloaded += (_, _) => { if (_subscribed) { _activity.Changed -= Changed; _subscribed = false; } };
    }
    private void Changed() { if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(Refresh)); }
    private async void Refresh()
    {
        try
        {
            var rows = await _activity.RecentAsync(50);
            if (!IsLoaded) return;
            _events.Items.Clear();
            foreach (var row in rows)
                _events.Items.Add(new ListBoxItem { Content = new TextBlock { Text = $"{row.Timestamp.ToLocalTime():HH:mm:ss}  {row.Severity} / {row.Code}\n{row.Message}", TextWrapping = TextWrapping.Wrap }, Tag = row.Message });
            _message.Text = rows.Count == 0 ? "No activity has been recorded yet." : "Select an event to read its full message.";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { _message.Text = "Activity log is unavailable: " + ex.Message; }
    }
}
