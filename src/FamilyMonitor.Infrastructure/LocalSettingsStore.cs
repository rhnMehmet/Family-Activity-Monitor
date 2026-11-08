using System.Text.Json;

namespace FamilyMonitor.Infrastructure;

public sealed record LocalSettings
{
    public int PrivacyVersion { get; init; }
    public bool ConsentGranted { get; init; }
    public int IdleThresholdSeconds { get; init; } = 180;
    public string DeviceId { get; init; } = Guid.NewGuid().ToString("D");
    public string LocalUserId { get; init; } = Guid.NewGuid().ToString("D");
    public bool GameDiscoveryConsentGranted { get; init; }
    public int GameDiscoveryPrivacyVersion { get; init; }
    public string[] GameFolders { get; init; } = [];
    public bool AutomaticGameMode { get; init; } = true;
}

public sealed class LocalSettingsStore(string directory)
{
    public const int CurrentPrivacyVersion = 1;
    public const int CurrentGameDiscoveryPrivacyVersion = 1;
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    private string SettingsPath => Path.Combine(DirectoryPath, "settings.json");

    public LocalSettings Load()
    {
        if (!File.Exists(SettingsPath)) return new();
        try
        {
            var settings = JsonSerializer.Deserialize<LocalSettings>(File.ReadAllText(SettingsPath));
            if (settings is null || settings.IdleThresholdSeconds is < 5 or > 3600 ||
                !Guid.TryParse(settings.DeviceId, out _) || !Guid.TryParse(settings.LocalUserId, out _)) return new();
            settings = settings with { GameFolders = (settings.GameFolders ?? []).Where(p => !string.IsNullOrWhiteSpace(p) && Path.IsPathFullyQualified(p) && !p.StartsWith(@"\\", StringComparison.Ordinal))
                .Where(p => !string.Equals(Path.GetPathRoot(p), Path.TrimEndingDirectorySeparator(p), StringComparison.OrdinalIgnoreCase))
                .Take(64).ToArray() };
            if (settings.GameDiscoveryPrivacyVersion != CurrentGameDiscoveryPrivacyVersion) settings = settings with { GameDiscoveryConsentGranted = false };
            return settings.PrivacyVersion == CurrentPrivacyVersion ? settings : settings with { ConsentGranted = false, GameDiscoveryConsentGranted = false };
        }
        catch (JsonException) { return new(); } // Fail closed on corrupt settings.
    }

    public void Save(LocalSettings settings)
    {
        Directory.CreateDirectory(DirectoryPath);
        var temporaryPath = SettingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporaryPath, SettingsPath, overwrite: true);
    }
}
