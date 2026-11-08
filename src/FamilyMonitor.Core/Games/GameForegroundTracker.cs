namespace FamilyMonitor.Core.Games;

public sealed record GameForegroundPeriod(Guid Id, string InstallationId, DateTimeOffset StartUtc,
    DateTimeOffset EndUtc, ActivityStatus Status)
{
    public TimeSpan Duration => EndUtc - StartUtc;
}
public interface IGameForegroundStore
{
    void SaveForeground(GameForegroundPeriod period);
    IReadOnlyList<GameForegroundPeriod> ReadForeground(DateTimeOffset fromUtc, DateTimeOffset toUtc);
    IReadOnlyDictionary<string, TimeSpan> GetForegroundDurations(DateTimeOffset fromUtc, DateTimeOffset toUtc);
}

/// <summary>Foreground presence is independent of input confirmation; gaps, lock, pause and sleep are excluded.</summary>
public sealed class GameForegroundTracker(IGameForegroundStore store)
{
    private GameForegroundPeriod? current;
    private DateTimeOffset lastObserved;
    private long window;
    public void Observe(ActivitySample sample, DateTimeOffset now, TimeSpan maximumGap)
    {
        var id = sample.IsSessionLocked ? null : sample.Application?.GameInstallationId;
        if (current is not null && now - lastObserved > maximumGap) Close(lastObserved, maximumGap);
        if (current is not null && (id != current.InstallationId || window != sample.Application?.WindowId)) Close(now, maximumGap);
        if (id is null) return;
        if (current is null)
        {
            current = new(Guid.NewGuid(), id, now, now, ActivityStatus.Active);
            window = sample.Application!.WindowId;
        }
        current = current with { EndUtc = now >= current.EndUtc ? now : current.EndUtc };
        lastObserved = now;
        if (current.Duration > TimeSpan.Zero) store.SaveForeground(current);
    }
    public void Close(DateTimeOffset now, TimeSpan maximumGap)
    {
        if (current is null) return;
        var end = now - lastObserved > maximumGap ? lastObserved : now;
        if (end < current.EndUtc) end = current.EndUtc;
        var completed = current with { EndUtc = end, Status = ActivityStatus.Completed };
        current = null;
        if (completed.Duration > TimeSpan.Zero) store.SaveForeground(completed);
    }
    public void Discard() => current=null;
}
