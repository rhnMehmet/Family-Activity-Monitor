using System.Security.Cryptography;
using FamilyMonitor.Core;
using FamilyMonitor.Core.Browsers;
using FamilyMonitor.Infrastructure;
using FamilyMonitor.Infrastructure.Browsers;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FamilyMonitor.Tests.Browsers;

public sealed class BrowserStorageTests:IDisposable
{
    [Fact]
    public void BrowserIdleLockAndSuspendDoNotEarnTimeAndFreshFocusResumesAfterEachBoundary()
    {
        var persistent=Setup();var clock=new FakeClock{UtcNow=Start};var source=new FakeSource{Application=new("Chrome","chrome",1)};
        using var buffer=new BufferedActivityStore(persistent,clock,startWorker:false,browserStore:new(Database));
        var tracker=new ActivityTracker(clock,source,buffer,"d","s",new(){IdleThreshold=TimeSpan.FromSeconds(5)});
        using var runtime=new BrowserRuntime(tracker,clock,buffer);runtime.Configure(BrowserCoreTests.Options());
        var client=Guid.NewGuid();long sequence=0;runtime.Correlator.Register(client,BrowserKind.Chrome,BrowserIdentity.ChromiumExtensionId);
        tracker.GrantConsent();tracker.Start();Acknowledge();Use(3);
        for(var i=1;i<=15;i++){source.IdleTime=TimeSpan.FromSeconds(i);clock.Advance(1);tracker.Tick();}
        Assert.Equal(TrackingState.Idle,tracker.State);Assert.Equal(3,Total());Assert.False(runtime.Correlator.Policy(client).Enabled);
        source.IdleTime=TimeSpan.Zero;Use(1);Acknowledge();Use(2);Assert.Equal(5,Total());
        tracker.SetLocked(true);clock.Advance(30);tracker.Tick();Assert.Equal(5,Total());Assert.Null(runtime.Correlator.Binding(clock.Elapsed));
        tracker.SetLocked(false);Acknowledge();Use(2);Assert.Equal(7,Total());
        tracker.SetSuspended(true);clock.Advance(100);tracker.Tick();Assert.Equal(7,Total());Assert.Null(runtime.Correlator.Binding(clock.Elapsed));
        tracker.SetSuspended(false);Acknowledge();Use(2);Assert.Equal(9,Total());
        Assert.Equal(9,buffer.Read(Start,Start.AddDays(1)).Sum(v=>v.ActiveDuration.TotalSeconds));
        void Use(int seconds){for(var i=0;i<seconds;i++){clock.Advance(1);tracker.Tick();}}
        double Total()=>buffer.GetBrowserUsage(Start,Start.AddDays(1)).Sum(v=>v.Duration.TotalSeconds);
        void Acknowledge()=>Assert.True(runtime.Receive(client,NativeMessageProtocol.Encode(new{version=1,type="snapshot",seq=++sequence,epoch=runtime.Correlator.CurrentEpoch,windowId=1,tabId=1,focused=true,@private=false,page=new BrowserPage("example.com")})));
    }
    [Fact]
    public void DelayedRetentionHidesExpiredRowsAndCannotResurrectOldQueuedPortions()
    {
        var persistent=Setup();var allowMaintenance=false;var browser=new SqliteBrowserActivityStore(Database);
        using var buffer=new BufferedActivityStore(persistent,new FakeClock(),mayMaintain:()=>allowMaintenance,startWorker:false,browserStore:browser);
        var app=App();var visit=Visit(app);buffer.Save(app);buffer.SaveBrowser(visit);buffer.Flush();
        buffer.SaveBrowser(visit with{EndUtc=Start.AddSeconds(12)});
        buffer.PruneBrowser(Start.AddSeconds(4));
        Assert.Equal(Start.AddSeconds(4),Assert.Single(buffer.ReadBrowser(Start,Start.AddDays(1))).StartUtc);
        Assert.Equal(8,Assert.Single(buffer.GetBrowserUsage(Start,Start.AddDays(1))).Duration.TotalSeconds);
        buffer.Flush();Assert.Equal(8,Assert.Single(browser.GetBrowserUsage(Start,Start.AddDays(1))).Duration.TotalSeconds);
        buffer.PruneBrowser(Start.AddSeconds(20));Assert.Empty(buffer.GetBrowserUsage(Start,Start.AddDays(1)));
        buffer.SaveBrowser(visit);buffer.Flush();Assert.Empty(buffer.ReadBrowser(Start,Start.AddDays(1)));
        allowMaintenance=true;buffer.Flush();Assert.Empty(browser.ReadBrowser(Start,Start.AddDays(1)));Assert.Single(persistent.Read(Start,Start.AddDays(1)));
    }
    [Fact]
    public void InFlightOldPolicySnapshotIsIgnoredWithoutDisconnectAndCurrentPolicyCanResume()
    {
        var persistent=Setup();var clock=new FakeClock{UtcNow=Start};var source=new FakeSource{Application=new("Chrome","chrome",1)};
        using var buffer=new BufferedActivityStore(persistent,clock,startWorker:false,browserStore:new(Database));
        var tracker=new ActivityTracker(clock,source,buffer,"d","s");using var runtime=new BrowserRuntime(tracker,clock,buffer);runtime.Configure(BrowserCoreTests.Options());
        var client=Guid.NewGuid();runtime.Correlator.Register(client,BrowserKind.Chrome,BrowserIdentity.ChromiumExtensionId);tracker.GrantConsent();tracker.Start();
        var oldEpoch=runtime.Correlator.CurrentEpoch;tracker.Pause();
        Assert.True(runtime.Receive(client,Snapshot(1,oldEpoch)));Assert.Null(runtime.Correlator.Binding(clock.Elapsed));
        tracker.Resume();Assert.True(runtime.Receive(client,Snapshot(2,oldEpoch)));Assert.Null(runtime.Correlator.Binding(clock.Elapsed));
        Assert.True(runtime.Receive(client,Snapshot(3,runtime.Correlator.CurrentEpoch)));Assert.NotNull(runtime.Correlator.Binding(clock.Elapsed));
        Assert.False(runtime.Receive(client,Snapshot(3,runtime.Correlator.CurrentEpoch)));
        byte[] Snapshot(int seq,string epoch)=>NativeMessageProtocol.Encode(new{version=1,type="snapshot",seq,epoch,windowId=1,tabId=1,focused=true,@private=false,page=new BrowserPage("example.com")});
    }
    [Fact]
    public void LongPauseHeartbeatsDoNotFillQueueOrPreventResuming()
    {
        var persistent=Setup();var clock=new FakeClock{UtcNow=Start};var source=new FakeSource{Application=new("Chrome","chrome",1)};
        using var buffer=new BufferedActivityStore(persistent,clock,startWorker:false,browserStore:new(Database));
        var tracker=new ActivityTracker(clock,source,buffer,"d","s");using var runtime=new BrowserRuntime(tracker,clock,buffer);runtime.Configure(BrowserCoreTests.Options());
        var client=Guid.NewGuid();runtime.Correlator.Register(client,BrowserKind.Chrome,BrowserIdentity.ChromiumExtensionId);tracker.GrantConsent();tracker.Start();tracker.Pause();
        for(var seq=1;seq<=2000;seq++){clock.Advance(10);Assert.True(runtime.Receive(client,NativeMessageProtocol.Encode(new{version=1,type="heartbeat",seq})));}
        Assert.Null(runtime.LastError);tracker.Resume();Assert.True(runtime.Correlator.Policy(client).Enabled);
    }
    private readonly string root=Path.Combine(Path.GetTempPath(),"FamilyMonitor.BrowserTests",Guid.NewGuid().ToString("N"));
    private string Database=>Path.Combine(root,"activity.db");
    private static readonly DateTimeOffset Start=new(2026,11,8,9,0,0,TimeSpan.Zero);
    private static ActivityRecord App(int seconds=10)=>new(Guid.NewGuid(),"test","test","Chrome","chrome",Start,Start.AddSeconds(seconds),TimeSpan.FromSeconds(seconds),ActivityStatus.Active);
    private static BrowserVisit Visit(ActivityRecord app)=>new(Guid.NewGuid(),app.Id,BrowserKind.Chrome,"example.com",null,null,null,app.StartUtc,app.EndUtc,ActivityStatus.Active);
    private SqliteActivityStore Setup(){BrowserDatabaseUpgradeService.EnsureReady(Database,_=>throw new Exception("New temp database needs no approval."));return new(Database,true);}
    [Fact]
    public void MigrationDeclineLeavesVersionThreeBytesUnchanged()
    {
        DatabaseUpgradeService.EnsureReady(Database,_=>true);var before=SHA256.HashData(File.ReadAllBytes(Database));
        Assert.False(BrowserDatabaseUpgradeService.EnsureReady(Database,_=>false));Assert.Equal(before,SHA256.HashData(File.ReadAllBytes(Database)));
    }
    [Fact]
    public void MigrationPreservesOriginalRecordsAndVerifiedBackupCanRestoreSeparateLegacyCopy()
    {
        DatabaseUpgradeService.EnsureReady(Database,_=>true);var old=new SqliteActivityStore(Database,true);var app=App();old.Save(app);
        DatabaseUpgradePlan? plan=null;Assert.True(BrowserDatabaseUpgradeService.EnsureReady(Database,p=>{plan=p;return true;}));
        Assert.Equal(3,plan!.CurrentVersion);Assert.Equal(4,plan.TargetVersion);Assert.True(File.Exists(plan.BackupPath));
        var restore=Path.Combine(root,"restored.db");BrowserDatabaseUpgradeService.CopyVerified(plan.BackupPath,restore);
        var restored=new SqliteActivityStore(restore,true);Assert.Equal(app.Id,Assert.Single(restored.Read(Start,Start.AddDays(1))).Id);
        Assert.True(BrowserDatabaseUpgradeService.EnsureReady(Database,_=>throw new Exception("No repeated migration approval.")));
        Assert.Throws<IOException>(()=>BrowserDatabaseUpgradeService.CopyVerified(plan.BackupPath,restore));
    }
    [Fact]
    public void BrowserAndApplicationShareOneAtomicBatchAndRestartRecovery()
    {
        var persistent=Setup();var app=App();var visit=Visit(app);persistent.SaveBatch([app],[],[visit]);
        _=new SqliteActivityStore(Database,true);var row=Assert.Single(new SqliteBrowserActivityStore(Database).ReadBrowser(Start,Start.AddDays(1)));
        Assert.Equal(visit.Id,row.Id);Assert.Equal(ActivityStatus.Interrupted,row.Status);Assert.Equal(app.Id,row.ActivityId);
        var invalid=Visit(app) with{ActivityId=Guid.NewGuid()};Assert.Throws<SqliteException>(()=>persistent.SaveBatch([App()],[],[invalid]));
        Assert.Single(persistent.Read(Start,Start.AddDays(1)));
    }
    [Fact]
    public void BufferOverlayDoesNotDoubleCountAndBrowserDeletionCannotResurrectQueuedData()
    {
        var persistent=Setup();var browser=new SqliteBrowserActivityStore(Database);using var buffer=new BufferedActivityStore(persistent,new FakeClock(),startWorker:false,browserStore:browser);
        var app=App();var visit=Visit(app);buffer.Save(app);buffer.SaveBrowser(visit);buffer.Flush();
        app=app with{EndUtc=Start.AddSeconds(15),ActiveDuration=TimeSpan.FromSeconds(15)};visit=visit with{EndUtc=Start.AddSeconds(15)};
        buffer.Save(app);buffer.SaveBrowser(visit);Assert.Equal(15,Assert.Single(buffer.GetBrowserUsage(Start,Start.AddDays(1))).Duration.TotalSeconds);
        buffer.DeleteBrowser();buffer.Flush();Assert.Empty(browser.ReadBrowser(Start,Start.AddDays(1)));Assert.Single(persistent.Read(Start,Start.AddDays(1)));
    }
    [Fact]
    public void GeneralHistoryDeletionCascadesBrowserRecordsAndClearsBothQueues()
    {
        var persistent=Setup();using var buffer=new BufferedActivityStore(persistent,new FakeClock(),startWorker:false,browserStore:new(Database));var app=App();
        buffer.Save(app);buffer.SaveBrowser(Visit(app));buffer.Flush();buffer.SaveBrowser(Visit(app));
        buffer.DeleteAll();buffer.Flush();Assert.Empty(buffer.ReadBrowser(Start,Start.AddDays(1)));Assert.Empty(buffer.Read(Start,Start.AddDays(1)));
    }
    [Fact]
    public void RetentionClipsCrossBoundaryVisitAndNeverDeletesGeneralUsage()
    {
        var persistent=Setup();var app=App();var browser=new SqliteBrowserActivityStore(Database);persistent.SaveBatch([app],[],[Visit(app)]);
        browser.PruneBrowser(Start.AddSeconds(4));Assert.Equal(6,Assert.Single(browser.GetBrowserUsage(Start,Start.AddDays(1))).Duration.TotalSeconds);
        browser.PruneBrowser(Start.AddSeconds(20));Assert.Empty(browser.ReadBrowser(Start,Start.AddDays(1)));Assert.Single(persistent.Read(Start,Start.AddDays(1)));
    }
    [Fact]
    public void LargeHistoryIsBoundedAndBrowserFilterDoesNotMixFamilies()
    {
        var persistent=Setup();var app=App(20000);
        var rows=Enumerable.Range(0,10000).Select(i=>Visit(app) with{Id=Guid.NewGuid(),StartUtc=Start.AddSeconds(i),EndUtc=Start.AddSeconds(i+1),Browser=i%2==0?BrowserKind.Chrome:BrowserKind.Firefox,Status=ActivityStatus.Completed}).ToArray();
        persistent.SaveBatch([app],[],rows);var browser=new SqliteBrowserActivityStore(Database);
        Assert.Equal(500,browser.ReadBrowser(Start,Start.AddDays(1)).Count);
        Assert.All(browser.ReadBrowser(Start,Start.AddDays(1),BrowserKind.Firefox),v=>Assert.Equal(BrowserKind.Firefox,v.Browser));
        Assert.Equal(5000,Assert.Single(browser.GetBrowserUsage(Start,Start.AddDays(1),BrowserKind.Chrome)).Duration.TotalSeconds);
    }
    [Fact]
    public void ExistingSettingsDoNotAcquireNewBrowserOrStartupPermissions()
    {
        var settingsStore=new LocalSettingsStore(root);settingsStore.Save(new(){PrivacyVersion=1,ConsentGranted=true,AutomaticGameMode=true});
        var loaded=settingsStore.Load();Assert.False(loaded.Browsers.DomainTracking);Assert.False(loaded.Browsers.DetailedUrls);Assert.False(loaded.Browsers.SearchQueries);Assert.False(loaded.AutoStartEnabled);Assert.False(loaded.AutoTrackOnLogin);Assert.True(loaded.AutomaticGameMode);
    }
    [Fact]
    public void RuntimeWritesOnlyConfirmedBrowserSlicesAndNeverGameOrPausedTime()
    {
        var persistent=Setup();var clock=new FakeClock{UtcNow=Start};var source=new FakeSource{Application=new("Chrome","chrome",1)};
        using var buffer=new BufferedActivityStore(persistent,clock,startWorker:false,browserStore:new(Database));var tracker=new ActivityTracker(clock,source,buffer,"d","s",new(){IdleThreshold=TimeSpan.FromSeconds(5)});
        using var runtime=new BrowserRuntime(tracker,clock,buffer);runtime.Configure(BrowserCoreTests.Options());var id=Guid.NewGuid();runtime.Correlator.Register(id,BrowserKind.Chrome,BrowserIdentity.ChromiumExtensionId);
        tracker.GrantConsent();tracker.Start();Acknowledge();Use(3);Assert.Equal(3,Assert.Single(buffer.GetBrowserUsage(Start,Start.AddDays(1))).Duration.TotalSeconds);
        tracker.Pause();clock.Advance(100);tracker.Tick();Assert.Equal(3,Assert.Single(buffer.GetBrowserUsage(Start,Start.AddDays(1))).Duration.TotalSeconds);
        source.Application=new("Game","game",2);tracker.Resume();Use(10);Assert.Equal(3,Assert.Single(buffer.GetBrowserUsage(Start,Start.AddDays(1))).Duration.TotalSeconds);
        source.Application=new("Chrome","chrome",1);Use(1);Acknowledge();Use(2);Assert.Equal(5,buffer.GetBrowserUsage(Start,Start.AddDays(1)).Sum(v=>v.Duration.TotalSeconds));
        tracker.RevokeConsent();Use(10);Assert.Equal(5,buffer.GetBrowserUsage(Start,Start.AddDays(1)).Sum(v=>v.Duration.TotalSeconds));
        void Acknowledge(){var epoch=runtime.Correlator.Policy(id).Epoch;Assert.True(runtime.Receive(id,NativeMessageProtocol.Encode(new{version=1,type="snapshot",seq=clock.Elapsed.Ticks+1,epoch,windowId=1,tabId=1,focused=true,@private=false,page=new BrowserPage("example.com")})));}
        void Use(int seconds){for(var i=0;i<seconds;i++){clock.Advance(1);tracker.Tick();}}
    }
    public void Dispose(){SqliteConnection.ClearAllPools();if(Directory.Exists(root))Directory.Delete(root,true);}
}
