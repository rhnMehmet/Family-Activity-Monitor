using FamilyMonitor.Core;
using FamilyMonitor.Core.Games;
using Xunit;

namespace FamilyMonitor.Tests.Games;

public sealed class GameDetectorTests
{
    private static GameInstallation Game() => RandomGame.Create(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
    [Fact]
    public void RelativeOrNetworkPathsAreNotAcceptedForLocalDiscovery()
    {
        Assert.Throws<ArgumentException>(() => GameIdentity.NormalizePath("relative/game.exe"));
        Assert.Throws<ArgumentException>(() => GameIdentity.NormalizePath(@"\\remote\share\game.exe"));
    }
    [Fact]
    public void CatalogPathResolvesRealMetadataName()
    {
        var game = Game(); var match = new GameDetector().Detect(new("Raw", "runtime", 1, game.ExecutablePaths[0]), [game], []);
        Assert.Equal(GameMatchStatus.Matched, match.Status); Assert.Equal(game.Name, match.Installation?.Name);
    }
    [Fact]
    public void SameProcessNameInAnotherFolderIsNotAGame()
    {
        var game = Game(); var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), Path.GetFileName(game.ExecutablePaths[0]));
        Assert.Equal(GameMatchStatus.Unknown, new GameDetector().Detect(new("Raw", "runtime", 1, path), [game], []).Status);
    }
    [Fact]
    public void ProcessNameAloneNeverResolvesAGame()
    {
        Assert.Equal(GameMatchStatus.Unknown, new GameDetector().Detect(new("Java", "javaw", 1), [Game()], []).Status);
    }
    [Theory]
    [InlineData("javaw.exe")]
    [InlineData("steam.exe")]
    [InlineData("EpicGamesLauncher.exe")]
    [InlineData("launcher.exe")]
    public void SharedRuntimeOrLauncherDoesNotCountEvenInsideGameRootOrWithPersistentMapping(string filename)
    {
        var game = Game(); var path = Path.Combine(game.InstallDirectory, filename);
        var result = new GameDetector().Detect(new("Helper", Path.GetFileNameWithoutExtension(filename), 1, path), [game], [new(path, game.Id)]);
        Assert.NotEqual(GameMatchStatus.Matched, result.Status);
    }
    [Fact]
    public void SameTitleOnTwoPlatformsRemainsTwoIdentitiesAndAmbiguousRootIsNotGuessed()
    {
        var first = Game(); var other = first with { Id = Guid.NewGuid().ToString(), GameId = "Epic:" + Guid.NewGuid(), Platform = GamePlatform.Epic };
        Assert.NotEqual(first.GameId, other.GameId);
        Assert.Equal(GameMatchStatus.Ambiguous, new GameDetector().Detect(new("Raw", "runtime", 1, first.ExecutablePaths[0]), [first, other], []).Status);
    }
    [Fact]
    public void ExactManualMappingResolvesUnknownExecutableToSelectedGame()
    {
        var game = Game(); var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), RandomGame.Exe());
        var result = new GameDetector().Detect(new("Raw", "runtime", 1, path), [game], [new(path, game.Id)]);
        Assert.Equal(game.Id, result.Installation?.Id);
    }
    [Fact]
    public void DirectoryPrefixDoesNotMatchSiblingDirectory()
    {
        var game = Game(); var path = game.InstallDirectory + "-other" + Path.DirectorySeparatorChar + RandomGame.Exe();
        Assert.Equal(GameMatchStatus.Unknown, new GameDetector().Detect(new("Raw", "runtime", 1, path), [game], []).Status);
    }
    [Fact]
    public void PackageFamilyResolvesProtectedExecutableWithoutReadingProtectedFolders()
    {
        var game = Game() with { PackageFamilyName = Guid.NewGuid() + "_publisher" };
        var result = new GameDetector().Detect(new("Package", "process", 1, PackageFamilyName: game.PackageFamilyName), [game], []);
        Assert.Equal(game.Id, result.Installation?.Id);
    }
    [Fact]
    public void MinecraftCatalogDoesNotTurnUnrelatedJavaApplicationIntoGame()
    {
        var game = Game() with { Platform = GamePlatform.MinecraftJava };
        var result = new GameDetector().Detect(new("Java", "javaw", 1, Path.Combine(Path.GetTempPath(), "shared-runtime", "javaw.exe")), [game], []);
        Assert.Equal(GameMatchStatus.Ambiguous, result.Status); Assert.Null(result.Installation);
    }
    [Fact]
    public void UninstalledGameDoesNotAutomaticallyMatchAReusedPath()
    {
        var game = Game() with { State = GameInstallationState.NotInstalled };
        Assert.Equal(GameMatchStatus.Unknown, new GameDetector().Detect(new("Raw", "runtime", 1, game.ExecutablePaths[0]), [game], []).Status);
    }
}
