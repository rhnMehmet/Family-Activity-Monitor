using System.Text.Json;
using FamilyMonitor.Core.Games;

namespace FamilyMonitor.Infrastructure.Games;

public sealed class EpicLibraryProvider(IGameFileSystem files, Func<IEnumerable<string>> manifestRoots) : IGameLibraryProvider
{
    public string Id => "Epic";
    public GameLibraryScan Discover(IReadOnlyList<GameInstallation> previous, CancellationToken cancellationToken)
    {
        var found = new List<GameInstallation>(); var warnings = new List<string>();
        foreach (var root in manifestRoots().Select(GameIdentity.NormalizePath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (files.DirectoryState(root) != PathAvailability.Present) { warnings.Add("Epic kurulum metaverisi erişilemiyor."); continue; }
            var complete = true;
            try
            {
                foreach (var path in files.Files(root, "*.item"))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        using var document = JsonDocument.Parse(files.ReadText(path)); var item = document.RootElement;
                        if (item.TryGetProperty("bIsApplication", out var application) && application.ValueKind == JsonValueKind.False) continue;
                        if (item.TryGetProperty("AppCategories", out var categories) && categories.ValueKind == JsonValueKind.Array &&
                            !categories.EnumerateArray().Any(x => x.GetString()?.Equals("games", StringComparison.OrdinalIgnoreCase) == true)) continue;
                        string Field(string name) => item.GetProperty(name).GetString() ?? throw new FormatException("Missing Epic field.");
                        var directory = GameIdentity.NormalizePath(Field("InstallLocation"));
                        var executable = GameIdentity.Child(directory, Field("LaunchExecutable"));
                        var state = files.DirectoryState(directory) switch
                        { PathAvailability.Present => GameInstallationState.Installed, PathAvailability.Missing => GameInstallationState.NotInstalled, _ => GameInstallationState.Unknown };
                        if (item.TryGetProperty("bIsIncompleteInstall", out var incomplete) && incomplete.ValueKind == JsonValueKind.True) state = GameInstallationState.Unknown;
                        var executables = state == GameInstallationState.Installed ? DiscoveryFiles.Executables(files, directory, cancellationToken).Append(executable).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : [executable];
                        found.Add(GameIdentity.Create(Field("AppName"), Field("DisplayName"), GamePlatform.Epic, Id, root, directory, executables, state));
                    }
                    catch (Exception error) when (DiscoveryFiles.IsReadError(error) || error is KeyNotFoundException or InvalidOperationException)
                    { complete = false; warnings.Add("Epic manifesti geçersiz veya okunamıyor; eski kayıt korundu."); }
                }
                if (complete) DiscoveryFiles.ReconcileMissing(found, previous, root, files);
            }
            catch (Exception error) when (DiscoveryFiles.IsReadError(error)) { warnings.Add("Epic kaynağı kısmen okunabildi."); }
        }
        return new(Id, found, warnings);
    }
}
