using FamilyMonitor.Core.Games;

namespace FamilyMonitor.Infrastructure.Games;

/// <summary>Uninstall metadata supplies bounded installation roots; only game-specific metadata confirms games.</summary>
public sealed class StandaloneLibraryProvider(IGameFileSystem files, Func<IEnumerable<string>> installationRoots) : IGameLibraryProvider
{
    public string Id => "Standalone";
    public GameLibraryScan Discover(IReadOnlyList<GameInstallation> previous, CancellationToken cancellationToken) =>
        new FolderLibraryProvider(files, installationRoots, Id, GamePlatform.Standalone, includeCandidates: false).Discover(previous, cancellationToken);
}
