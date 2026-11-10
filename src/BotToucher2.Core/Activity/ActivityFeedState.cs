namespace BotToucher2.Core.Activity;

/// <summary>Retains a diagnostic selection while the bounded activity stream refreshes.</summary>
public sealed class ActivityFeedState
{
    public IReadOnlyList<ActivityEntry> Rows { get; private set; } = Array.Empty<ActivityEntry>();
    public ActivityEntry? Selected { get; private set; }
    public void Select(ActivityEntry? entry) => Selected = entry;
    public bool Refresh(IReadOnlyList<ActivityEntry> rows)
    {
        if (Rows.SequenceEqual(rows)) return false;
        Rows = rows;
        if (Selected is not null) Selected = rows.FirstOrDefault(row => row == Selected);
        return true;
    }
}
