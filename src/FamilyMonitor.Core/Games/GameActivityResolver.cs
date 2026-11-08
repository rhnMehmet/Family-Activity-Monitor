namespace FamilyMonitor.Core.Games;

public sealed class GameActivityResolver(IActivitySource source, GameDiscoveryService discovery, IGameDetector detector) : IActivitySource
{
    public ActivitySample Read()
    {
        var sample = source.Read();
        if (sample.Application is not { } app) return sample;
        var match = discovery.Resolve(app, detector);
        return sample with { Application = app with
        {
            GameInstallationId = match.Installation?.Id,
            GameName = match.Installation?.Name,
            GameResolutionNote = match.Status == GameMatchStatus.Ambiguous ? match.Explanation : null
        } };
    }
}

public sealed class GameUsageService(IGameCatalogRepository repository,IGameForegroundStore? foreground=null)
{
    public IReadOnlyList<GameUsage> GetUsage(DateOnly date, bool week, TimeZoneInfo zone)
    {
        if (week) date = date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
        var from=LocalDayBoundary(date,zone);var to=LocalDayBoundary(date.AddDays(week?7:1),zone);
        var active=repository.GetUsage(from,to);
        if(foreground is null)return active;
        var catalog=repository.GetInstallations().ToDictionary(x=>x.Id);
        var combined=active.ToDictionary(x=>x.GameId);
        foreach(var group in foreground.GetForegroundDurations(from,to).Where(x=>catalog.ContainsKey(x.Key)).GroupBy(x=>catalog[x.Key].GameId))
        {
            var game=catalog[group.First().Key];
            var row=combined.GetValueOrDefault(game.GameId)??new GameUsage(game.GameId,game.Name,game.Platform,TimeSpan.Zero);
            combined[game.GameId]=row with{ForegroundDuration=TimeSpan.FromTicks(group.Sum(x=>x.Value.Ticks))};
        }
        return combined.Values.OrderByDescending(x=>x.Duration).ThenBy(x=>x.Name).ToArray();
    }
    public IReadOnlyList<GameUsageHistory> GetHistory(DateOnly date, bool week, TimeZoneInfo zone)
    {
        if (week) date = date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
        return repository.GetHistory(LocalDayBoundary(date, zone), LocalDayBoundary(date.AddDays(week ? 7 : 1), zone));
    }
    public static DateTimeOffset LocalDayBoundary(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        if (zone.IsAmbiguousTime(local)) return new DateTimeOffset(local, zone.GetAmbiguousTimeOffsets(local).Max()).ToUniversalTime();
        return new(TimeZoneInfo.ConvertTimeToUtc(local, zone));
    }
}
