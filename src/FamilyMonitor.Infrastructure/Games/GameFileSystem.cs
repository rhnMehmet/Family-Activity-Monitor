using FamilyMonitor.Core.Games;

namespace FamilyMonitor.Infrastructure.Games;

public enum PathAvailability { Present, Missing, Unavailable }
public interface IGameFileSystem
{
    string ReadText(string path);
    string[] Files(string directory, string pattern);
    string[] Directories(string directory);
    PathAvailability DirectoryState(string directory);
    bool FileExists(string path);
    MetadataStamp? Stamp(string path) => null;
}

public sealed class GameFileSystem : IGameFileSystem
{
    public MetadataStamp? Stamp(string path)
    {
        try { var attributes=File.GetAttributes(path);return new((attributes&FileAttributes.Directory)!=0?0:new FileInfo(path).Length,File.GetLastWriteTimeUtc(path).Ticks); }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException){return null;}
    }
    public string ReadText(string path)
    {
        if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new IOException("Metadata exceeds size limit.");
        return File.ReadAllText(path);
    }
    private static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    public string[] Files(string directory, string pattern) => Directory.EnumerateFiles(directory, pattern)
        .Where(p => !IsLink(p)).Take(2001).ToArray() is var files && files.Length <= 2000 ? files : throw new IOException("File budget exceeded.");
    public string[] Directories(string directory) => Directory.EnumerateDirectories(directory)
        .Where(p => !IsLink(p)).Take(1001).ToArray() is var directories && directories.Length <= 1000 ? directories : throw new IOException("Directory budget exceeded.");
    public bool FileExists(string path) => File.Exists(path) && !IsLink(path);
    public PathAvailability DirectoryState(string directory)
    {
        try { return (File.GetAttributes(directory) & FileAttributes.Directory) != 0 && !IsLink(directory) ? PathAvailability.Present : PathAvailability.Unavailable; }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        { return Directory.Exists(Path.GetPathRoot(Path.GetFullPath(directory))) ? PathAvailability.Missing : PathAvailability.Unavailable; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return PathAvailability.Unavailable; }
    }
}

internal static class DiscoveryFiles
{
    public static string[] Executables(IGameFileSystem files, string root, CancellationToken token)
    {
        var result = new List<string>();
        var visited = 0;
        void Visit(string directory, int depth)
        {
            token.ThrowIfCancellationRequested();
            if (++visited > 300) throw new IOException("Installation scan budget exceeded.");
            if (files.DirectoryState(directory) != PathAvailability.Present) return;
            result.AddRange(files.Files(directory, "*.exe"));
            if (result.Count > 2000) throw new IOException("Executable budget exceeded.");
            if (depth == 0) return;
            foreach (var child in files.Directories(directory))
            {
                var name = Path.GetFileName(child);
                if (name.Equals("userdata", StringComparison.OrdinalIgnoreCase) || name.Equals("saves", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("logs", StringComparison.OrdinalIgnoreCase) || name.Equals(".egstore", StringComparison.OrdinalIgnoreCase)) continue;
                Visit(child, depth - 1);
            }
        }
        Visit(root, 4);
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static void ReconcileMissing(List<GameInstallation> found, IReadOnlyList<GameInstallation> previous,
        string scope, IGameFileSystem files, bool authoritative = false)
    {
        var foundIds=found.Select(g=>g.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var old in previous.Where(g => g.Scope == scope && !foundIds.Contains(g.Id)))
        {
            var state = authoritative || (!string.IsNullOrEmpty(old.InstallDirectory) && files.DirectoryState(old.InstallDirectory) == PathAvailability.Missing)
                ? GameInstallationState.NotInstalled : GameInstallationState.Unknown;
            found.Add(old with { State = state });
        }
    }

    public static bool IsReadError(Exception error) => error is IOException or UnauthorizedAccessException or ArgumentException or FormatException or System.Text.Json.JsonException or System.Xml.XmlException;
}
