namespace FamilyMonitor.Core;

public enum TrackingState { Stopped, Active, Idle, Locked, Suspended, Paused, Unavailable, Faulted }
public enum ActivityStatus { Active, Completed, Interrupted }

// Executable/package data is optional, in-memory and read only under separate game discovery consent.
// No window title, command line or browser contents are collected.
public sealed record ApplicationIdentity(string ApplicationName, string ProcessName, long WindowId,
    string? ExecutablePath = null, string? PackageFamilyName = null, string? GameInstallationId = null,
    string? GameName = null, string? GameResolutionNote = null);
public sealed record ActivitySample(ApplicationIdentity? Application, TimeSpan IdleTime, bool IsSessionLocked = false,
    string? ControllerStatus = null);
public sealed record ActivityRecord(
    Guid Id, string DeviceId, string LocalSessionId, string ApplicationName, string ProcessName,
    DateTimeOffset StartUtc, DateTimeOffset EndUtc, TimeSpan ActiveDuration, ActivityStatus Status,
    string? GameInstallationId = null);
public sealed record ApplicationUsage(string ApplicationName, string ProcessName, TimeSpan Duration);

public sealed record TrackerOptions
{
    public TimeSpan IdleThreshold { get; init; } = TimeSpan.FromSeconds(180);
    public TimeSpan MaximumSampleGap { get; init; } = TimeSpan.FromSeconds(5);

    public void Validate()
    {
        if (IdleThreshold < TimeSpan.FromSeconds(5) || IdleThreshold > TimeSpan.FromHours(1))
            throw new ArgumentOutOfRangeException(nameof(IdleThreshold));
        if (MaximumSampleGap <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(MaximumSampleGap));
    }
}
