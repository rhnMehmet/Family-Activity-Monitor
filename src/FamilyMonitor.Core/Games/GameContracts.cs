namespace FamilyMonitor.Core.Games;

public interface IGameLibraryProvider
{
    string Id { get; }
    GameLibraryScan Discover(IReadOnlyList<GameInstallation> previous, CancellationToken cancellationToken);
}

public interface IGameCatalogRepository
{
    IReadOnlyList<GameInstallation> GetInstallations();
    IReadOnlyList<ManualGameMapping> GetMappings();
    void ApplyScan(GameLibraryScan scan);
    void SaveManualGame(GameInstallation installation, string executablePath);
    IReadOnlyList<GameUsage> GetUsage(DateTimeOffset fromUtc, DateTimeOffset toUtc);
    IReadOnlyList<GameUsageHistory> GetHistory(DateTimeOffset fromUtc, DateTimeOffset toUtc);
}

public interface IGameDetector
{
    GameMatch Detect(ApplicationIdentity application, IReadOnlyList<GameInstallation> catalog,
        IReadOnlyList<ManualGameMapping> mappings);
}

public interface IGameMetadataResolver
{
    bool IsSharedOrHelperExecutable(string path);
}
