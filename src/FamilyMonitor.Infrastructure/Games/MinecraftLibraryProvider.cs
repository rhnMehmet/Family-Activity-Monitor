using System.Text.Json;
using FamilyMonitor.Core.Games;

namespace FamilyMonitor.Infrastructure.Games;

/// <summary>Minecraft is an explicit supported edition provider, not a table of arbitrary game process names.</summary>
public sealed class MinecraftLibraryProvider(IGameFileSystem files, Func<IEnumerable<string>> javaRoots, IWindowsPackageInventory inventory) : IGameLibraryProvider
{
    public string Id => "Minecraft";
    public static bool IsBedrock(InstalledPackage package) => package.Name.StartsWith("Microsoft.MinecraftUWP", StringComparison.OrdinalIgnoreCase) ||
        package.Name.StartsWith("Microsoft.MinecraftWindowsBeta", StringComparison.OrdinalIgnoreCase);

    public GameLibraryScan Discover(IReadOnlyList<GameInstallation> previous, CancellationToken cancellationToken)
    {
        var found = new List<GameInstallation>(); var warnings = new List<string>();
        foreach (var root in javaRoots().Select(GameIdentity.NormalizePath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var versions = Path.Combine(root, "versions");
            if (files.DirectoryState(versions) != PathAvailability.Present)
            {
                if (files.DirectoryState(root) != PathAvailability.Unavailable) DiscoveryFiles.ReconcileMissing(found, previous, root, files);
                continue;
            }
            try
            {
                var valid = false;
                foreach (var version in files.Directories(versions))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var id = Path.GetFileName(version); var path = Path.Combine(version, id + ".json");
                    if (!files.FileExists(path)) continue;
                    using var document = JsonDocument.Parse(files.ReadText(path));
                    if (document.RootElement.TryGetProperty("mainClass", out var main) && main.GetString()?.StartsWith("net.minecraft.", StringComparison.Ordinal) == true &&
                        files.FileExists(Path.Combine(version, id + ".jar"))) { valid = true; break; }
                }
                if (valid) found.Add(GameIdentity.Create("java", "Minecraft Java", GamePlatform.MinecraftJava, Id, root,
                    root, [], GameInstallationState.Installed));
                DiscoveryFiles.ReconcileMissing(found, previous, root, files);
            }
            catch (Exception error) when (DiscoveryFiles.IsReadError(error)) { warnings.Add("Minecraft Java sürüm metaverisi okunamadı. Hesap/profil/kayıt dosyaları okunmadı."); }
        }
        try
        {
            var packages = inventory.Read(cancellationToken);
            foreach (var package in packages.Where(IsBedrock))
                found.Add(GameIdentity.Create(package.FamilyName, "Minecraft Bedrock", GamePlatform.MinecraftBedrock, Id,
                    "current-user-packages", package.InstallDirectory,
                    string.IsNullOrWhiteSpace(package.InstallDirectory) ? [] : package.Executables.Select(p => GameIdentity.Child(package.InstallDirectory, p)).ToArray(),
                    GameInstallationState.Installed, packageFamily: package.FamilyName));
            foreach (var old in previous.Where(g => g.Platform == GamePlatform.MinecraftBedrock && found.All(n => n.Id != g.Id)))
                found.Add(old with { State = GameInstallationState.NotInstalled });
        }
        catch (Exception error) when (error is not OperationCanceledException and not OutOfMemoryException)
        { warnings.Add("Minecraft Bedrock paket sorgusu kullanılamıyor; Java keşfi ve eski kayıtlar korundu."); }
        return new(Id, found, warnings);
    }
}
