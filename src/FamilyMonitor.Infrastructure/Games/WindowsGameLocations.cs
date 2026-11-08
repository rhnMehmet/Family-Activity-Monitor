using System.Runtime.Versioning;
using Microsoft.Win32;

namespace FamilyMonitor.Infrastructure.Games;

[SupportedOSPlatform("windows")]
public static class WindowsGameLocations
{
    public static IEnumerable<string> SteamRoots()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ReadRegistryPaths(RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath", result);
        ReadRegistryPaths(RegistryHive.LocalMachine, @"Software\Valve\Steam", "InstallPath", result);
        result.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        return result;
    }
    public static IEnumerable<string> EpicManifestRoots()
    {
        var locations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ReadRegistryPaths(RegistryHive.LocalMachine, @"Software\Epic Games\EpicGamesLauncher", "AppDataPath", locations);
        locations.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data"));
        return locations.Select(path => Path.Combine(path, "Manifests")).ToArray();
    }
    public static IEnumerable<string> JavaRoots() => [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft")];
    public static IEnumerable<string> StandaloneRoots()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var key = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (key is null) continue;
                    foreach (var name in key.GetSubKeyNames().Take(2000))
                    {
                        using var item = key.OpenSubKey(name);
                        if (item?.GetValue("InstallLocation") is string path && Path.IsPathFullyQualified(path)) result.Add(path);
                    }
                }
                catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
            }
        return result;
    }
    private static void ReadRegistryPaths(RegistryHive hive, string subKey, string value, HashSet<string> result)
    {
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(subKey);
                if (key?.GetValue(value) is string path && Path.IsPathFullyQualified(path)) result.Add(path);
            }
            catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        }
    }
}
