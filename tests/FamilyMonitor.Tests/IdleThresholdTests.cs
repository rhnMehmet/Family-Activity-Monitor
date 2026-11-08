using FamilyMonitor.Core;
using FamilyMonitor.Infrastructure;
using Xunit;

namespace FamilyMonitor.Tests;

public sealed class IdleThresholdTests
{
    [Fact]
    public void DefaultThresholdIs180Seconds()
    {
        Assert.Equal(180, new TrackerOptions().IdleThreshold.TotalSeconds);
        Assert.Equal(180, new LocalSettings().IdleThresholdSeconds);
        Assert.Equal(180, new TrackerFixture().Tracker.IdleThreshold.TotalSeconds);
    }

    [Fact]
    public void FiveSecondThresholdExcludesFifteenSecondsWithoutInputAndRecovers()
    {
        var f = new TrackerFixture();
        f.Tracker.UpdateOptions(new TrackerOptions { IdleThreshold = TimeSpan.FromSeconds(5) });
        f.Use(3);
        f.Idle(4);
        Assert.Equal(TrackingState.Active, f.Tracker.State);
        f.Idle(1);
        Assert.Equal(TrackingState.Idle, f.Tracker.State);
        f.Idle(10);
        Assert.Equal(TimeSpan.FromSeconds(15), f.Tracker.LastObservedIdleTime);
        Assert.Equal(3, f.Store.TotalSeconds);
        f.Use(1);
        Assert.Equal(TrackingState.Active, f.Tracker.State);
        Assert.Equal(TimeSpan.Zero, f.Tracker.LastObservedIdleTime);
        f.Use(3);
        Assert.Equal(6, f.Store.TotalSeconds);
        var rows = f.Store.Records.Values.OrderBy(x => x.StartUtc).ToArray();
        Assert.Equal(2, rows.Length);
        Assert.True(rows[0].EndUtc <= rows[1].StartUtc);
        Assert.All(rows, row => Assert.True(row.ActiveDuration > TimeSpan.Zero));
    }

    [Fact]
    public void LoweringThresholdAppliesToCurrentIdleSampleImmediately()
    {
        var f = new TrackerFixture();
        f.Use(3);
        f.Idle(6);
        Assert.Equal(TrackingState.Active, f.Tracker.State);
        f.Tracker.UpdateOptions(new TrackerOptions { IdleThreshold = TimeSpan.FromSeconds(5) });
        Assert.Equal(TrackingState.Idle, f.Tracker.State);
        Assert.Equal(TimeSpan.FromSeconds(5), f.Tracker.IdleThreshold);
        Assert.Equal(3, f.Store.TotalSeconds);
    }

    [Fact]
    public void IncreasingThresholdDoesNotRestoreAlreadyDiscardedIdleTime()
    {
        var f = new TrackerFixture();
        f.Tracker.UpdateOptions(new TrackerOptions { IdleThreshold = TimeSpan.FromSeconds(5) });
        f.Use(3);
        f.Idle(15);
        f.Tracker.UpdateOptions(new TrackerOptions { IdleThreshold = TimeSpan.FromSeconds(180) });
        f.Use(3);
        Assert.Equal(6, f.Store.TotalSeconds);
    }

    [Fact]
    public void InvalidThresholdLeavesCurrentTrackingOptionsUnchanged()
    {
        var f = new TrackerFixture();
        Assert.Throws<ArgumentOutOfRangeException>(() => f.Tracker.UpdateOptions(
            new TrackerOptions { IdleThreshold = TimeSpan.FromSeconds(4) }));
        Assert.Equal(TimeSpan.FromSeconds(180), f.Tracker.IdleThreshold);
        Assert.True(f.Tracker.IsEnabled);
    }

    [Fact]
    public void ThresholdChangeWhilePausedDoesNotReadSourceOrResumeTracking()
    {
        var f = new TrackerFixture();
        f.Tracker.Pause();
        var reads = f.Source.Reads;
        f.Tracker.UpdateOptions(new TrackerOptions { IdleThreshold = TimeSpan.FromSeconds(5) });
        Assert.Equal(reads, f.Source.Reads);
        Assert.Equal(TrackingState.Paused, f.Tracker.State);
        f.Tracker.Resume();
        f.Idle(5);
        Assert.Equal(TrackingState.Idle, f.Tracker.State);
    }
}
