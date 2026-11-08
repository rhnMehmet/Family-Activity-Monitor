using FamilyMonitor.Core.Games;

namespace FamilyMonitor.Infrastructure.Games;

public sealed class XboxLibraryProvider(IWindowsPackageInventory inventory) : IGameLibraryProvider
{
    public string Id => "Xbox";
    public GameLibraryScan Discover(IReadOnlyList<GameInstallation> previous, CancellationToken cancellationToken)
    {
        var packages = inventory.Read(cancellationToken);
        var found = new List<GameInstallation>(); var warnings = new List<string>();
        foreach (var package in packages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (MinecraftLibraryProvider.IsBedrock(package)) continue;
            try
            {
                GameConfiguration? config = package.GameConfigXml is { Length: > 0 } xml
                    ? GameConfiguration.Read(xml, package.ConfigDirectory ?? package.InstallDirectory) : null;
                if (config is null && !package.HasGameExtension)
                {
                    foreach (var old in previous.Where(g => g.PackageFamilyName == package.FamilyName)) found.Add(old with { State = GameInstallationState.Unknown });
                    continue; // A Store app is not automatically a game.
                }
                var root = package.ConfigDirectory ?? package.InstallDirectory;
                var executables = config?.Executables ?? (string.IsNullOrWhiteSpace(root) ? [] : package.Executables.Select(p => GameIdentity.Child(root, p)).ToArray());
                found.Add(GameIdentity.Create(package.FamilyName, config?.Name ?? package.DisplayName, GamePlatform.Xbox,
                    Id, "current-user-packages", root, executables, GameInstallationState.Installed, packageFamily: package.FamilyName));
            }
            catch (Exception error) when (DiscoveryFiles.IsReadError(error))
            { warnings.Add("Bir Windows oyun paketinin metaverisi okunamadı; izinler aşılmadı."); }
        }
        // Only disappearance from a successful current-user package query confirms unregistration.
        foreach (var old in previous.Where(g => found.All(n => n.Id != g.Id)))
            found.Add(old with { State = packages.Any(p => p.FamilyName == old.PackageFamilyName) ? GameInstallationState.Unknown : GameInstallationState.NotInstalled });
        return new(Id, found, warnings);
    }
}
