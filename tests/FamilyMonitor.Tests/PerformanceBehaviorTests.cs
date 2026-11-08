using FamilyMonitor.Core;
using FamilyMonitor.Core.Games;
using FamilyMonitor.Core.Input;
using FamilyMonitor.Core.Performance;
using FamilyMonitor.Infrastructure.Windows;
using Xunit;

namespace FamilyMonitor.Tests;

public sealed class PerformanceBehaviorTests
{
    [Fact]
    public void SixtySecondsForegroundAndFortyFiveSecondsConfirmedRemainSeparate()
    {
        var clock=new FakeClock();var source=GameSource();var active=new MemoryStore();var presence=new PresenceStore();
        var tracker=Create(clock,source,active,presence);
        for(var i=1;i<=60;i++){clock.Advance(1);source.IdleTime=TimeSpan.FromSeconds(Math.Max(0,i-45));tracker.Tick();}
        Assert.Equal(TrackingState.Idle,tracker.State);Assert.True(tracker.HasForegroundGame);
        tracker.Stop();Assert.Equal(45,active.TotalSeconds);Assert.Equal(60,presence.Seconds);
    }

    [Fact]
    public void PausingStopsBothMetricsAndResumeDoesNotBackdate()
    {
        var clock=new FakeClock();var source=GameSource();var active=new MemoryStore();var presence=new PresenceStore();var tracker=Create(clock,source,active,presence);
        Use(2);tracker.Pause();var reads=source.Reads;clock.Advance(100);tracker.Tick();
        Assert.Equal(reads,source.Reads);Assert.Equal(2,presence.Seconds);Assert.Equal(2,active.TotalSeconds);
        tracker.Resume();Use(3);tracker.Stop();Assert.Equal(5,presence.Seconds);Assert.Equal(5,active.TotalSeconds);
        void Use(int seconds){for(var i=0;i<seconds;i++){clock.Advance(1);tracker.Tick();}}
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LockAndSleepStopBothCounters(bool sleep)
    {
        var clock=new FakeClock();var source=GameSource();var active=new MemoryStore();var presence=new PresenceStore();var tracker=Create(clock,source,active,presence);
        clock.Advance(2);tracker.Tick();if(sleep)tracker.SetSuspended(true);else tracker.SetLocked(true);
        clock.Advance(600);tracker.Tick();Assert.Equal(2,presence.Seconds);
        if(sleep)tracker.SetSuspended(false);else tracker.SetLocked(false);
        clock.Advance(3);tracker.Tick();tracker.Stop();Assert.Equal(5,presence.Seconds);Assert.Equal(5,active.TotalSeconds);
    }

    [Fact]
    public void UnobservedGapIsExcludedFromPresenceAndIntervalsDoNotOverlap()
    {
        var clock=new FakeClock();var source=GameSource();var active=new MemoryStore();var presence=new PresenceStore();var tracker=Create(clock,source,active,presence);
        clock.Advance(2);tracker.Tick();clock.Advance(100);tracker.Tick();clock.Advance(2);tracker.Tick();tracker.Stop();
        Assert.Equal(4,presence.Seconds);Assert.Equal(4,active.TotalSeconds);
        var rows=presence.Rows.Values.OrderBy(x=>x.StartUtc).ToArray();Assert.Equal(2,rows.Length);Assert.True(rows[0].EndUtc<=rows[1].StartUtc);
    }

    [Fact]
    public void ConsentWithdrawalStillStopsWhenForegroundWriteFails()
    {
        var clock=new FakeClock();var source=GameSource();var active=new MemoryStore();var presence=new PresenceStore();var tracker=Create(clock,source,active,presence);
        clock.Advance(1);tracker.Tick();presence.Fail=true;tracker.RevokeConsent();
        Assert.False(tracker.HasConsent);Assert.False(tracker.IsEnabled);Assert.False(tracker.Start());
    }

    [Fact]
    public void ClearDiscardsCurrentPresenceSoItCannotResurrect()
    {
        var clock=new FakeClock();var source=GameSource();var active=new MemoryStore();var presence=new PresenceStore();var tracker=Create(clock,source,active,presence);
        clock.Advance(2);tracker.Tick();tracker.ClearHistory();presence.Rows.Clear();clock.Advance(5);tracker.Stop();
        Assert.Empty(active.Records);Assert.Empty(presence.Rows);
    }

    [Fact]
    public void ModeRetainsConservativeDeferralDuringPauseAndStaggersMaintenance()
    {
        var clock=new FakeClock();var mode=new GameModePolicy(clock);
        mode.Observe(true,false,false,false,false);Assert.False(mode.IsActive);
        mode.Observe(true,false,false,false,true);Assert.True(mode.IsActive);Assert.False(mode.MayRunMaintenance);Assert.Equal(15,mode.UiRefreshInterval.TotalSeconds);
        mode.Observe(true,true,false,false,false);Assert.True(mode.IsActive);
        mode.Observe(true,false,false,false,false);Assert.False(mode.IsActive);Assert.False(mode.MayRunMaintenance);
        clock.Advance(44);Assert.False(mode.MayRunMaintenance);clock.Advance(1);Assert.True(mode.MayRunMaintenance);
    }

    [Theory]
    [InlineData(false,false,false,false)]
    [InlineData(true,false,true,false)]
    [InlineData(true,false,false,true)]
    public void StopLockSleepLeaveGameMode(bool tracking,bool paused,bool locked,bool sleeping)
    {
        var mode=new GameModePolicy(new FakeClock());mode.Observe(true,false,false,false,true);
        mode.Observe(tracking,paused,locked,sleeping,true);Assert.False(mode.IsActive);
    }

    [Fact]
    public void DisablingAutomaticModeDoesNotStopActivityTracking()
    {
        var mode=new GameModePolicy(new FakeClock()){Enabled=false};mode.Observe(true,false,false,false,true);Assert.False(mode.IsActive);
        var f=new TrackerFixture();f.Use(3);Assert.Equal(3,f.Store.TotalSeconds);Assert.True(f.Tracker.IsEnabled);
    }

    [Theory]
    [InlineData(false,false)]
    [InlineData(true,true)]
    public void ControllersAreNotPolledForUntrustedOrLockedWindows(bool game,bool locked)
    {
        var source=game?GameSource():new FakeSource();source.Locked=locked;var pad=new Pad();
        var sample=new GamepadActivitySource(source,pad,new FakeClock()).Read();Assert.Equal(0,pad.Reads);Assert.Null(sample.ControllerStatus);
    }

    [Fact]
    public void ObservedPadInputResetsIdleThenNeutralTimeAccumulates()
    {
        var clock=new FakeClock();var source=GameSource();source.IdleTime=TimeSpan.FromMinutes(20);var pad=new Pad{Evidence=new(true,true)};
        var combined=new GamepadActivitySource(source,pad,clock);Assert.Equal(TimeSpan.Zero,combined.Read().IdleTime);
        pad.Evidence=new(true,false);clock.Advance(15);Assert.Equal(TimeSpan.FromSeconds(15),combined.Read().IdleTime);
        source.Application=new("Editor","editor",3);combined.Read();source.Application=GameSource().Application;
        Assert.Equal(source.IdleTime,combined.Read().IdleTime);
    }

    [Fact]
    public void UnsupportedControllerCannotInventActiveTime()
    {
        var source=GameSource();source.IdleTime=TimeSpan.FromSeconds(200);
        var sample=new GamepadActivitySource(source,new Pad{Evidence=new(true,true,false)},new FakeClock()).Read();
        Assert.Equal(source.IdleTime,sample.IdleTime);Assert.Contains("kullanılamıyor",sample.ControllerStatus);
    }

    [Theory]
    [InlineData(0,0,0,false)]
    [InlineData(1000,0,0,false)]
    [InlineData(8000,0,0,true)]
    [InlineData(0,1,0,true)]
    [InlineData(0,0,31,true)]
    public void XInputUsesDeadZonesAndOnlyEmitsActivityBooleans(short axis,ushort buttons,byte trigger,bool expected)
    {
        var api=new PadApi{Sample=new(true,buttons,trigger,0,axis,0,0,0)};
        Assert.Equal(expected,new XInputActivityProvider(api,new FakeClock()).Read().Active);
    }

    [Fact]
    public void DisconnectedSlotsUseFiveSecondRetryAndMissingApiIsCached()
    {
        var clock=new FakeClock();var api=new PadApi();var provider=new XInputActivityProvider(api,clock);
        provider.Read();provider.Read();Assert.Equal(4,api.Reads);clock.Advance(5);provider.Read();Assert.Equal(8,api.Reads);
        var absent=new PadApi{Missing=true};provider=new(absent,clock);
        Assert.False(provider.Read().Supported);Assert.False(provider.Read().Supported);Assert.Equal(1,absent.Reads);
    }

    [Fact]
    public void ForegroundMetadataCacheStillReadsIdleAndInvalidatesPermissionAndWindow()
    {
        var clock=new FakeClock();var api=new WindowApi();var permitted=true;var source=new WindowActivitySource(api,()=>permitted,clock);
        for(var i=0;i<1000;i++)source.Read();Assert.Equal(1,api.MetadataReads);Assert.Equal(1000,api.InputReads);
        clock.Advance(5);source.Read();Assert.Equal(2,api.MetadataReads);
        permitted=false;Assert.Null(source.Read().Application!.ExecutablePath);Assert.Equal(2,api.PathReads);
        api.Window=2;source.Read();Assert.Equal(4,api.MetadataReads);
    }

    private static FakeSource GameSource()=>new(){Application=new("Game","runtime",1,GameInstallationId:"game",GameName:"Game")};
    private static ActivityTracker Create(FakeClock clock,FakeSource source,MemoryStore store,PresenceStore presence)
    {var tracker=new ActivityTracker(clock,source,store,"d","s",new(){IdleThreshold=TimeSpan.FromSeconds(5)},presence);tracker.GrantConsent();tracker.Start();return tracker;}
    private sealed class Pad:IControllerActivityProvider
    {public int Reads;public ControllerEvidence Evidence=new(false,false);public ControllerEvidence Read(){Reads++;return Evidence;}}
    private sealed class PadApi:IXInputApi
    {public int Reads;public bool Missing;public ControllerSample Sample;public ControllerSample Read(uint index){Reads++;if(Missing)throw new DllNotFoundException();return Sample;}}
    private sealed class WindowApi:IWindowsActivityApi
    {
        public int MetadataReads,InputReads,PathReads;public nint Window=1;
        public bool IsInputDesktopAvailable()=>true;public TimeSpan GetIdleTime(){InputReads++;return TimeSpan.Zero;}
        public nint GetForegroundWindow()=>Window;public uint GetProcessId(nint window)=>123;
        public string GetProcessName(uint id){MetadataReads++;return "runtime";}
        public string GetExecutablePath(uint id){PathReads++;return @"C:\Synthetic\runtime.exe";}
    }
    private sealed class PresenceStore:IGameForegroundStore
    {
        public Dictionary<Guid,GameForegroundPeriod> Rows=[];public bool Fail;
        public double Seconds=>Rows.Values.Sum(x=>x.Duration.TotalSeconds);
        public void SaveForeground(GameForegroundPeriod p){if(Fail)throw new IOException();Rows[p.Id]=p;}
        public IReadOnlyList<GameForegroundPeriod> ReadForeground(DateTimeOffset from,DateTimeOffset to)=>Rows.Values.Where(x=>x.EndUtc>from&&x.StartUtc<to).ToArray();
        public IReadOnlyDictionary<string,TimeSpan> GetForegroundDurations(DateTimeOffset from,DateTimeOffset to)=>ReadForeground(from,to).GroupBy(x=>x.InstallationId).ToDictionary(x=>x.Key,x=>TimeSpan.FromTicks(x.Sum(p=>p.Duration.Ticks)));
    }
}
