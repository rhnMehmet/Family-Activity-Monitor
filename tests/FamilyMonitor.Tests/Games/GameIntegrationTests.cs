using FamilyMonitor.Core;
using FamilyMonitor.Core.Games;
using FamilyMonitor.Infrastructure;
using FamilyMonitor.Infrastructure.Games;
using FamilyMonitor.Infrastructure.Windows;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FamilyMonitor.Tests.Games;

public sealed class GameIntegrationTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "FamilyMonitor.GameTests", Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(directory, "test.db");
    private GameInstallation Game(GamePlatform platform = GamePlatform.Steam) => RandomGame.Create(Path.Combine(directory, Guid.NewGuid().ToString("N")), platform);
    private static GameLibraryScan Scan(params GameInstallation[] games) => new("Test", games, []);
    private static ActivityRecord Activity(GameInstallation game, DateTimeOffset start, int seconds) => new(Guid.NewGuid(), "device", "session", game.Name,
        "runtime", start, start.AddSeconds(seconds), TimeSpan.FromSeconds(seconds), ActivityStatus.Completed, game.Id);

    [Fact]
    public void DiscoveryCannotTouchProvidersBeforeSeparateConsent()
    {
        var repo = new SqliteGameCatalogRepository(Database); var provider = new FakeGameProvider("Test", () => Scan(Game()));
        using var service = new GameDiscoveryService([provider], repo, new FakeClock());
        Assert.False(service.Refresh().Ran); Assert.Equal(0, provider.Calls); Assert.Empty(repo.GetInstallations());
        service.SetConsent(true); Assert.True(service.Refresh().Ran); Assert.Single(repo.GetInstallations());
    }

    [Fact]
    public void BrokenProviderDoesNotStopOtherSources()
    {
        var repo = new SqliteGameCatalogRepository(Database); var game = Game();
        using var service = new GameDiscoveryService([new FakeGameProvider("Broken", () => throw new IOException()),
            new FakeGameProvider("Working", () => Scan(game))], repo, new FakeClock());
        service.SetConsent(true); var report = service.Refresh();
        Assert.True(report.Ran); Assert.Single(repo.GetInstallations()); Assert.Contains(report.Messages, x => x.Contains("Broken"));
    }

    [Fact]
    public void PeriodicCheckIsLimitedToFifteenMinutes()
    {
        var clock = new FakeClock(); var repo = new SqliteGameCatalogRepository(Database);
        using var service = new GameDiscoveryService([], repo, clock);
        Assert.False(service.IsDue); service.SetConsent(true); Assert.True(service.IsDue);
        service.Refresh(); Assert.False(service.IsDue); clock.Advance(899); Assert.False(service.IsDue);
        clock.Advance(1); Assert.True(service.IsDue);
    }

    [Fact]
    public void PeriodicDiscoveryUsesMonotonicClockWhenWallClockMovesBackwards()
    {
        var clock = new FakeClock(); var repo = new SqliteGameCatalogRepository(Database);
        using var service = new GameDiscoveryService([], repo, clock); service.SetConsent(true); service.Refresh();
        clock.UtcNow = clock.UtcNow.AddDays(-3); clock.Advance(900); Assert.True(service.IsDue);
    }

    [Fact]
    public void RepeatedScansAreIdempotentAndDiscoverFutureGames()
    {
        var repo = new SqliteGameCatalogRepository(Database); var list = new List<GameInstallation> { Game() };
        using var service = new GameDiscoveryService([new FakeGameProvider("Test", () => Scan(list.ToArray()))], repo, new FakeClock());
        service.SetConsent(true); service.Refresh(); service.Refresh(); Assert.Single(repo.GetInstallations());
        list.Add(Game()); service.Refresh(); Assert.Equal(2, repo.GetInstallations().Count);
    }

    [Fact]
    public void RevocationDuringScanRejectsPendingResultsAndFutureScans()
    {
        var repo = new SqliteGameCatalogRepository(Database); var game = Game(); GameDiscoveryService? service = null;
        var provider = new FakeGameProvider("Test", () => { service!.SetConsent(false); return Scan(game); });
        using (service = new GameDiscoveryService([provider], repo, new FakeClock()))
        {
            service.SetConsent(true); Assert.False(service.Refresh().Ran); Assert.Empty(repo.GetInstallations());
            Assert.False(service.Refresh().Ran); Assert.Equal(1, provider.Calls);
        }
    }

    [Fact]
    public void ForegroundGameIsASubsetOfGeneralUsageAndLauncherIsNotDoubleCounted()
    {
        var clock = new FakeClock(); var repo = new SqliteGameCatalogRepository(Database); var store = new SqliteActivityStore(Database); var game = Game();
        repo.ApplyScan(Scan(game)); using var discovery = new GameDiscoveryService([], repo, clock); discovery.SetConsent(true);
        var source = new FakeSource { Application = new("Raw", "runtime", 1, game.ExecutablePaths[0]) };
        var tracker = new ActivityTracker(clock, new GameActivityResolver(source, discovery, new GameDetector()), store, "device", "session");
        tracker.GrantConsent(); tracker.Start();
        void Use(int count) { for (var i = 0; i < count; i++) { clock.Advance(1); tracker.Tick(); } }
        Use(3); Assert.Equal(game.Name, tracker.CurrentApplication);
        source.Application = new("Steam", "steam", 2, Path.Combine(directory, "client", "steam.exe")); Use(3);
        source.Application = new("Raw", "runtime", 1, game.ExecutablePaths[0]); Use(4);
        tracker.Stop();
        var total = store.Read(DateTimeOffset.MinValue, DateTimeOffset.MaxValue).Sum(x => x.ActiveDuration.TotalSeconds);
        var gameTotal = Assert.Single(repo.GetUsage(DateTimeOffset.MinValue, DateTimeOffset.MaxValue)).Duration.TotalSeconds;
        Assert.Equal(10, total); Assert.Equal(7, gameTotal); Assert.True(gameTotal <= total);
        Assert.Equal(2, repo.GetHistory(DateTimeOffset.MinValue, DateTimeOffset.MaxValue).Count);
        Assert.Equal(game.GameId, repo.GetUsage(DateTimeOffset.MinValue, DateTimeOffset.MaxValue)[0].GameId);
    }

    [Fact]
    public void BackgroundGameDoesNotGrowAndClosingForegroundCompletesSession()
    {
        var clock = new FakeClock(); var repo = new SqliteGameCatalogRepository(Database); var store = new SqliteActivityStore(Database); var game = Game();
        repo.ApplyScan(Scan(game)); using var discovery = new GameDiscoveryService([], repo, clock); discovery.SetConsent(true);
        var source = new FakeSource { Application = new("Raw", "runtime", 1, game.ExecutablePaths[0]) };
        var tracker = new ActivityTracker(clock, new GameActivityResolver(source, discovery, new GameDetector()), store, "device", "session");
        tracker.GrantConsent(); tracker.Start(); clock.Advance(1); tracker.Tick(); clock.Advance(1); tracker.Tick();
        source.Application = null; clock.Advance(1); tracker.Tick();
        var closed = Assert.Single(store.Read(DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
        Assert.Equal(ActivityStatus.Completed, closed.Status); Assert.Equal(2, closed.ActiveDuration.TotalSeconds);
        source.Application = new("Editor", "editor", 9, Path.Combine(directory, "editor.exe"));
        for (var i = 0; i < 10; i++) { clock.Advance(1); tracker.Tick(); }
        Assert.Equal(2, Assert.Single(repo.GetUsage(DateTimeOffset.MinValue, DateTimeOffset.MaxValue)).Duration.TotalSeconds);
    }

    [Fact]
    public void RemovedGameRetainsUsageAndLastPlayedAfterRestart()
    {
        var repo = new SqliteGameCatalogRepository(Database); var store = new SqliteActivityStore(Database); var game = Game(); var start = new FakeClock().UtcNow;
        repo.ApplyScan(Scan(game)); store.Save(Activity(game, start, 25)); repo.ApplyScan(Scan(game with { State = GameInstallationState.NotInstalled }));
        var reopened = new SqliteGameCatalogRepository(Database);
        Assert.Equal(GameInstallationState.NotInstalled, Assert.Single(reopened.GetInstallations()).State);
        Assert.Equal(start.AddSeconds(25), Assert.Single(reopened.GetInstallations()).LastPlayedUtc);
        Assert.Equal(25, Assert.Single(reopened.GetUsage(start.AddDays(-1), start.AddDays(1))).Duration.TotalSeconds);
    }

    [Fact]
    public void SameGameAcrossInstallationsAndMetadataNamesStillHasOneUsageTotal()
    {
        var repo = new SqliteGameCatalogRepository(Database); var store = new SqliteActivityStore(Database);
        var first = Game(); var second = Game() with { GameId = first.GameId };
        repo.ApplyScan(Scan(first, second)); var start = new FakeClock().UtcNow;
        store.Save(Activity(first, start, 20)); store.Save(Activity(second, start.AddMinutes(1), 30));
        var total = Assert.Single(repo.GetUsage(start.AddDays(-1), start.AddDays(1)));
        Assert.Equal(first.GameId, total.GameId); Assert.Equal(50, total.Duration.TotalSeconds);
    }

    [Fact]
    public void SameGameNameOnDifferentPlatformsHasSeparateCatalogIdentities()
    {
        var repo = new SqliteGameCatalogRepository(Database); var first = Game(); var second = Game(GamePlatform.Epic) with { Name = first.Name };
        repo.ApplyScan(Scan(first, second)); Assert.Equal(2, repo.GetInstallations().Count);
        Assert.Equal(2, repo.GetInstallations().Select(x => x.GameId).Distinct().Count());
    }

    [Fact]
    public void ManualNameAndMappingSurviveRescanAndRepositoryRestart()
    {
        var repo = new SqliteGameCatalogRepository(Database); var original = Game() with { IsConfirmed = false }; repo.ApplyScan(Scan(original));
        var renamed = original with { Name = RandomGame.Name(), IsConfirmed = true }; repo.SaveManualGame(renamed, original.ExecutablePaths[0]);
        repo.ApplyScan(Scan(original)); var reopened = new SqliteGameCatalogRepository(Database);
        Assert.Equal(renamed.Name, Assert.Single(reopened.GetInstallations()).Name);
        Assert.True(Assert.Single(reopened.GetInstallations()).IsConfirmed); Assert.Single(reopened.GetMappings());
    }

    [Fact]
    public void GenericRuntimeCannotBePersistentlyMappedAsAGame()
    {
        var repo = new SqliteGameCatalogRepository(Database); using var service = new GameDiscoveryService([], repo, new FakeClock()); service.SetConsent(true);
        Assert.Throws<ArgumentException>(() => service.AddManualGame(Game(), Path.Combine(directory, "javaw.exe")));
        Assert.Empty(repo.GetInstallations());
    }

    [Fact]
    public void DailyAndWeeklyGameUsageSplitMidnightAndCombineSessions()
    {
        var repo = new SqliteGameCatalogRepository(Database); var store = new SqliteActivityStore(Database); var game = Game(); repo.ApplyScan(Scan(game));
        var start = new DateTimeOffset(2026, 11, 9, 23, 59, 30, TimeSpan.Zero); store.Save(Activity(game, start, 120));
        store.Save(Activity(game, start.AddHours(2), 10)); var usage = new GameUsageService(repo);
        Assert.Equal(30, Assert.Single(usage.GetUsage(new(2026, 11, 9), false, TimeZoneInfo.Utc)).Duration.TotalSeconds);
        Assert.Equal(100, Assert.Single(usage.GetUsage(new(2026, 11, 10), false, TimeZoneInfo.Utc)).Duration.TotalSeconds);
        Assert.Equal(130, Assert.Single(usage.GetUsage(new(2026, 11, 10), true, TimeZoneInfo.Utc)).Duration.TotalSeconds);
    }

    [Fact]
    public void HistoryDeletionAlsoClearsGameTotalsAndLastPlayedButPreservesCatalogAndMappings()
    {
        var repo = new SqliteGameCatalogRepository(Database); var store = new SqliteActivityStore(Database); var game = Game();
        repo.SaveManualGame(game, game.ExecutablePaths[0]); store.Save(Activity(game, new FakeClock().UtcNow, 30)); store.DeleteAll();
        Assert.Empty(repo.GetUsage(DateTimeOffset.MinValue, DateTimeOffset.MaxValue)); Assert.Single(repo.GetMappings());
        Assert.Null(Assert.Single(repo.GetInstallations()).LastPlayedUtc);
    }

    [Fact]
    public void VersionOneMigrationPreservesAllOriginalDataAndAddsOptionalGameClassification()
    {
        Directory.CreateDirectory(directory); var id = Guid.NewGuid(); var start = new FakeClock().UtcNow;
        using (var connection = new SqliteConnection($"Data Source={Database};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE activities (id TEXT PRIMARY KEY,device_id TEXT NOT NULL,local_session_id TEXT NOT NULL,
                    application_name TEXT NOT NULL,process_name TEXT NOT NULL,start_utc_ticks INTEGER NOT NULL,
                    end_utc_ticks INTEGER NOT NULL,duration_ticks INTEGER NOT NULL,status TEXT NOT NULL);
                INSERT INTO activities VALUES ($id,'device','session','Existing','existing',$start,$end,$duration,'Completed');
                PRAGMA user_version=1;
                """;
            command.Parameters.AddWithValue("$id", id.ToString()); command.Parameters.AddWithValue("$start", start.UtcTicks);
            command.Parameters.AddWithValue("$end", start.AddSeconds(60).UtcTicks); command.Parameters.AddWithValue("$duration", TimeSpan.FromSeconds(60).Ticks); command.ExecuteNonQuery();
        }
        var store = new SqliteActivityStore(Database); var record = Assert.Single(store.Read(DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
        Assert.Equal(id, record.Id); Assert.Equal("Existing", record.ApplicationName); Assert.Equal(60, record.ActiveDuration.TotalSeconds); Assert.Null(record.GameInstallationId);
        var repo = new SqliteGameCatalogRepository(Database); repo.ApplyScan(Scan(Game()));
        var secondOpen = new SqliteActivityStore(Database); Assert.Single(secondOpen.Read(DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
        Assert.Single(new SqliteGameCatalogRepository(Database).GetInstallations());
        using var check = new SqliteConnection($"Data Source={Database};Pooling=False"); check.Open(); using var version = check.CreateCommand();
        version.CommandText = "PRAGMA user_version;"; Assert.Equal(2L, version.ExecuteScalar());
        version.CommandText = "SELECT COUNT(*) FROM pragma_table_info('activities');"; Assert.Equal(9L, version.ExecuteScalar());
        // Legacy 1.1 writes used an unqualified nine-value INSERT; keep that contract compatible.
        version.CommandText = "INSERT INTO activities VALUES ($id,'d','s','Legacy','legacy',$start,$end,$duration,'Completed');";
        version.Parameters.AddWithValue("$id", Guid.NewGuid().ToString()); version.Parameters.AddWithValue("$start", start.AddHours(1).UtcTicks);
        version.Parameters.AddWithValue("$end", start.AddHours(1).AddSeconds(1).UtcTicks); version.Parameters.AddWithValue("$duration", TimeSpan.FromSeconds(1).Ticks);
        version.ExecuteNonQuery(); Assert.Equal(2, secondOpen.Read(DateTimeOffset.MinValue, DateTimeOffset.MaxValue).Count);
    }

    [Fact]
    public void InvalidGameLinkRollsBackEntireActivityWrite()
    {
        var store = new SqliteActivityStore(Database);
        Assert.Throws<SqliteException>(() => store.Save(Activity(Game(), new FakeClock().UtcNow, 10)));
        Assert.Empty(store.Read(DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
    }

    [Fact]
    public void OldSettingsKeepGeneralConsentButDefaultGameDiscoveryToOff()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "settings.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            PrivacyVersion = 1, ConsentGranted = true, IdleThresholdSeconds = 5,
            DeviceId = Guid.NewGuid().ToString(), LocalUserId = Guid.NewGuid().ToString()
        }));
        var settings = new LocalSettingsStore(directory).Load();
        Assert.True(settings.ConsentGranted); Assert.False(settings.GameDiscoveryConsentGranted); Assert.Equal(5, settings.IdleThresholdSeconds);
    }

    [Fact]
    public void GameConsentNeedsItsOwnVersionAndDoesNotGrantTrackingConsent()
    {
        var store = new LocalSettingsStore(directory);
        var settings = new LocalSettings { PrivacyVersion = 1, GameDiscoveryConsentGranted = true };
        store.Save(settings); Assert.False(store.Load().GameDiscoveryConsentGranted);
        store.Save(settings with { GameDiscoveryPrivacyVersion = LocalSettingsStore.CurrentGameDiscoveryPrivacyVersion });
        Assert.True(store.Load().GameDiscoveryConsentGranted); Assert.False(store.Load().ConsentGranted);
    }

    [Fact]
    public void FutureDatabaseVersionIsRejectedInsteadOfDowngraded()
    {
        Directory.CreateDirectory(directory); using var connection = new SqliteConnection($"Data Source={Database};Pooling=False"); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "PRAGMA user_version=99;"; command.ExecuteNonQuery();
        Assert.Throws<InvalidOperationException>(() => new SqliteActivityStore(Database));
        command.CommandText = "PRAGMA user_version;"; Assert.Equal(99L, command.ExecuteScalar());
    }

    private sealed class FakeApi : IWindowsActivityApi
    {
        public int PathReads { get; private set; }
        public TimeSpan GetIdleTime() => TimeSpan.Zero;
        public bool IsInputDesktopAvailable() => true;
        public nint GetForegroundWindow() => 1;
        public uint GetProcessId(nint window) => 2;
        public string? GetProcessName(uint id) => "generated";
        public string? GetExecutablePath(uint id) { PathReads++; return @"C:\Synthetic\random.exe"; }
        public string? GetPackageFamilyName(uint id) { PathReads++; return "synthetic_publisher"; }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProcessPathAndPackageAreReadOnlyWithSeparateConsent(bool consent)
    {
        var api = new FakeApi(); var sample = new WindowActivitySource(api, () => consent).Read();
        Assert.Equal(consent ? 2 : 0, api.PathReads);
        Assert.Equal(consent, sample.Application?.ExecutablePath is not null);
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
}
