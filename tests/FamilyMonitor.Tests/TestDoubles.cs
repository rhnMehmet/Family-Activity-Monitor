using FamilyMonitor.Core;

namespace FamilyMonitor.Tests;

internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);
    public TimeSpan Elapsed { get; set; }
    public void Advance(double seconds) { Elapsed += TimeSpan.FromSeconds(seconds); UtcNow += TimeSpan.FromSeconds(seconds); }
}

internal sealed class FakeSource : IActivitySource
{
    public ApplicationIdentity? Application { get; set; } = new("Editor", "editor", 1);
    public TimeSpan IdleTime { get; set; }
    public bool Locked { get; set; }
    public bool Throw { get; set; }
    public int Reads { get; private set; }
    public ActivitySample Read()
    {
        Reads++;
        if (Throw) throw new System.ComponentModel.Win32Exception(5);
        return new(Application, IdleTime, Locked);
    }
}

internal sealed class MemoryStore : IActivityStore
{
    public Dictionary<Guid, ActivityRecord> Records { get; } = [];
    public bool ThrowOnSave { get; set; }
    public int Writes { get; private set; }
    public void Save(ActivityRecord record)
    {
        Writes++;
        if (ThrowOnSave) throw new IOException("Test storage failure");
        Records[record.Id] = record;
    }
    public IReadOnlyList<ActivityRecord> Read(DateTimeOffset fromUtc, DateTimeOffset toUtc) =>
        Records.Values.Where(x => x.EndUtc > fromUtc && x.StartUtc < toUtc).OrderBy(x => x.StartUtc).ToList();
    public IReadOnlyList<ApplicationUsage> GetDailyUsage(DateOnly day, TimeZoneInfo timeZone) => throw new NotSupportedException();
    public DateTimeOffset? GetLatestEnd() => Records.Count == 0 ? null : Records.Values.Max(x => x.EndUtc);
    public void DeleteAll() => Records.Clear();
    public double TotalSeconds => Records.Values.Sum(x => x.ActiveDuration.TotalSeconds);
}

internal sealed class TrackerFixture
{
    public FakeClock Clock { get; } = new();
    public FakeSource Source { get; } = new();
    public MemoryStore Store { get; } = new();
    public ActivityTracker Tracker { get; }
    public TrackerFixture(bool start = true)
    {
        Tracker = new(Clock, Source, Store, "device", "local-session");
        if (start) { Tracker.GrantConsent(); Tracker.Start(); }
    }
    public void Use(int seconds)
    {
        for (var i = 0; i < seconds; i++) { Source.IdleTime = TimeSpan.Zero; Clock.Advance(1); Tracker.Tick(); }
    }
    public void Idle(int seconds)
    {
        for (var i = 0; i < seconds; i++) { Source.IdleTime += TimeSpan.FromSeconds(1); Clock.Advance(1); Tracker.Tick(); }
    }
}
