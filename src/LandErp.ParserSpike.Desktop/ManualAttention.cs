using LandErp.ParserSpike.LocalCollection;

namespace LandErp.ParserSpike.Desktop;

public sealed record ManualAttention(CollectionNotice Notice, DateTimeOffset SinceUtc)
{
    public bool ReminderSent { get; set; }
}

/// <summary>Dispatcher-owned alerts. A duplicate notice must not reset its waiting time.</summary>
public sealed class ManualAttentionState
{
    private readonly Dictionary<string, ManualAttention> pending = new(StringComparer.Ordinal);
    public ManualAttention[] Pending => pending.Values.OrderBy(x => x.SinceUtc).ToArray();
    public bool Add(CollectionNotice notice, DateTimeOffset now)
    {
        if (pending.TryGetValue(notice.JobId, out var previous) && previous.Notice.Reason == notice.Reason) return false;
        pending[notice.JobId] = new(notice, now);
        return true;
    }
    public void RemoveResolved(Func<string, bool> stillWaiting)
    {
        foreach (string id in pending.Keys.Where(id => !stillWaiting(id)).ToArray()) pending.Remove(id);
    }
    public ManualAttention[] Reminders(DateTimeOffset now)
    {
        var due = Pending.Where(x => !x.ReminderSent && now - x.SinceUtc >= TimeSpan.FromMinutes(5)).ToArray();
        foreach (var alert in due) alert.ReminderSent = true;
        return due;
    }
}
