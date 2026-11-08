using System.Text.Json;
using FamilyMonitor.Core;
using FamilyMonitor.Core.Games;
using FamilyMonitor.Infrastructure.Games;
using Xunit;

namespace FamilyMonitor.Tests.Games;

public sealed class LibraryProviderTests
{
    private static string Root(char drive = 'C') => $@"{drive}:\Fake_{Guid.NewGuid():N}";

    [Fact]
    public void SteamDiscoversArbitraryGameMetadata()
    {
        var files = new MemoryGameFiles(); var root = Root(); var expected = RandomGame.Steam(files, root);
        var result = new SteamLibraryProvider(files, () => [root]).Discover([], default);
        var game = Assert.Single(result.Installations);
        Assert.Equal(expected.Id, game.Id); Assert.Equal(expected.Name, game.Name);
        Assert.Equal(expected.ExecutablePaths, game.ExecutablePaths);
    }

    [Fact]
    public void SteamFindsSecondLibraryOnDifferentDriveAndSupportsBothVdfLocations()
    {
        var files = new MemoryGameFiles(); var root = Root(); var second = Root('D');
        var firstGame = RandomGame.Steam(files, root); var secondGame = RandomGame.Steam(files, second);
        files.File(Path.Combine(root, "config", "libraryfolders.vdf"), $"// libraries\n\"libraryfolders\" {{ \"0\" {{ \"path\" {RandomGame.Quote(root)} }} \"1\" {{ \"path\" {RandomGame.Quote(second)} }} }}");
        var result = new SteamLibraryProvider(files, () => [root]).Discover([], default);
        Assert.Equal(2, result.Installations.Count);
        Assert.Contains(result.Installations, g => g.Id == firstGame.Id);
        Assert.Contains(result.Installations, g => g.Id == secondGame.Id && g.InstallDirectory.StartsWith("D:", StringComparison.Ordinal));
    }

    [Fact]
    public void RescanDiscoversNewGameWithoutDuplicatingOldGame()
    {
        var files = new MemoryGameFiles(); var root = Root(); RandomGame.Steam(files, root);
        var provider = new SteamLibraryProvider(files, () => [root]); var before = provider.Discover([], default);
        var added = RandomGame.Steam(files, root);
        var after = provider.Discover(before.Installations, default);
        Assert.Equal(2, after.Installations.Count); Assert.Contains(after.Installations, x => x.Id == added.Id);
    }

    [Fact]
    public void RemovedSteamInstallationIsMarkedWithoutDeletingCatalogIdentity()
    {
        var files = new MemoryGameFiles(); var root = Root(); var game = RandomGame.Steam(files, root);
        var provider = new SteamLibraryProvider(files, () => [root]); var before = provider.Discover([], default);
        files.Remove(game.InstallDirectory); files.Remove(Path.Combine(root, "steamapps", $"appmanifest_{game.GameId.Split(':')[1]}.acf"));
        var after = Assert.Single(provider.Discover(before.Installations, default).Installations);
        Assert.Equal(game.Id, after.Id); Assert.Equal(GameInstallationState.NotInstalled, after.State);
    }

    [Fact]
    public void OfflineLibraryDoesNotDeclareGamesUninstalled()
    {
        var files = new MemoryGameFiles(); var root = Root('D'); RandomGame.Steam(files, root);
        var provider = new SteamLibraryProvider(files, () => [root]); var before = provider.Discover([], default);
        files.Unavailable.Add(root);
        var scan = provider.Discover(before.Installations, default);
        Assert.Empty(scan.Installations); Assert.NotEmpty(scan.Warnings);
    }

    [Fact]
    public void CorruptManifestDoesNotRemovePreviousGame()
    {
        var files = new MemoryGameFiles(); var root = Root(); var game = RandomGame.Steam(files, root);
        files.File(Path.Combine(root, "steamapps", $"appmanifest_{game.GameId.Split(':')[1]}.acf"), "\"AppState\" {");
        var scan = new SteamLibraryProvider(files, () => [root]).Discover([game], default);
        Assert.Empty(scan.Installations); Assert.NotEmpty(scan.Warnings);
    }

    [Fact]
    public void SteamManifestCannotEscapeLibraryRoot()
    {
        var files = new MemoryGameFiles(); var root = Root();
        files.File(Path.Combine(root, "steamapps", "appmanifest_123.acf"), "\"AppState\" {\"appid\" \"123\" \"name\" \"Random\" \"installdir\" \"../../outside\" \"StateFlags\" \"4\"}");
        var scan = new SteamLibraryProvider(files, () => [root]).Discover([], default);
        Assert.Empty(scan.Installations); Assert.NotEmpty(scan.Warnings);
    }

    [Fact]
    public void EpicUsesLocalManifestWithArbitraryNameAndInstallDrive()
    {
        var files = new MemoryGameFiles(); var root = Root(); var directory = Root('E'); var exe = RandomGame.Exe(); var name = RandomGame.Name();
        files.File(Path.Combine(directory, exe));
        files.File(Path.Combine(root, "entry.item"), JsonSerializer.Serialize(new { AppName = Guid.NewGuid().ToString(), DisplayName = name,
            InstallLocation = directory, LaunchExecutable = exe, bIsApplication = true, AppCategories = new[] { "games" } }));
        var game = Assert.Single(new EpicLibraryProvider(files, () => [root]).Discover([], default).Installations);
        Assert.Equal(name, game.Name); Assert.Equal(directory, game.InstallDirectory); Assert.Equal(GamePlatform.Epic, game.Platform);
    }

    [Fact]
    public void EpicNonApplicationEntryIsNotAutomaticallyAGame()
    {
        var files = new MemoryGameFiles(); var root = Root(); files.File(Path.Combine(root, "tool.item"), "{\"bIsApplication\":false}");
        Assert.Empty(new EpicLibraryProvider(files, () => [root]).Discover([], default).Installations);
    }

    [Fact]
    public void XboxUsesGameConfigurationAndIgnoresOrdinaryPackages()
    {
        var name = RandomGame.Name(); var exe = RandomGame.Exe(); var package = new InstalledPackage("Package_" + Guid.NewGuid().ToString("N"),
            Guid.NewGuid().ToString("N") + "_publisher", Root(), name, [exe], RandomGame.Config("Identity", name, exe));
        var ordinary = new InstalledPackage("Utility", "utility_publisher", Root(), "Utility", [RandomGame.Exe()]);
        var game = Assert.Single(new XboxLibraryProvider(new FakePackages(package, ordinary)).Discover([], default).Installations);
        Assert.Equal(name, game.Name); Assert.Equal(package.FamilyName, game.PackageFamilyName);
    }

    [Fact]
    public void XboxMissingMetadataIsUnknownWhileUnregisteredPackageIsNotInstalled()
    {
        var package = new InstalledPackage("Package", "pkg_publisher", Root(), RandomGame.Name(), [RandomGame.Exe()], RandomGame.Config("id", RandomGame.Name(), "game.exe"));
        var inventory = new FakePackages(package); var provider = new XboxLibraryProvider(inventory);
        var before = provider.Discover([], default);
        inventory.Packages = [package with { GameConfigXml = null }];
        Assert.Equal(GameInstallationState.Unknown, Assert.Single(provider.Discover(before.Installations, default).Installations).State);
        inventory.Packages = [];
        Assert.Equal(GameInstallationState.NotInstalled, Assert.Single(provider.Discover(before.Installations, default).Installations).State);
    }

    [Fact]
    public void MinecraftEditionsAreSeparateAndAccountOrSaveFilesAreNotRead()
    {
        var files = new MemoryGameFiles(); var root = Root(); var version = Guid.NewGuid().ToString("N");
        files.File(Path.Combine(root, "versions", version, version + ".json"), "{\"mainClass\":\"net.minecraft.client.main.Main\"}");
        files.File(Path.Combine(root, "versions", version, version + ".jar"));
        files.File(Path.Combine(root, "launcher_profiles.json"), "must never be read");
        files.File(Path.Combine(root, "saves", "private.txt"), "must never be read");
        var bedrock = new InstalledPackage("Microsoft.MinecraftUWP", "family_publisher", Root(), "resource", [RandomGame.Exe()]);
        var result = new MinecraftLibraryProvider(files, () => [root], new FakePackages(bedrock)).Discover([], default);
        Assert.Equal(2, result.Installations.Count);
        Assert.Contains(result.Installations, g => g.Platform == GamePlatform.MinecraftJava);
        Assert.Contains(result.Installations, g => g.Platform == GamePlatform.MinecraftBedrock);
        Assert.Single(files.Reads); Assert.EndsWith(".json", files.Reads[0]); Assert.Contains("versions", files.Reads[0]);
    }

    [Fact]
    public void BrokenPackageQueryDoesNotPreventJavaDiscovery()
    {
        var files = new MemoryGameFiles(); var root = Root();
        files.File(Path.Combine(root, "versions", "generated", "generated.json"), "{\"mainClass\":\"net.minecraft.client.main.Main\"}");
        files.File(Path.Combine(root, "versions", "generated", "generated.jar"));
        var result = new MinecraftLibraryProvider(files, () => [root], new FakePackages { Fail = true }).Discover([], default);
        Assert.Single(result.Installations); Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void ArbitraryExeInUserFolderIsOnlyAnUnconfirmedCandidate()
    {
        var files = new MemoryGameFiles(); var root = Root(); var exe = Path.Combine(root, RandomGame.Exe()); files.File(exe);
        var game = Assert.Single(new FolderLibraryProvider(files, () => [root]).Discover([], default).Installations);
        Assert.False(game.IsConfirmed);
        var app = new ApplicationIdentity("Unknown", "process", 1, exe);
        Assert.Equal(GameMatchStatus.Ambiguous, new GameDetector().Detect(app, [game], []).Status);
    }

    [Fact]
    public void StandaloneRequiresGameSpecificMetadataAndNeverTreatsEveryExeAsAGame()
    {
        var files = new MemoryGameFiles(); var root = Root(); files.File(Path.Combine(root, RandomGame.Exe()));
        var provider = new StandaloneLibraryProvider(files, () => [root]);
        Assert.Empty(provider.Discover([], default).Installations);
        var name = RandomGame.Name(); files.File(Path.Combine(root, "MicrosoftGame.config"), RandomGame.Config("id", name, RandomGame.Exe()));
        Assert.Equal(name, Assert.Single(provider.Discover([], default).Installations).Name);
    }

    [Fact]
    public void WholeDiskUserSelectionIsRejected()
    {
        var files = new MemoryGameFiles(); files.Directory(@"C:\");
        var result = new FolderLibraryProvider(files, () => [@"C:\"]).Discover([], default);
        Assert.Empty(result.Installations); Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void MaliciousXmlCannotReadExternalFiles()
    {
        Assert.ThrowsAny<System.Xml.XmlException>(() => GameConfiguration.Read("<!DOCTYPE Game [<!ENTITY file SYSTEM 'file:///private'>]><Game>&file;</Game>", Root()));
    }
}
