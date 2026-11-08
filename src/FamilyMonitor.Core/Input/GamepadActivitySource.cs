namespace FamilyMonitor.Core.Input;

public sealed record ControllerEvidence(bool Connected, bool Active, bool Supported = true);
public interface IControllerActivityProvider { ControllerEvidence Read(); }

/// <summary>Retains only last activity time, never buttons/axes/combinations. Only trusted foreground games poll controllers.</summary>
public sealed class GamepadActivitySource(IActivitySource source, IControllerActivityProvider controllers, IClock clock) : IActivitySource
{
    private string? lastGame;
    private TimeSpan? lastInteraction;
    public ActivitySample Read()
    {
        var sample = source.Read();
        var game = sample.IsSessionLocked ? null : sample.Application?.GameInstallationId;
        if (game != lastGame) { lastInteraction = null; lastGame = game; }
        if (game is null) return sample;
        var evidence = controllers.Read();
        if (evidence.Supported && evidence.Active && evidence.Connected) lastInteraction = clock.Elapsed;
        var idle = sample.IdleTime;
        if (lastInteraction is { } last)
        {
            var padIdle = clock.Elapsed >= last ? clock.Elapsed - last : TimeSpan.Zero;
            if (padIdle < idle) idle = padIdle;
        }
        return sample with { IdleTime = idle, ControllerStatus = !evidence.Supported ? "XInput kullanılamıyor" :
            evidence.Connected ? "XInput bağlı; yalnızca gözlenen etkileşim doğrulanır" : "XInput algılanmadı; diğer kontrolcüler doğrulanamaz" };
    }
}
