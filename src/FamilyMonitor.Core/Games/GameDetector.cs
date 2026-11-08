namespace FamilyMonitor.Core.Games;

public sealed class GameMetadataResolver : IGameMetadataResolver
{
    private static readonly HashSet<string> Shared = new(StringComparer.OrdinalIgnoreCase)
    {
        "java", "javaw", "dotnet", "python", "pythonw", "node", "cmd", "powershell", "pwsh",
        "steam", "steamwebhelper", "epicgameslauncher", "gamingservices", "gamingservicesnet",
        "applicationframehost", "explorer", "setup", "installer", "uninstall", "unins000",
        "crashreporter", "crashreportclient", "unitycrashhandler64", "launcher", "updater"
    };

    public bool IsSharedOrHelperExecutable(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return Shared.Contains(name) || name.StartsWith("unins", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("crashreport", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class GameDetector(IGameMetadataResolver? metadata = null) : IGameDetector
{
    private readonly IGameMetadataResolver metadata = metadata ?? new GameMetadataResolver();

    public GameMatch Detect(ApplicationIdentity app, IReadOnlyList<GameInstallation> catalog, IReadOnlyList<ManualGameMapping> mappings)
    {
        try
        {
            if (app.ExecutablePath is not null && !metadata.IsSharedOrHelperExecutable(app.ExecutablePath))
            {
                var manual = mappings.Where(m => GameIdentity.SamePath(m.ExecutablePath, app.ExecutablePath))
                    .Select(m => catalog.SingleOrDefault(g => g.Id == m.InstallationId)).OfType<GameInstallation>().ToArray();
                if (manual.Length == 1) return new(GameMatchStatus.Matched, manual[0], "Kullanıcının tam dosya yolu eşleştirmesi");
            }
            // A name alone (including javaw) never identifies a game.
            var candidates = catalog.Where(g => g.State == GameInstallationState.Installed &&
                ((!string.IsNullOrEmpty(app.PackageFamilyName) && g.PackageFamilyName == app.PackageFamilyName) ||
                 (app.ExecutablePath is not null && !string.IsNullOrEmpty(g.InstallDirectory) &&
                  GameIdentity.IsWithin(app.ExecutablePath, g.InstallDirectory)))).ToArray();
            if ((app.ProcessName.Equals("java", StringComparison.OrdinalIgnoreCase) || app.ProcessName.Equals("javaw", StringComparison.OrdinalIgnoreCase)) &&
                catalog.Any(g => g.Platform == GamePlatform.MinecraftJava && g.State == GameInstallationState.Installed))
                return new(GameMatchStatus.Ambiguous, null, "Paylaşılan Java süreci oyun olarak doğrulanmadı; bu sürümde JVM oturum ayrımı yapılmıyor.");
            if (candidates.Length == 0) return new(GameMatchStatus.Unknown, null);
            if (app.ExecutablePath is not null && metadata.IsSharedOrHelperExecutable(app.ExecutablePath))
                return new(GameMatchStatus.Ambiguous, null, "Ortak çalışma zamanı / yardımcı süreç; güvenilir oyun eşleştirmesi yapılamadı.");
            if (candidates.Length != 1 || !candidates[0].IsConfirmed)
                return new(GameMatchStatus.Ambiguous, null, "Birden çok eşleşme veya doğrulanmamış oyun adayı; manuel eşleştirme gerekir.");
            return new(GameMatchStatus.Matched, candidates[0], "Kütüphane kurulum yolu / paket kimliği");
        }
        catch (ArgumentException) { return new(GameMatchStatus.Unknown, null, "Çalıştırılabilir dosya yolu doğrulanamadı."); }
    }
}
