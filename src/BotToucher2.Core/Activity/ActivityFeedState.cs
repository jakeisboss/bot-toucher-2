namespace BotToucher2.Core.Activity;

/// <summary>Retains a diagnostic selection while the bounded activity stream refreshes.</summary>
public sealed class ActivityFeedState
{
    public IReadOnlyList<ActivityEntry> Rows { get; private set; } = Array.Empty<ActivityEntry>();
    public ActivityEntry? Selected { get; private set; }
    private string? _readError;
    public string Details => _readError ?? Selected?.Message ?? (Rows.Count == 0 ? "No activity has been recorded yet." : "Select an event to read its full message.");
    public void ReportUnavailable(string message) => _readError = "Activity log is unavailable: " + message;
    public void Select(ActivityEntry? entry) { Selected = entry; _readError = null; }
    public bool Refresh(IReadOnlyList<ActivityEntry> rows)
    {
        _readError = null;
        if (Rows.SequenceEqual(rows)) return false;
        Rows = rows;
        if (Selected is not null) Selected = rows.FirstOrDefault(row => row == Selected);
        return true;
    }
}
