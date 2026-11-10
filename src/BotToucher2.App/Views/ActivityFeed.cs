using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BotToucher2.Core.Activity;
namespace BotToucher2.App.Views;
/// <summary>One presentation component for the shared bounded shell/update stream.</summary>
public sealed class ActivityFeed : UserControl
{
    private readonly ActivityLog _activity;
    private readonly ActivityFeedState _state = new();
    private readonly ListBox _events = new() { MaxHeight = 350 };
    private readonly TextBlock _message = new() { Text = "No activity has been recorded yet." };
    private bool _subscribed;
    private bool _refreshing;
    private bool _changingRows;
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(2) };
    public ActivityFeed(ActivityLog activity)
    {
        _activity = activity;
        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.Children.Add(_events);
        var details = new ScrollViewer { Content = _message, MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 8, 0, 0) };
        Grid.SetRow(details, 1); panel.Children.Add(details); Content = panel;
        _events.SelectionChanged += (_, _) =>
        {
            if (_changingRows) return;
            _state.Select((_events.SelectedItem as ListBoxItem)?.Tag as ActivityEntry);
            _message.Text = _state.Selected?.Message ?? "Select an event to read its full message.";
        };
        _refresh.Tick += (_, _) => Refresh(); // Also observes events written by the external helper.
        Loaded += (_, _) => { if (!_subscribed) { _activity.Changed += Changed; _subscribed = true; } _refresh.Start(); Refresh(); };
        Unloaded += (_, _) => { _refresh.Stop(); if (_subscribed) { _activity.Changed -= Changed; _subscribed = false; } };
    }
    private void Changed() { if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(Refresh)); }
    private async void Refresh()
    {
        if (_refreshing || !IsLoaded) return;
        _refreshing = true;
        try
        {
            var rows = await _activity.RecentAsync(50);
            if (!IsLoaded || !_state.Refresh(rows)) return;
            _changingRows = true;
            _events.Items.Clear();
            foreach (var row in rows)
                _events.Items.Add(new ListBoxItem { Content = new TextBlock { Text = $"{row.Timestamp.ToLocalTime():HH:mm:ss}  {row.Severity} / {row.Code}\n{row.Message}", TextWrapping = TextWrapping.Wrap }, Tag = row });
            _events.SelectedItem = _events.Items.Cast<ListBoxItem>().FirstOrDefault(item => Equals(item.Tag, _state.Selected));
            _message.Text = _state.Selected?.Message ?? (rows.Count == 0 ? "No activity has been recorded yet." : "Select an event to read its full message.");
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { _message.Text = "Activity log is unavailable: " + ex.Message; }
        finally { _changingRows = false; _refreshing = false; }
    }
}
