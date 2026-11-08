using FamilyMonitor.Core;
using Xunit;

namespace FamilyMonitor.Tests;

public sealed class ActivityTrackerTests
{
    [Fact]
    public void ConsentIsRequiredBeforeAnySourceRead()
    {
        var f = new TrackerFixture(start: false);
        Assert.False(f.Tracker.Start());
        f.Use(10);
        Assert.Equal(0, f.Source.Reads);
        Assert.Empty(f.Store.Records);
        Assert.Equal(TrackingState.Stopped, f.Tracker.State);
    }

    [Fact]
    public void GrantingConsentDoesNotAutomaticallyStart()
    {
        var f = new TrackerFixture(start: false);
        f.Tracker.GrantConsent();
        f.Tracker.Tick();
        Assert.False(f.Tracker.IsEnabled);
        Assert.Equal(0, f.Source.Reads);
    }

    [Fact]
    public void OneForegroundApplicationAccumulatesConfirmedTime()
    {
        var f = new TrackerFixture();
        f.Use(12);
        f.Tracker.Stop();
        var record = Assert.Single(f.Store.Records.Values);
        Assert.Equal(12, record.ActiveDuration.TotalSeconds);
        Assert.Equal(record.EndUtc - record.StartUtc, record.ActiveDuration);
        Assert.Equal(ActivityStatus.Completed, record.Status);
    }

    [Fact]
    public void ApplicationSwitchCompletesPreviousPeriodWithoutOverlap()
    {
        var f = new TrackerFixture();
        f.Use(5);
        f.Source.Application = new("Browser", "chrome", 2);
        f.Use(1);
        f.Use(3);
        var records = f.Store.Records.Values.OrderBy(x => x.StartUtc).ToArray();
        Assert.Equal(2, records.Length);
        Assert.Equal(6, records[0].ActiveDuration.TotalSeconds);
        Assert.Equal(3, records[1].ActiveDuration.TotalSeconds);
        Assert.Equal(records[0].EndUtc, records[1].StartUtc);
    }

    [Fact]
    public void DifferentWindowsOfSameProcessCreateSeparatePeriods()
    {
        var f = new TrackerFixture();
        f.Use(2);
        f.Source.Application = new("Editor", "editor", 99);
        f.Use(2);
        Assert.Equal(2, f.Store.Records.Count);
        Assert.Equal(4, f.Store.TotalSeconds);
    }

    [Fact]
    public void IdleThresholdDiscardsEntireUnconfirmedTail()
    {
        var f = new TrackerFixture();
        f.Use(10);
        f.Idle(179);
        Assert.Equal(TrackingState.Active, f.Tracker.State);
        Assert.Equal(10, f.Store.TotalSeconds);
        f.Idle(1);
        Assert.Equal(TrackingState.Idle, f.Tracker.State);
        Assert.Equal(10, f.Store.TotalSeconds);
        f.Idle(300);
        Assert.Equal(10, f.Store.TotalSeconds);
    }

    [Fact]
    public void ReturningFromIdleStartsAtLatestInputNotIdleStart()
    {
        var f = new TrackerFixture();
        f.Use(10);
        f.Idle(190);
        f.Use(4);
        Assert.Equal(TrackingState.Active, f.Tracker.State);
        Assert.Equal(13, f.Store.TotalSeconds); // First returning sample starts a fresh period.
        var rows = f.Store.Records.Values.OrderBy(x => x.StartUtc).ToArray();
        Assert.Equal(2, rows.Length);
        Assert.True(rows[1].StartUtc - rows[0].EndUtc >= TimeSpan.FromSeconds(190));
    }

    [Fact]
    public void ReturnAtThresholdBetweenSamplesDoesNotConfirmIdleGap()
    {
        var f = new TrackerFixture();
        f.Use(5);
        f.Idle(179);
        f.Use(1); // Exactly 180 seconds between inputs, with no sampled Idle state.
        f.Use(2);
        Assert.Equal(7, f.Store.TotalSeconds);
        var rows = f.Store.Records.Values.OrderBy(x => x.StartUtc).ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Equal(TimeSpan.FromSeconds(180), rows[1].StartUtc - rows[0].EndUtc);
    }

    [Fact]
    public void InputWithinThresholdConfirmsReadingInterval()
    {
        var f = new TrackerFixture();
        f.Use(5);
        f.Idle(20);
        Assert.Equal(5, f.Store.TotalSeconds);
        f.Use(1);
        Assert.Equal(26, f.Store.TotalSeconds);
    }

    [Fact]
    public void LockedSessionDoesNotReadOrAccumulateUntilUnlock()
    {
        var f = new TrackerFixture();
        f.Use(5);
        f.Tracker.SetLocked(true);
        var reads = f.Source.Reads;
        f.Use(60);
        Assert.Equal(reads, f.Source.Reads);
        Assert.Equal(TrackingState.Locked, f.Tracker.State);
        Assert.Equal(5, f.Store.TotalSeconds);
        f.Tracker.SetLocked(false);
        f.Use(5);
        Assert.Equal(10, f.Store.TotalSeconds);
    }

    [Fact]
    public void StartingWhileDesktopIsLockedCreatesNoActivity()
    {
        var f = new TrackerFixture(start: false);
        f.Source.Locked = true;
        f.Tracker.GrantConsent();
        f.Tracker.Start();
        f.Use(30);
        Assert.Equal(TrackingState.Locked, f.Tracker.State);
        Assert.Empty(f.Store.Records);
        f.Source.Locked = false;
        f.Use(3);
        Assert.Equal(2, f.Store.TotalSeconds);
    }

    [Fact]
    public void SuspendAndResumeExcludeSleepTime()
    {
        var f = new TrackerFixture();
        f.Use(4);
        f.Tracker.SetSuspended(true);
        f.Clock.Advance(7200);
        f.Tracker.Tick();
        Assert.Equal(TrackingState.Suspended, f.Tracker.State);
        f.Tracker.SetSuspended(false);
        f.Use(6);
        Assert.Equal(10, f.Store.TotalSeconds);
    }

    [Fact]
    public void MissedPowerEventOrLongSampleGapDoesNotAttributeUnknownTime()
    {
        var f = new TrackerFixture();
        f.Use(4);
        f.Clock.Advance(600);
        f.Tracker.Tick();
        f.Use(3);
        Assert.Equal(7, f.Store.TotalSeconds);
    }

    [Fact]
    public void WallClockChangesCannotProduceNegativeOrOverlappingPeriods()
    {
        var f = new TrackerFixture();
        f.Use(5);
        f.Clock.UtcNow = f.Clock.UtcNow.AddHours(-10);
        f.Source.Application = new("Second", "second", 2);
        f.Use(5);
        f.Clock.UtcNow = f.Clock.UtcNow.AddDays(10);
        f.Source.Application = new("Third", "third", 3);
        f.Use(5);
        var rows = f.Store.Records.Values.OrderBy(x => x.StartUtc).ToArray();
        Assert.Equal(15, f.Store.TotalSeconds);
        Assert.All(rows, row => Assert.True(row.ActiveDuration > TimeSpan.Zero));
        Assert.True(rows[0].EndUtc <= rows[1].StartUtc);
        Assert.True(rows[1].EndUtc <= rows[2].StartUtc);
    }

    [Fact]
    public void NonMonotonicFakeClockIsClamped()
    {
        var f = new TrackerFixture();
        f.Use(5);
        f.Clock.Elapsed -= TimeSpan.FromSeconds(2);
        f.Tracker.Tick();
        Assert.Equal(5, f.Store.TotalSeconds);
        Assert.Equal(TrackingState.Active, f.Tracker.State);
    }

    [Fact]
    public void PauseResumeAndStopRespectExplicitControls()
    {
        var f = new TrackerFixture();
        f.Use(4);
        f.Tracker.Pause();
        f.Use(20);
        Assert.Equal(TrackingState.Paused, f.Tracker.State);
        Assert.Equal(4, f.Store.TotalSeconds);
        f.Tracker.Resume();
        f.Use(6);
        f.Tracker.Stop();
        f.Tracker.Resume();
        f.Use(20);
        Assert.Equal(10, f.Store.TotalSeconds);
        Assert.False(f.Tracker.IsEnabled);
    }

    [Fact]
    public void RevokingConsentPreventsReadsAndNewWritesImmediately()
    {
        var f = new TrackerFixture();
        f.Use(5);
        var writes = f.Store.Writes;
        var reads = f.Source.Reads;
        f.Tracker.RevokeConsent();
        f.Tracker.Resume();
        Assert.False(f.Tracker.Start());
        f.Use(30);
        Assert.Equal(writes, f.Store.Writes);
        Assert.Equal(reads, f.Source.Reads);
        Assert.Equal(5, f.Store.TotalSeconds);
    }

    [Fact]
    public void ClearHistoryStopsAndCannotResurrectCurrentPeriod()
    {
        var f = new TrackerFixture();
        f.Use(5);
        f.Tracker.ClearHistory();
        f.Use(20);
        f.Tracker.Stop();
        Assert.Empty(f.Store.Records);
        f.Tracker.Start();
        f.Use(2);
        Assert.Equal(2, f.Store.TotalSeconds);
    }

    [Fact]
    public void ProviderErrorStopsSafelyAndRequiresExplicitRestart()
    {
        var f = new TrackerFixture();
        f.Use(3);
        f.Source.Throw = true;
        f.Use(1);
        Assert.Equal(TrackingState.Faulted, f.Tracker.State);
        Assert.False(f.Tracker.IsEnabled);
        var reads = f.Source.Reads;
        f.Use(10);
        Assert.Equal(reads, f.Source.Reads);
        Assert.Equal(3, f.Store.TotalSeconds);
        f.Source.Throw = false;
        Assert.True(f.Tracker.Start());
        f.Use(2);
        Assert.Equal(5, f.Store.TotalSeconds);
    }

    [Fact]
    public void UnavailableWindowDoesNotBridgeUnknownPeriod()
    {
        var f = new TrackerFixture();
        f.Use(3);
        f.Source.Application = null;
        f.Use(10);
        Assert.Equal(TrackingState.Unavailable, f.Tracker.State);
        f.Source.Application = new("Editor", "editor", 1);
        f.Use(4);
        Assert.Equal(6, f.Store.TotalSeconds);
    }

    [Fact]
    public void StorageFailureStopsCollection()
    {
        var f = new TrackerFixture();
        f.Store.ThrowOnSave = true;
        f.Use(1);
        Assert.Equal(TrackingState.Faulted, f.Tracker.State);
        Assert.False(f.Tracker.IsEnabled);
        f.Tracker.RevokeConsent();
        Assert.False(f.Tracker.HasConsent);
    }

    [Fact]
    public void PauseIsPreservedAcrossLockAndSleep()
    {
        var f = new TrackerFixture();
        f.Use(3);
        f.Tracker.Pause();
        f.Tracker.SetLocked(true);
        f.Tracker.SetSuspended(true);
        f.Clock.Advance(60);
        f.Tracker.SetSuspended(false);
        f.Tracker.SetLocked(false);
        Assert.Equal(TrackingState.Paused, f.Tracker.State);
        f.Use(3);
        Assert.Equal(3, f.Store.TotalSeconds);
    }
}
