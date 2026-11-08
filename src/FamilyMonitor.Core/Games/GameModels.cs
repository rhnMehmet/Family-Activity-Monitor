using System.Security.Cryptography;
using System.Text;

namespace FamilyMonitor.Core.Games;

public enum GamePlatform { Steam, Epic, Xbox, MinecraftJava, MinecraftBedrock, Standalone, Custom }
public enum GameInstallationState { Installed, NotInstalled, Unknown }
public enum GameMatchStatus { Unknown, Matched, Ambiguous }

public sealed record GameInstallation(string Id, string GameId, string Name, GamePlatform Platform,
    string ProviderId, string Scope, string InstallDirectory, IReadOnlyList<string> ExecutablePaths,
    GameInstallationState State, bool IsConfirmed = true, string? PackageFamilyName = null,
    DateTimeOffset? LastPlayedUtc = null);

public sealed record ManualGameMapping(string ExecutablePath, string InstallationId);
public sealed record GameMatch(GameMatchStatus Status, GameInstallation? Installation, string? Explanation = null);
public sealed record GameLibraryScan(string ProviderId, IReadOnlyList<GameInstallation> Installations,
    IReadOnlyList<string> Warnings);
public sealed record GameDiscoveryReport(bool Ran, IReadOnlyList<string> Messages);
public sealed record GameUsage(string GameId, string Name, GamePlatform Platform, TimeSpan Duration,TimeSpan? ForegroundDuration=null);
public sealed record GameUsageHistory(string Name, GamePlatform Platform, ActivityRecord Activity);

public static class GameIdentity
{
    public static string Stable(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToUpperInvariant())));
    public static string NormalizePath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("Only absolute local paths are supported.", nameof(path));
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
    public static bool SamePath(string a, string b) => string.Equals(NormalizePath(a), NormalizePath(b), StringComparison.OrdinalIgnoreCase);
    public static bool IsWithin(string file, string directory) => SamePath(file, directory) ||
        NormalizePath(file).StartsWith(NormalizePath(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public static string Child(string root, string relative)
    {
        if (Path.IsPathRooted(relative)) throw new ArgumentException("Expected a relative path.");
        var child = NormalizePath(Path.Combine(root, relative));
        if (!IsWithin(child, root)) throw new ArgumentException("Path escapes installation root.");
        return child;
    }

    public static GameInstallation Create(string externalId, string name, GamePlatform platform, string provider,
        string scope, string directory, IReadOnlyList<string> executables, GameInstallationState state,
        bool confirmed = true, string? packageFamily = null)
    {
        var gameId = $"{platform}:{externalId}";
        return new(Stable($"{gameId}|{scope}|{directory}"), gameId, name, platform, provider, scope,
            directory, executables, state, confirmed, packageFamily);
    }
}
