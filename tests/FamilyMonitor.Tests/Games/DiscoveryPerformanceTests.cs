using FamilyMonitor.Core;
using FamilyMonitor.Core.Games;
using FamilyMonitor.Infrastructure.Games;
using Xunit;

namespace FamilyMonitor.Tests.Games;

public sealed class DiscoveryPerformanceTests
{
    [Fact]
    public async Task AsyncPermissionLoadsOffCallerThreadAndPublishesOnlyAfterLoad()
    {
        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();var thread=Environment.CurrentManagedThreadId;var readThread=0;
        var repo=new Catalog{Reading=()=>{readThread=Environment.CurrentManagedThreadId;entered.Set();release.Wait();}};
        using var discovery=new GameDiscoveryService([],repo,new FakeClock());var task=discovery.GrantConsentAsync();
        try{Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));Assert.NotEqual(thread,readThread);Assert.False(discovery.HasConsent);}
        finally{release.Set();}
        Assert.True(await task);Assert.True(discovery.HasConsent);
    }

    [Fact]
    public async Task WithdrawalDuringPermissionLoadCannotReenableConsent()
    {
        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        var repo=new Catalog{Reading=()=>{entered.Set();release.Wait();}};using var discovery=new GameDiscoveryService([],repo,new FakeClock());var task=discovery.GrantConsentAsync();
        try{Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));discovery.SetConsent(false);}
        finally{release.Set();}
        Assert.False(await task);Assert.False(discovery.HasConsent);
    }

    [Fact]
    public async Task CachedForegroundResolutionDoesNotWaitForSlowScanCommit()
    {
        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();var root=Path.GetFullPath("synthetic-game");
        var game=GameIdentity.Create("synthetic","Synthetic",GamePlatform.Custom,"Test",root,root,[],GameInstallationState.Installed);
        var repo=new Catalog{Applying=()=>{entered.Set();release.Wait();}};
        using var discovery=new GameDiscoveryService([new FakeGameProvider("Test",()=>new("Test",[game],[]))],repo,new FakeClock());discovery.SetConsent(true);
        var app=new ApplicationIdentity("App","runtime",1);var detector=new Detector();discovery.Resolve(app,detector);var scan=Task.Run(discovery.Refresh);
        try{Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));var read=Task.Run(()=>discovery.Resolve(app,detector));await read.WaitAsync(TimeSpan.FromSeconds(2));Assert.Equal(1,detector.Calls);}
        finally{release.Set();await scan;}
    }
    [Fact]
    public void UnchangedCatalogSkipsSqlCommandsAndCatalogReload()
    {
        var root=Path.GetFullPath("synthetic-game");var game=GameIdentity.Create("synthetic","Synthetic",GamePlatform.Custom,"Test",root,root,[],GameInstallationState.Installed);
        var repo=new Catalog{Games=[game]};var provider=new FakeGameProvider("Test",()=>new("Test",[game with{LastPlayedUtc=DateTimeOffset.UtcNow}],[]));
        using var discovery=new GameDiscoveryService([provider],repo,new FakeClock());discovery.SetConsent(true);discovery.Refresh();discovery.Refresh();
        Assert.Equal(2,provider.Calls);Assert.Equal(0,repo.Commits);Assert.Equal(1,repo.Reads);
    }
    [Fact]
    public void GameDefersEveryRequestAndQuietPeriodResumesOnce()
    {
        var clock=new FakeClock();var repo=new Catalog();var provider=new FakeGameProvider("Test",()=>new("Test",[],[]));using var discovery=new GameDiscoveryService([provider],repo,clock,TimeSpan.FromHours(1));discovery.SetConsent(true);
        discovery.SetGaming(true);for(var i=0;i<1000;i++){discovery.NotifyLibraryChange();Assert.False(discovery.Refresh().Ran);}
        Assert.Equal(0,provider.Calls);Assert.True(discovery.IsDeferred);discovery.SetGaming(false);clock.Advance(14);Assert.False(discovery.Refresh().Ran);
        clock.Advance(1);Assert.True(discovery.Refresh().Ran);Assert.Equal(1,provider.Calls);Assert.False(discovery.IsDeferred);Assert.False(discovery.IsDue);
        clock.Advance(3599);Assert.False(discovery.IsDue);clock.Advance(1);Assert.True(discovery.IsDue);
    }

    [Fact]
    public void MeaningfulChangeIsDebouncedAndNoChangesAreObservedWithoutConsent()
    {
        var clock=new FakeClock();var provider=new FakeGameProvider("Test",()=>new("Test",[],[]));using var discovery=new GameDiscoveryService([provider],new Catalog(),clock);
        discovery.NotifyLibraryChange();Assert.False(discovery.IsDeferred);discovery.SetConsent(true);discovery.Refresh();
        discovery.NotifyLibraryChange();clock.Advance(4);Assert.False(discovery.Refresh().Ran);clock.Advance(1);Assert.True(discovery.Refresh().Ran);Assert.Equal(2,provider.Calls);
    }

    [Fact]
    public void BeginningGameCancelsInFlightScanBeforeItsCommit()
    {
        var clock=new FakeClock();var repo=new Catalog();GameDiscoveryService? service=null;var startGame=true;
        var provider=new FakeGameProvider("Test",()=>{if(startGame)service!.SetGaming(true);return new("Test",[],[]);});
        using var discovery=service=new([provider],repo,clock);discovery.SetConsent(true);Assert.False(discovery.Refresh().Ran);Assert.Equal(0,repo.Commits);Assert.True(discovery.IsDeferred);
        startGame=false;discovery.SetGaming(false);clock.Advance(15);Assert.True(discovery.Refresh().Ran);Assert.Equal(1,repo.Commits);
    }

    [Fact]
    public void FailingProviderHasBackoffAndDoesNotBlockHealthyProvider()
    {
        var clock=new FakeClock();var bad=new FakeGameProvider("Bad",()=>throw new UnauthorizedAccessException());var good=new FakeGameProvider("Good",()=>new("Good",[],[]));
        using var discovery=new GameDiscoveryService([bad,good],new Catalog(),clock);discovery.SetConsent(true);discovery.Refresh();discovery.Refresh();
        Assert.Equal(1,bad.Calls);Assert.Equal(2,good.Calls);clock.Advance(60);discovery.Refresh();Assert.Equal(2,bad.Calls);
    }

    [Fact]
    public void LargeCatalogIsLoadedOncePerScanAndSameForegroundResolutionIsCached()
    {
        var root=Path.GetFullPath("synthetic-library");var games=Enumerable.Range(0,2000).Select(i=>GameIdentity.Create(i.ToString(),"Synthetic_"+i,GamePlatform.Custom,"Test",root,Path.Combine(root,i.ToString()),[],GameInstallationState.Installed)).ToArray();
        var repo=new Catalog{Games=games};var providers=Enumerable.Range(0,6).Select(i=>new FakeGameProvider(i.ToString(),()=>new(i.ToString(),[],[]))).ToArray();
        using var discovery=new GameDiscoveryService(providers,repo,new FakeClock());discovery.SetConsent(true);var reads=repo.Reads;discovery.Refresh();Assert.Equal(reads+1,repo.Reads);
        var detector=new Detector();var app=new ApplicationIdentity("Synthetic","runtime",1);
        for(var i=0;i<10000;i++)discovery.Resolve(app,detector);Assert.Equal(1,detector.Calls);
        discovery.Refresh();discovery.Resolve(app,detector);Assert.Equal(2,detector.Calls);
    }

    [Fact]
    public void UnchangedMetadataIsReadOnceAndStampOrTtlChangeInvalidates()
    {
        var clock=new FakeClock();var files=new StampedFiles();var cache=new CachingGameFileSystem(files,clock);
        for(var i=0;i<1000;i++)Assert.Equal("metadata",cache.ReadText("synthetic"));Assert.Equal(1,files.Reads);Assert.Equal(999,cache.Hits);
        files.Revision++;cache.ReadText("synthetic");Assert.Equal(2,files.Reads);clock.Advance(600);cache.ReadText("synthetic");Assert.Equal(3,files.Reads);
    }

    [Fact]
    public void CacheHasBoundedPayloadAndEntriesAfterLongSyntheticRun()
    {
        var cache=new CachingGameFileSystem(new StampedFiles(),new FakeClock(),1024);
        for(var i=0;i<100000;i++)cache.ReadText("metadata_"+i);
        Assert.True(cache.RetainedBytes<=1024);Assert.Equal(100000,cache.Misses);
    }

    [Fact]
    public void CachedDirectoryArraysAreNotMutableByCallers()
    {
        var files=new StampedFiles();var cache=new CachingGameFileSystem(files,new FakeClock());
        cache.Files("synthetic","*.acf")[0]="modified";Assert.Equal("original",cache.Files("synthetic","*.acf")[0]);Assert.Equal(1,files.Lists);
        files.Revision++;Assert.Equal("original",cache.Files("synthetic","*.acf")[0]);Assert.Equal(2,files.Lists);
    }

    [Fact]
    public void RepeatedSteamScanReusesMetadataAndStillDiscoversNewManifest()
    {
        var inner=new MemoryGameFiles();var steam=Path.GetFullPath("synthetic-steam");for(var i=0;i<300;i++)RandomGame.Steam(inner,steam);
        var stamped=new StampedMemory(inner);var cache=new CachingGameFileSystem(stamped,new FakeClock());var provider=new SteamLibraryProvider(cache,()=>[steam]);
        var first=provider.Discover([],default);var reads=inner.Reads.Count;var second=provider.Discover(first.Installations,default);
        Assert.Equal(300,second.Installations.Count);Assert.Equal(reads,inner.Reads.Count);
        RandomGame.Steam(inner,steam);stamped.Revision++;Assert.Equal(301,provider.Discover(second.Installations,default).Installations.Count);
    }

    private sealed class Detector:IGameDetector
    {public int Calls;public GameMatch Detect(ApplicationIdentity app,IReadOnlyList<GameInstallation> games,IReadOnlyList<ManualGameMapping> mappings){Calls++;return new(GameMatchStatus.Unknown,null);}}
    private sealed class Catalog:IGameCatalogRepository
    {
        public GameInstallation[] Games=[];public int Reads,Commits;public Action? Reading,Applying;
        public IReadOnlyList<GameInstallation> GetInstallations(){Reading?.Invoke();Reads++;return Games;}
        public IReadOnlyList<ManualGameMapping> GetMappings()=>[];public void ApplyScan(GameLibraryScan scan){Applying?.Invoke();Commits++;}
        public void SaveManualGame(GameInstallation game,string path){}public IReadOnlyList<GameUsage> GetUsage(DateTimeOffset from,DateTimeOffset to)=>[];
        public IReadOnlyList<GameUsageHistory> GetHistory(DateTimeOffset from,DateTimeOffset to)=>[];
    }
    private sealed class StampedFiles:IGameFileSystem
    {
        public int Reads,Lists;public long Revision;
        public MetadataStamp? Stamp(string path)=>new(8,Revision);
        public string ReadText(string path){Reads++;return "metadata";}
        public string[] Files(string directory,string pattern){Lists++;return ["original"];}
        public string[] Directories(string directory)=>[];public PathAvailability DirectoryState(string path)=>PathAvailability.Present;public bool FileExists(string path)=>true;
    }
    private sealed class StampedMemory(MemoryGameFiles inner):IGameFileSystem
    {
        public long Revision;
        public MetadataStamp? Stamp(string path)=>new(0,Revision);public string ReadText(string path)=>inner.ReadText(path);
        public string[] Files(string directory,string pattern)=>inner.Files(directory,pattern);public string[] Directories(string directory)=>inner.Directories(directory);
        public PathAvailability DirectoryState(string path)=>inner.DirectoryState(path);public bool FileExists(string path)=>inner.FileExists(path);
    }
}
