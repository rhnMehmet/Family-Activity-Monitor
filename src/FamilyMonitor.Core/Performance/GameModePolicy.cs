namespace FamilyMonitor.Core.Performance;

public sealed class GameModePolicy(IClock clock)
{
    private readonly object gate = new();
    private bool enabled = true, active;
    private TimeSpan? lastExit;
    public bool Enabled { get { lock(gate) return enabled; } set { lock(gate) enabled=value; } }
    public bool IsActive { get { lock(gate) return active; } }
    public TimeSpan? LastExit { get { lock(gate) return lastExit; } }
    public TimeSpan UiRefreshInterval => IsActive ? TimeSpan.FromSeconds(15) : TimeSpan.FromSeconds(5);
    public bool MayRunMaintenance { get { lock(gate) return !active && (lastExit is null || clock.Elapsed-lastExit>=TimeSpan.FromSeconds(45)); } }
    public void Observe(bool tracking, bool paused, bool locked, bool sleeping, bool trustedForegroundGame)
    {
        lock(gate)
        {
            var next = enabled && tracking && !locked && !sleeping && (paused ? active : trustedForegroundGame);
            if (active && !next) lastExit = clock.Elapsed;
            active = next;
        }
    }
}
