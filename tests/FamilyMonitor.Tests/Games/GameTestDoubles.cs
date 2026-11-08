using FamilyMonitor.Core.Games;
using FamilyMonitor.Infrastructure.Games;
using System.IO.Enumeration;

namespace FamilyMonitor.Tests.Games;

internal sealed class MemoryGameFiles : IGameFileSystem
{
    private readonly Dictionary<string, string> content = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> directories = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Unavailable { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Reads { get; } = [];
    public void Directory(string path)
    {
        var current = GameIdentity.NormalizePath(path);
        while (directories.Add(current) && Path.GetDirectoryName(current) is { } parent) current = GameIdentity.NormalizePath(parent);
    }
    public void File(string path, string text = "") { Directory(Path.GetDirectoryName(path)!); content[GameIdentity.NormalizePath(path)] = text; }
    public void Remove(string path)
    {
        foreach (var file in content.Keys.Where(p => GameIdentity.IsWithin(p, path)).ToArray()) content.Remove(file);
        foreach (var directory in directories.Where(p => GameIdentity.IsWithin(p, path)).ToArray()) directories.Remove(directory);
    }
    private void Check(string path) { if (Unavailable.Any(p => GameIdentity.IsWithin(path, p))) throw new UnauthorizedAccessException(); }
    public string ReadText(string path) { Check(path); Reads.Add(path); return content[GameIdentity.NormalizePath(path)]; }
    public string[] Files(string directory, string pattern)
    {
        Check(directory);
        return content.Keys.Where(p => GameIdentity.SamePath(Path.GetDirectoryName(p)!, directory) &&
            FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(p), true)).ToArray();
    }
    public string[] Directories(string directory)
    {
        Check(directory);
        return directories.Where(p => Path.GetDirectoryName(p) is { } parent && GameIdentity.SamePath(parent, directory)).ToArray();
    }
    public PathAvailability DirectoryState(string directory) => Unavailable.Any(p => GameIdentity.IsWithin(directory, p))
        ? PathAvailability.Unavailable : directories.Contains(GameIdentity.NormalizePath(directory)) ? PathAvailability.Present : PathAvailability.Missing;
    public bool FileExists(string path) { Check(path); return content.ContainsKey(GameIdentity.NormalizePath(path)); }
}

internal sealed class FakePackages(params InstalledPackage[] packages) : IWindowsPackageInventory
{
    public InstalledPackage[] Packages { get; set; } = packages;
    public bool Fail { get; set; }
    public IReadOnlyList<InstalledPackage> Read(CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); if (Fail) throw new IOException(); return Packages; }
}

internal sealed class FakeGameProvider(string id, Func<GameLibraryScan> scan) : IGameLibraryProvider
{
    public string Id => id;
    public int Calls { get; private set; }
    public GameLibraryScan Discover(IReadOnlyList<GameInstallation> previous, CancellationToken cancellationToken) { Calls++; return scan(); }
}

internal static class RandomGame
{
    private static int nextSteamId = 100000;
    public static string Name() => "Oyun_" + Guid.NewGuid().ToString("N");
    public static string Exe() => "runtime_" + Guid.NewGuid().ToString("N") + ".exe";
    public static GameInstallation Create(string directory, GamePlatform platform = GamePlatform.Steam, bool confirmed = true)
    {
        var name = Name();
        return GameIdentity.Create(Guid.NewGuid().ToString("N"), name, platform, platform.ToString(), Path.GetDirectoryName(directory)!,
            directory, [Path.Combine(directory, Exe())], GameInstallationState.Installed, confirmed);
    }
    public static string Quote(string text) => '"' + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + '"';
    public static GameInstallation Steam(MemoryGameFiles files, string library, string? id = null)
    {
        id ??= Interlocked.Increment(ref nextSteamId).ToString();
        var name = Name(); var folder = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(library, "steamapps", "common", folder); var exe = Path.Combine(directory, Exe());
        files.File(exe);
        files.File(Path.Combine(library, "steamapps", $"appmanifest_{id}.acf"),
            $"\"AppState\" {{ \"appid\" {Quote(id)} \"name\" {Quote(name)} \"installdir\" {Quote(folder)} \"StateFlags\" \"4\" }}");
        return GameIdentity.Create(id, name, GamePlatform.Steam, "Steam", library, directory, [exe], GameInstallationState.Installed);
    }
    public static string Config(string id, string name, string exe) =>
        $"<Game><Identity Name='{id}'/><ExecutableList><Executable Name='{exe}'/></ExecutableList><ShellVisuals DefaultDisplayName='{name}'/></Game>";
}
