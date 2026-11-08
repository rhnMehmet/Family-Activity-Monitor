using FamilyMonitor.Core.Games;

namespace FamilyMonitor.Infrastructure.Games;

public sealed class FolderLibraryProvider(IGameFileSystem files, Func<IEnumerable<string>> roots,
    string providerId = "CustomFolders", GamePlatform platform = GamePlatform.Custom, bool includeCandidates = true) : IGameLibraryProvider
{
    public string Id => providerId;
    public GameLibraryScan Discover(IReadOnlyList<GameInstallation> previous, CancellationToken cancellationToken)
    {
        var found = new List<GameInstallation>(); var warnings = new List<string>();
        foreach (var root in roots().Select(GameIdentity.NormalizePath).Distinct(StringComparer.OrdinalIgnoreCase).Take(includeCandidates ? 64 : 2000))
        {
            if (GameIdentity.SamePath(root, Path.GetPathRoot(root)!) || files.DirectoryState(root) != PathAvailability.Present)
            { warnings.Add($"{Id}: disk kökü veya erişilemeyen klasör taranmadı."); continue; }
            var complete = true;
            try
            {
                // User-selected roots and one level of game folders, never entire disks.
                foreach (var directory in new[] { root }.Concat(includeCandidates ? files.Directories(root).Take(200) : []))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var configPath = Path.Combine(directory, "MicrosoftGame.config");
                        if (files.FileExists(configPath))
                        {
                            var config = GameConfiguration.Read(files.ReadText(configPath), directory);
                            found.Add(GameIdentity.Create(config.ExternalId, config.Name, platform, Id, root, directory,
                                config.Executables, GameInstallationState.Installed));
                            continue;
                        }
                        var sourcePath = Path.Combine(directory, "gameinfo.txt");
                        if (files.FileExists(sourcePath))
                        {
                            var info = ValveKeyValues.Parse(files.ReadText(sourcePath)).Child("GameInfo");
                            if (info?.Get("game") is { Length: > 0 } name)
                                found.Add(GameIdentity.Create(GameIdentity.Stable(directory), name, platform, Id, root, directory,
                                    DiscoveryFiles.Executables(files, directory, cancellationToken), GameInstallationState.Installed));
                            continue;
                        }
                        if (!includeCandidates || files.Files(directory, "*.exe").Length == 0) continue;
                        found.Add(GameIdentity.Create(GameIdentity.Stable(directory), Path.GetFileName(directory), platform, Id, root,
                            directory, files.Files(directory, "*.exe"), GameInstallationState.Installed, confirmed: false));
                    }
                    catch (Exception error) when (DiscoveryFiles.IsReadError(error)) { complete = false; warnings.Add($"{Id}: bir klasör okunamadı; eksik kayıtlar kaldırıldı sayılmadı."); }
                }
                if (complete) DiscoveryFiles.ReconcileMissing(found, previous, root, files);
            }
            catch (Exception error) when (DiscoveryFiles.IsReadError(error)) { warnings.Add($"{Id}: kaynak erişilemiyor."); }
        }
        return new(Id, found, warnings);
    }
}
