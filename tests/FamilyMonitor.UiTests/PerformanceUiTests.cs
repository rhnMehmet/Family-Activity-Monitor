using System.Diagnostics;
using FamilyMonitor.Agent;
using FamilyMonitor.Core;
using FamilyMonitor.Core.Performance;
using FamilyMonitor.Infrastructure;
using FamilyMonitor.Infrastructure.Games;
using FamilyMonitor.Core.Games;
using Microsoft.Data.Sqlite;
using FamilyMonitor.Tests;
using Xunit;

namespace FamilyMonitor.UiTests;

public sealed class PerformanceUiTests
{
    [Fact]
    public void StatisticsShowForegroundAndConfirmedTimeInDifferentColumns() => MainFormTests.OnUiThread(() =>
    {
        var root=Path.Combine(Path.GetTempPath(),"FamilyMonitor.UiPerformance",Guid.NewGuid().ToString("N"));var database=Path.Combine(root,"test.db");
        try
        {
            DatabaseUpgradeService.EnsureReady(database,_=>throw new Exception());var persistent=new SqliteActivityStore(database,true);var catalog=new SqliteGameCatalogRepository(database,true);
            var game=GameIdentity.Create("synthetic","Sentetik oyun",GamePlatform.Custom,"Test",root,Path.Combine(root,"game"),[],GameInstallationState.Installed);catalog.ApplyScan(new("Test",[game],[]));
            var clock=new FakeClock();using var buffer=new BufferedActivityStore(persistent,clock,new SqliteGameForegroundStore(database,true),startWorker:false);
            var source=new FakeSource{Application=new("Synthetic","runtime",1,GameInstallationId:game.Id,GameName:game.Name)};
            var tracker=new ActivityTracker(clock,source,buffer,"d","s",new(){IdleThreshold=TimeSpan.FromSeconds(5)},buffer);tracker.GrantConsent();tracker.Start();
            for(var i=1;i<=60;i++){clock.Advance(1);source.IdleTime=TimeSpan.FromSeconds(Math.Max(0,i-45));tracker.Tick();}buffer.Flush();
            using var discovery=new GameDiscoveryService([],catalog,clock);var settings=new LocalSettings{ConsentGranted=true,PrivacyVersion=1};
            using var form=new MainForm(tracker,buffer,new LocalSettingsStore(root),settings,new FakeDialogs(),false,discovery,catalog)
            {UseAsyncQueries=true,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-12000,-12000)};
            var tabs=MainFormTests.Descendants(form).OfType<TabControl>().Single();tabs.SelectedIndex=4;
            MainFormTests.Descendants(form).OfType<DateTimePicker>().Single().Value=clock.UtcNow.LocalDateTime;form.Show();Pump(()=>!form.IsReading);
            var row=Assert.Single(MainFormTests.Descendants(form).OfType<DataGridView>().Single(x=>x.Name=="gameUsage").Rows.Cast<DataGridViewRow>());
            Assert.Equal("00:00:45",row.Cells[2].Value);Assert.Equal("00:01:00",row.Cells[3].Value);form.Close();
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(root))Directory.Delete(root,true);}
    });

    [Fact]
    public void BlockedDatabaseReadRunsOffUiThreadAndStopRemainsResponsive() => MainFormTests.OnUiThread(() =>
    {
        using var f=new AsyncFixture(block:true);var uiThread=Environment.CurrentManagedThreadId;f.Tracker.Start();f.Form.Show();
        Assert.True(f.Store.Entered.Wait(TimeSpan.FromSeconds(3)));Assert.NotEqual(uiThread,f.Store.QueryThread);
        f.Control<Button>("İzlemeyi durdur",byText:true).PerformClick();Assert.False(f.Tracker.IsEnabled);
        Assert.Equal("İzleme: Kapalı",f.Control<Label>("trackingStatus").Text);
        f.Store.Release.Set();Pump(()=>!f.Form.IsReading);
    });

    [Fact]
    public void HiddenWindowDoesNoTableQueriesWhileTrackingContinuesAndReopenRefreshes() => MainFormTests.OnUiThread(() =>
    {
        using var f=new AsyncFixture();f.Tracker.Start();f.Form.Show();Pump(()=>!f.Form.IsReading);f.Form.Hide();var queries=f.Store.Queries;var reads=f.Source.Reads;
        for(var i=0;i<1000;i++){f.Clock.Advance(1);f.Form.SampleAndRefresh();}
        Assert.Equal(queries,f.Store.Queries);Assert.Equal(reads+1000,f.Source.Reads);Assert.True(f.Tracker.IsEnabled);
        f.Form.Show();Pump(()=>!f.Form.IsReading);Assert.True(f.Store.Queries>queries);
    });

    [Fact]
    public void HistoryTabRendersOnlyLastFiveHundredFromLargeSqliteHistory() => MainFormTests.OnUiThread(() =>
    {
        using var f=new UiFixture();var start=f.Clock.UtcNow;
        f.Store.SaveBatch(Enumerable.Range(0,10000).Select(i=>new ActivityRecord(Guid.NewGuid(),"synthetic","synthetic","App","runtime",start.AddSeconds(i),start.AddSeconds(i+1),TimeSpan.FromSeconds(1),ActivityStatus.Completed)).ToArray(),[]);
        f.Form.UseAsyncQueries=true;f.Control<TabControl>("mainTabs").SelectedIndex=1;f.Form.Show();Pump(()=>!f.Form.IsReading);
        Assert.Equal(500,f.Control<DataGridView>("activityHistory").Rows.Count);Assert.Empty(f.Control<DataGridView>("dailyUsage").Rows.Cast<DataGridViewRow>());
        Assert.Contains("02:46:40",f.Control<Label>("dailyTotal").Text);
    });

    [Fact]
    public void DateChangeDuringReadDiscardsStaleResultAndCoalescesReload() => MainFormTests.OnUiThread(() =>
    {
        using var f=new AsyncFixture(block:true);f.Form.Show();Assert.True(f.Store.Entered.Wait(TimeSpan.FromSeconds(3)));
        var date=f.Control<DateTimePicker>("");date.Value=DateTime.Today.AddDays(-1);f.Store.Release.Set();Pump(()=>!f.Form.IsReading);
        Assert.Contains("00:00:02",f.Control<Label>("dailyTotal").Text);Assert.InRange(f.Store.Queries,2,3);
    });

    [Fact]
    public void UiModeFollowsTrustedGamePauseLockAndSavedToggleWithoutStoppingTracking() => MainFormTests.OnUiThread(() =>
    {
        using var f=new UiFixture();var policy=new GameModePolicy(f.Clock);f.Form.ConfigurePerformance(policy);f.Form.Show();
        f.Source.Application=new("Synthetic game","runtime",1,GameInstallationId:"trusted");f.Tracker.Start();f.Form.RefreshView();Assert.True(f.Form.IsGameModeActive);
        Assert.Contains("Açık",f.Control<Label>("gameModeStatus").Text);f.Tracker.Pause();f.Form.RefreshView();Assert.True(f.Form.IsGameModeActive);
        f.Tracker.Resume();f.Tracker.SetLocked(true);f.Form.RefreshView();Assert.False(f.Form.IsGameModeActive);f.Tracker.SetLocked(false);f.Form.RefreshView();Assert.True(f.Form.IsGameModeActive);
        f.ShowSettings();f.Control<CheckBox>("automaticGameMode").Checked=false;Assert.False(f.Form.IsGameModeActive);Assert.True(f.Tracker.IsEnabled);Assert.False(f.SettingsStore.Load().AutomaticGameMode);
    });

    [Fact]
    public void ClosingWithPendingReadDoesNotApplyDisposedControls() => MainFormTests.OnUiThread(() =>
    {
        using var f=new AsyncFixture(block:true);f.Form.Show();Assert.True(f.Store.Entered.Wait(TimeSpan.FromSeconds(3)));var work=f.Form.PendingDatabaseRead!;
        f.Form.Close();f.Store.Release.Set();Assert.True(work.Wait(TimeSpan.FromSeconds(3)));Application.DoEvents();Assert.True(f.Form.IsDisposed);
    });

    private static void Pump(Func<bool> done)
    {
        var watch=Stopwatch.StartNew();do{Application.DoEvents();Thread.Yield();}while(!done()&&watch.Elapsed<TimeSpan.FromSeconds(8));
        Assert.True(done(),"Background snapshot did not settle.");
    }

    private sealed class AsyncFixture:IDisposable
    {
        private readonly string directory=Path.Combine(Path.GetTempPath(),"FamilyMonitor.UiPerformance",Guid.NewGuid().ToString("N"));
        public FakeClock Clock=new();public FakeSource Source=new();public QueryStore Store;public ActivityTracker Tracker;public MainForm Form;
        public AsyncFixture(bool block=false)
        {
            Store=new(block);Tracker=new(Clock,Source,Store,"synthetic","synthetic");Tracker.GrantConsent();var settings=new LocalSettings{ConsentGranted=true,PrivacyVersion=1};
            var settingsStore=new LocalSettingsStore(directory);settingsStore.Save(settings);
            Form=new(Tracker,Store,settingsStore,settings,new FakeDialogs(),enableWindowsIntegration:false)
            {UseAsyncQueries=true,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-12000,-12000)};
        }
        public T Control<T>(string name,bool byText=false) where T:Control=>MainFormTests.Descendants(Form).OfType<T>().Single(x=>byText?x.Text==name:x.Name==name);
        public void Dispose(){Store.Release.Set();Form.PendingDatabaseRead?.Wait(TimeSpan.FromSeconds(3));Form.Close();Form.Dispose();Application.DoEvents();Store.Dispose();if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }

    private sealed class QueryStore(bool block):IActivityStore,IDisposable
    {
        public ManualResetEventSlim Entered=new(),Release=new(!block);public int Queries,QueryThread;
        public void Save(ActivityRecord record){}public void DeleteAll(){}public DateTimeOffset? GetLatestEnd()=>null;
        public IReadOnlyList<ActivityRecord> Read(DateTimeOffset from,DateTimeOffset to)=>[];
        public IReadOnlyList<ApplicationUsage> GetDailyUsage(DateOnly day,TimeZoneInfo zone)
        {
            Interlocked.Increment(ref Queries);QueryThread=Environment.CurrentManagedThreadId;Entered.Set();
            if(!Release.Wait(TimeSpan.FromSeconds(10)))throw new TimeoutException("Test barrier.");
            return [new("Synthetic","runtime",TimeSpan.FromSeconds(day==DateOnly.FromDateTime(DateTime.Today.AddDays(-1))?2:1))];
        }
        public void Dispose(){Entered.Dispose();Release.Dispose();}
    }
}
