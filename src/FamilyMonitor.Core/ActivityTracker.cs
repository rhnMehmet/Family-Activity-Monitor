namespace FamilyMonitor.Core;

/// <summary>
/// Single-threaded state machine. Only the input-confirmed part of a foreground period is persisted.
/// Unconfirmed reading time is held in memory until another input confirms it, or discarded on idle/stop.
/// The host serializes ticks and controls on its UI thread.
/// </summary>
public sealed class ActivityTracker
{
    private readonly IClock clock;
    private readonly IActivitySource source;
    private readonly IActivityStore store;
    private readonly string deviceId;
    private readonly string sessionId;
    private TrackerOptions options;
    private readonly DateTimeOffset anchorUtc;
    private readonly TimeSpan anchorElapsed;
    private TimeSpan lastElapsed;
    private DateTimeOffset lastSampleUtc;
    private DateTimeOffset boundaryUtc;
    private ActivityRecord? current;
    private long currentWindow;
    private bool enabled;
    private bool paused;
    private bool locked;
    private bool suspended;
    private readonly Games.GameForegroundTracker? foreground;
    public bool HasForegroundGame { get; private set; }
    public string? ControllerStatus { get; private set; }

    public ActivityTracker(IClock clock, IActivitySource source, IActivityStore store,
        string deviceId, string sessionId, TrackerOptions? options = null, Games.IGameForegroundStore? foregroundStore = null)
    {
        this.clock = clock;
        this.source = source;
        this.store = store;
        this.deviceId = deviceId;
        this.sessionId = sessionId;
        this.options = options ?? new TrackerOptions();
        this.options.Validate();
        foreground = foregroundStore is null ? null : new Games.GameForegroundTracker(foregroundStore);
        anchorElapsed = lastElapsed = clock.Elapsed;
        // A backwards wall-clock correction, including across restarts, must not overlap saved records.
        anchorUtc = Max(clock.UtcNow.ToUniversalTime(), store.GetLatestEnd() ?? DateTimeOffset.MinValue);
        boundaryUtc = lastSampleUtc = anchorUtc;
    }

    public bool HasConsent { get; private set; }
    public bool IsEnabled => enabled;
    public bool IsPaused => paused;
    public bool IsSessionLocked => locked || State == TrackingState.Locked;
    public bool IsSuspended => suspended;
    public TimeSpan IdleThreshold => options.IdleThreshold;
    public TimeSpan LastObservedIdleTime { get; private set; }
    public TrackingState State { get; private set; } = TrackingState.Stopped;
    public string? CurrentApplication => State == TrackingState.Active ? current?.ApplicationName : null;
    public string? CurrentGameNote { get; private set; }
    public string? LastError { get; private set; }

    public void GrantConsent() => HasConsent = true;

    public void RevokeConsent()
    {
        // Close already-observed presence before withdrawing; no new source read is performed.
        try { foreground?.Close(Now(), options.MaximumSampleGap); }
        catch (Exception error) when (error is not OutOfMemoryException) { LastError=error.GetType().Name; }
        HasForegroundGame = false;
        // No source read or new activity write is allowed after withdrawal.
        HasConsent = false;
        enabled = paused = false;
        current = null;
        CurrentGameNote = null;
        boundaryUtc = Now();
        State = TrackingState.Stopped;
    }

    public bool Start()
    {
        if (!HasConsent) return false;
        if (enabled) return true;
        enabled = true;
        paused = false;
        LastError = null;
        boundaryUtc = lastSampleUtc = Now();
        Tick();
        return enabled;
    }

    public void Stop() => ChangeBoundary(() => { enabled = paused = false; }, TrackingState.Stopped);
    public void Pause() => ChangeBoundary(() => paused = true, enabled ? TrackingState.Paused : TrackingState.Stopped);
    public void Resume()
    {
        if (!HasConsent || !enabled) return;
        paused = false;
        boundaryUtc = lastSampleUtc = Now();
        Tick();
    }

    public void SetLocked(bool value)
    {
        ChangeBoundary(() => locked = value, value && enabled ? TrackingState.Locked : TrackingState.Stopped);
        if (!value) Tick();
    }

    public void SetSuspended(bool value)
    {
        ChangeBoundary(() => suspended = value, value && enabled ? TrackingState.Suspended : TrackingState.Stopped);
        if (!value) Tick();
    }

    public void UpdateOptions(TrackerOptions value)
    {
        value.Validate();
        ChangeBoundary(() => options = value, TrackingState.Stopped);
        Tick();
    }

    public void ClearHistory()
    {
        foreground?.Discard();
        HasForegroundGame = false;
        enabled = paused = false;
        current = null; // Never allow an in-memory session to resurrect deleted data.
        State = TrackingState.Stopped;
        boundaryUtc = lastSampleUtc = Now();
        store.DeleteAll();
        LastError = null;
    }

    public void Tick()
    {
        if (!HasConsent || !enabled) { State = TrackingState.Stopped; return; }
        if (paused) { State = TrackingState.Paused; return; }
        if (suspended) { State = TrackingState.Suspended; return; }
        if (locked) { State = TrackingState.Locked; return; }

        try
        {
            var now = Now();
            var gap = now - lastSampleUtc;
            if (gap > options.MaximumSampleGap)
            {
                Finish();
                boundaryUtc = now; // Unobserved time (sleep, blocked UI, debugger) is never assigned.
            }
            lastSampleUtc = now;
            var sample = source.Read();
            foreground?.Observe(sample, now, options.MaximumSampleGap);
            HasForegroundGame = !sample.IsSessionLocked && sample.Application?.GameInstallationId is not null;
            ControllerStatus = sample.ControllerStatus;
            if (sample.IsSessionLocked) { Finish(); State = TrackingState.Locked; boundaryUtc = now; return; }
            if (sample.IdleTime < TimeSpan.Zero) throw new InvalidOperationException("Invalid idle time.");
            LastObservedIdleTime = sample.IdleTime;
            if (sample.IdleTime >= options.IdleThreshold)
            {
                Finish(); State = TrackingState.Idle; boundaryUtc = now; return;
            }
            if (sample.Application is null)
            {
                Finish(); State = TrackingState.Unavailable; boundaryUtc = now; return;
            }

            var app = sample.Application;
            CurrentGameNote = app.GameResolutionNote;
            var inputUtc = now - sample.IdleTime;
            if (current is not null && inputUtc - current.EndUtc >= options.IdleThreshold)
            {
                // The user can return between two samples, without a sample ever observing Idle.
                // Detect that full input gap before confirming it as foreground use.
                Finish();
                boundaryUtc = inputUtc;
                State = TrackingState.Idle;
            }
            if (current is null || currentWindow != app.WindowId || current.ProcessName != app.ProcessName || current.GameInstallationId != app.GameInstallationId)
            {
                if (current is not null)
                {
                    var end = Max(current.EndUtc, Max(current.StartUtc, now - sample.IdleTime));
                    current = current with { EndUtc = end, ActiveDuration = end - current.StartUtc };
                }
                Finish();
                // New windows are only known from this sample; never backdate them into another app.
                var start = State == TrackingState.Idle ? Max(boundaryUtc, now - sample.IdleTime) : now;
                current = new ActivityRecord(Guid.NewGuid(), deviceId, sessionId, app.GameName ?? app.ApplicationName,
                    app.ProcessName, start, start, TimeSpan.Zero, ActivityStatus.Active, app.GameInstallationId);
                currentWindow = app.WindowId;
            }

            var confirmedEnd = Max(current.EndUtc, Max(current.StartUtc, now - sample.IdleTime));
            current = current with { EndUtc = confirmedEnd, ActiveDuration = confirmedEnd - current.StartUtc };
            if (current.ActiveDuration > TimeSpan.Zero) store.Save(current);
            State = TrackingState.Active;
            LastError = null;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Provider/storage faults stop collection and require an explicit restart.
            current = null;
            HasForegroundGame = false;
            try { foreground?.Close(lastSampleUtc, options.MaximumSampleGap); } catch (Exception closeError) when (closeError is not OutOfMemoryException) { }
            enabled = paused = false;
            State = TrackingState.Faulted;
            LastError = error.GetType().Name; // Do not expose paths, titles or other user data in diagnostics.
        }
    }

    private void Finish()
    {
        if (current is { ActiveDuration: var duration } && duration > TimeSpan.Zero)
            store.Save(current with { Status = ActivityStatus.Completed });
        current = null;
    }

    private void ChangeBoundary(Action change, TrackingState nextState)
    {
        // Controls take effect even when the final database write fails.
        try { foreground?.Close(Now(), options.MaximumSampleGap); Finish(); }
        catch (Exception error) when (error is not OutOfMemoryException) { LastError = error.GetType().Name; }
        finally
        {
            current = null;
            HasForegroundGame = false;
            change();
            boundaryUtc = lastSampleUtc = Now();
            State = nextState;
        }
    }

    private DateTimeOffset Now()
    {
        lastElapsed = clock.Elapsed > lastElapsed ? clock.Elapsed : lastElapsed;
        return anchorUtc + (lastElapsed - anchorElapsed);
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
}
