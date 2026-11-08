using FamilyMonitor.Core;
using FamilyMonitor.Infrastructure;
using Xunit;

namespace FamilyMonitor.Tests;

public sealed class SqliteActivityStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "FamilyMonitor.Tests", Guid.NewGuid().ToString("N"));
    private string DatabasePath => Path.Combine(directory, "activity.db");
    private static ActivityRecord MakeRecord(DateTimeOffset start, int seconds = 60) => new(Guid.NewGuid(), "device", "session",
        "Google Chrome", "chrome", start, start.AddSeconds(seconds), TimeSpan.FromSeconds(seconds), ActivityStatus.Completed);

    [Fact]
    public void InsertReadAndUpsertPreserveIdentityAndUtc()
    {
        var store = new SqliteActivityStore(DatabasePath);
        var record = MakeRecord(new(2026, 11, 7, 12, 0, 0, TimeSpan.FromHours(3)));
        store.Save(record);
        var updated = record with { EndUtc = record.EndUtc.AddSeconds(30), ActiveDuration = TimeSpan.FromSeconds(90) };
        store.Save(updated);
        var read = Assert.Single(store.Read(record.StartUtc.AddDays(-1), record.EndUtc.AddDays(1)));
        Assert.Equal(updated, read);
        Assert.Equal(TimeSpan.Zero, read.StartUtc.Offset);
    }

    [Fact]
    public void ReopeningDatabaseRetainsRecordsAndMarksInterruptedSessions()
    {
        var store = new SqliteActivityStore(DatabasePath);
        var record = MakeRecord(DateTimeOffset.UtcNow) with { Status = ActivityStatus.Active };
        store.Save(record);
        var reopened = new SqliteActivityStore(DatabasePath);
        var read = Assert.Single(reopened.Read(record.StartUtc.AddDays(-1), record.EndUtc.AddDays(1)));
        Assert.Equal(record.Id, read.Id);
        Assert.Equal(ActivityStatus.Interrupted, read.Status);
        Assert.Equal(record.EndUtc, reopened.GetLatestEnd());
    }

    [Fact]
    public void DailyUsageSplitsMidnightInUserTimeZone()
    {
        var store = new SqliteActivityStore(DatabasePath);
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test+3", TimeSpan.FromHours(3), "Test+3", "Test+3");
        store.Save(MakeRecord(new(2026, 11, 7, 20, 59, 30, TimeSpan.Zero), 120));
        Assert.Equal(30, Assert.Single(store.GetDailyUsage(new(2026, 11, 7), zone)).Duration.TotalSeconds);
        Assert.Equal(90, Assert.Single(store.GetDailyUsage(new(2026, 11, 8), zone)).Duration.TotalSeconds);
    }

    [Fact]
    public void DailyUsageHandlesDaylightSavingDayLength()
    {
        var store = new SqliteActivityStore(DatabasePath);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        store.Save(MakeRecord(new(2026, 3, 8, 5, 0, 0, TimeSpan.Zero), 24 * 3600));
        Assert.Equal(23, Assert.Single(store.GetDailyUsage(new(2026, 3, 8), zone)).Duration.TotalHours);
        Assert.Equal(1, Assert.Single(store.GetDailyUsage(new(2026, 3, 9), zone)).Duration.TotalHours);
    }

    [Fact]
    public void DeleteRemainsEmptyAfterReopening()
    {
        var store = new SqliteActivityStore(DatabasePath);
        store.Save(MakeRecord(DateTimeOffset.UtcNow));
        store.DeleteAll();
        var reopened = new SqliteActivityStore(DatabasePath);
        Assert.Empty(reopened.Read(DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
        Assert.Null(reopened.GetLatestEnd());
    }

    [Fact]
    public void InvalidDurationIsRejected()
    {
        var store = new SqliteActivityStore(DatabasePath);
        var record = MakeRecord(DateTimeOffset.UtcNow) with { ActiveDuration = TimeSpan.FromSeconds(-1) };
        Assert.Throws<ArgumentException>(() => store.Save(record));
        Assert.Empty(store.Read(DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
    }

    [Fact]
    public void TrackerRestartUsesSavedBoundaryAfterWallClockRollback()
    {
        var clock = new FakeClock();
        var source = new FakeSource();
        var store = new SqliteActivityStore(DatabasePath);
        var previous = MakeRecord(clock.UtcNow, 60);
        store.Save(previous);
        var tracker = new ActivityTracker(clock, source, store, "device", "session");
        tracker.GrantConsent();
        tracker.Start();
        clock.Advance(1);
        tracker.Tick();
        var rows = store.Read(DateTimeOffset.MinValue, DateTimeOffset.MaxValue);
        Assert.Equal(2, rows.Count);
        Assert.True(rows[0].EndUtc <= rows[1].StartUtc);
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
}
