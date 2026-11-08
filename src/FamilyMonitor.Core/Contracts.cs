namespace FamilyMonitor.Core;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
    TimeSpan Elapsed { get; }
}

public interface IActivitySource
{
    ActivitySample Read();
}

public interface IActivityStore
{
    void Save(ActivityRecord record);
    IReadOnlyList<ActivityRecord> Read(DateTimeOffset fromUtc, DateTimeOffset toUtc);
    IReadOnlyList<ApplicationUsage> GetDailyUsage(DateOnly day, TimeZoneInfo timeZone);
    DateTimeOffset? GetLatestEnd();
    void DeleteAll();
}
