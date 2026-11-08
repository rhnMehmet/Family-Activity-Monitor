using FamilyMonitor.Core.Games;

namespace FamilyMonitor.Infrastructure.Games;

public sealed class SteamLibraryProvider(IGameFileSystem files, Func<IEnumerable<string>> steamRoots) : IGameLibraryProvider
{
    public string Id => "Steam";
    public GameLibraryScan Discover(IReadOnlyList<GameInstallation> previous, CancellationToken cancellationToken)
    {
        var found = new List<GameInstallation>(); var warnings = new List<string>();
        var roots = new HashSet<string>(steamRoots().Select(GameIdentity.NormalizePath), StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.ToArray())
            foreach (var relative in new[] { "steamapps/libraryfolders.vdf", "config/libraryfolders.vdf" })
            {
                var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                try
                {
                    if (!files.FileExists(path)) continue;
                    var libraries = ValveKeyValues.Parse(files.ReadText(path)).Child("libraryfolders");
                    if (libraries is null) continue;
                    foreach (var node in libraries.Children.Where(x => int.TryParse(x.Key, out _)))
                    {
                        var library = node.Get("path") ?? node.Value;
                        if (!string.IsNullOrWhiteSpace(library)) roots.Add(GameIdentity.NormalizePath(library));
                    }
                }
                catch (Exception error) when (DiscoveryFiles.IsReadError(error)) { warnings.Add("Steam kütüphane listesi kısmen okunamadı; eski kayıtlar korundu."); }
            }
        if (roots.Count > 64) throw new IOException("Steam library budget exceeded.");
        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var apps = Path.Combine(root, "steamapps");
            if (files.DirectoryState(apps) != PathAvailability.Present)
            { warnings.Add("Steam kütüphanesi erişilemiyor; kurulumlar kaldırıldı sayılmadı."); continue; }
            var complete = true;
            try
            {
                foreach (var path in files.Files(apps, "appmanifest_*.acf"))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var state = ValveKeyValues.Parse(files.ReadText(path)).Child("AppState") ?? throw new FormatException("Missing AppState.");
                        var id = state.Get("appid") ?? throw new FormatException("Missing id.");
                        var name = state.Get("name") ?? throw new FormatException("Missing name.");
                        var directory = GameIdentity.Child(Path.Combine(apps, "common"), state.Get("installdir") ?? throw new FormatException("Missing directory."));
                        var availability = files.DirectoryState(directory);
                        var installed = availability == PathAvailability.Present && uint.TryParse(state.Get("StateFlags"), out var flags) && (flags & 4) != 0;
                        var status = installed ? GameInstallationState.Installed : availability == PathAvailability.Missing ? GameInstallationState.NotInstalled : GameInstallationState.Unknown;
                        var executables = installed ? DiscoveryFiles.Executables(files, directory, cancellationToken) : [];
                        found.Add(GameIdentity.Create(id, name, GamePlatform.Steam, Id, root, directory, executables, status));
                    }
                    catch (Exception error) when (DiscoveryFiles.IsReadError(error)) { complete = false; warnings.Add("Steam oyun metaverisi okunamadı; bu kütüphanede eksik kayıtlar silinmedi."); }
                }
                if (complete) DiscoveryFiles.ReconcileMissing(found, previous, root, files);
            }
            catch (Exception error) when (DiscoveryFiles.IsReadError(error)) { warnings.Add("Steam kaynağı kısmen okunabildi."); }
        }
        return new(Id, found, warnings);
    }
}
