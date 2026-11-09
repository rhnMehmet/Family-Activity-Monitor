using System.Text;
using FamilyMonitor.Core;
using FamilyMonitor.Core.Browsers;
using Xunit;

namespace FamilyMonitor.Tests.Browsers;

public sealed class BrowserCoreTests
{
    [Theory]
    [InlineData(BrowserKind.Chrome)]
    [InlineData(BrowserKind.Edge)]
    [InlineData(BrowserKind.Firefox)]
    public void NativeHostArgumentsAllowOnlyOwnExtensionAndExpectedFirefoxManifest(BrowserKind kind)
    {
        var manifest=Path.GetFullPath("native-host-manifests/firefox.json");
        var args=kind==BrowserKind.Firefox?new[]{manifest,BrowserIdentity.FirefoxExtensionId}:new[]{$"chrome-extension://{BrowserIdentity.ChromiumExtensionId}/","--parent-window=0"};
        Assert.True(NativeHostIdentity.ArgumentsAllowed(kind,args,manifest));
        args=kind==BrowserKind.Firefox?new[]{manifest,"untrusted@example.test"}:new[]{"chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/"};
        Assert.False(NativeHostIdentity.ArgumentsAllowed(kind,args,manifest));
        Assert.Equal(kind,NativeHostIdentity.FromExecutable(Path.GetFullPath($"FamilyMonitor.NativeHost.{kind}.exe")));
        Assert.Null(NativeHostIdentity.FromExecutable(Path.GetFullPath("FamilyMonitor.NativeHost.exe")));
    }
    [Theory]
    [InlineData("chrome",BrowserKind.Chrome)]
    [InlineData("msedge",BrowserKind.Edge)]
    [InlineData("firefox",BrowserKind.Firefox)]
    public void BrowserHasIndependentPermissionAndForegroundChallenge(string process,BrowserKind kind)
    {
        var c=Configured();var id=Guid.NewGuid();Assert.True(c.Register(id,kind,BrowserIdentity.ExtensionId(kind)));
        c.ObserveForeground(new(new("Browser",process,10),TimeSpan.Zero),true);var policy=c.Policy(id);Assert.True(policy.Enabled);
        Assert.True(c.Accept(id,Snapshot(policy.Epoch,1),TimeSpan.Zero));Assert.Equal("example.com",c.Binding(TimeSpan.Zero)!.Page.Domain);
        c.ObserveForeground(new(new("Game","game",20),TimeSpan.Zero),true);Assert.Null(c.Binding(TimeSpan.FromSeconds(1)));Assert.False(c.Policy(id).Enabled);
        c.ObserveForeground(new(new("Browser",process,10),TimeSpan.Zero),true);Assert.NotEqual(policy.Epoch,c.Policy(id).Epoch);Assert.Null(c.Binding(TimeSpan.FromSeconds(2)));
        Assert.False(c.Accept(id,Snapshot(policy.Epoch,2),TimeSpan.FromSeconds(2)));
    }
    [Fact]
    public void ChangedNativeWindowRequiresNewSnapshotAndDisconnectedOrStaleClientCannotEarnTime()
    {
        var c=Configured();var id=Guid.NewGuid();c.Register(id,BrowserKind.Chrome,BrowserIdentity.ChromiumExtensionId);
        c.ObserveForeground(new(new("Chrome","chrome",1),TimeSpan.Zero),true);c.Accept(id,Snapshot(c.Policy(id).Epoch,1),TimeSpan.Zero);
        Assert.Null(c.Binding(TimeSpan.FromSeconds(21)));c.ObserveForeground(new(new("Chrome","chrome",2),TimeSpan.Zero),true);
        Assert.Null(c.Binding(TimeSpan.FromSeconds(22)));c.Accept(id,Snapshot(c.Policy(id).Epoch,2),TimeSpan.FromSeconds(22));Assert.Equal(2,c.Binding(TimeSpan.FromSeconds(22))!.NativeWindow);
        c.Disconnect(id);Assert.Null(c.Binding(TimeSpan.FromSeconds(23)));
    }
    [Fact]
    public void TwoProfilesMustBothAcknowledgeAndMultipleFocusedProfilesAreAmbiguous()
    {
        var c=Configured();var a=Guid.NewGuid();var b=Guid.NewGuid();c.Register(a,BrowserKind.Chrome,BrowserIdentity.ChromiumExtensionId);c.Register(b,BrowserKind.Chrome,BrowserIdentity.ChromiumExtensionId);
        c.ObserveForeground(new(new("Chrome","chrome",1),TimeSpan.Zero),true);var epoch=c.Policy(a).Epoch;
        c.Accept(a,Snapshot(epoch,1),TimeSpan.Zero);Assert.Null(c.Binding(TimeSpan.Zero));
        c.Accept(b,Snapshot(epoch,1),TimeSpan.Zero);Assert.Null(c.Binding(TimeSpan.Zero));Assert.Contains("Belirsiz",c.State);
        c.Accept(b,Snapshot(epoch,2,false),TimeSpan.Zero);Assert.NotNull(c.Binding(TimeSpan.Zero));
    }
    [Fact]
    public void NoConsentPrivateWindowAndWithdrawnConsentNeverProducePageBinding()
    {
        var c=new BrowserCorrelator();var id=Guid.NewGuid();Assert.False(c.Register(id,BrowserKind.Chrome,"untrusted"));c.Register(id,BrowserKind.Chrome,BrowserIdentity.ChromiumExtensionId);
        c.ObserveForeground(new(new("Chrome","chrome",1),TimeSpan.Zero),true);Assert.False(c.Policy(id).Enabled);
        c.Configure(Options());Assert.True(c.Accept(id,Snapshot(c.Policy(id).Epoch,1,true,true),TimeSpan.Zero));Assert.Null(c.Binding(TimeSpan.Zero));
        c.Configure(new());Assert.False(c.Policy(id).Enabled);Assert.Null(c.Binding(TimeSpan.Zero));
    }
    [Fact]
    public void DuplicateSequenceUnknownFieldsAndUnauthorisedDetailsAreRejected()
    {
        var c=Configured();var id=Guid.NewGuid();c.Register(id,BrowserKind.Chrome,BrowserIdentity.ChromiumExtensionId);c.ObserveForeground(new(new("Chrome","chrome",1),TimeSpan.Zero),true);
        var epoch=c.Policy(id).Epoch;var message=Snapshot(epoch,1);Assert.True(c.Accept(id,message,TimeSpan.Zero));Assert.False(c.Accept(id,message,TimeSpan.Zero));
        var extra=Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Snapshot(epoch,2)).Replace("\"version\":1","\"version\":1,\"cookie\":\"forbidden\""));Assert.False(c.Accept(id,extra,TimeSpan.Zero));
        Assert.False(c.Accept(id,NativeMessageProtocol.Encode(new{version=1,type="snapshot",seq=3,epoch,windowId=1,tabId=1,focused=true,@private=false,page=new BrowserPage("example.com","https://example.com/path")}),TimeSpan.Zero));
    }
    [Theory]
    [InlineData("https://user:secret@example.com/")]
    [InlineData("file:///C:/secret.txt")]
    [InlineData("chrome://settings")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("javascript:alert(1)")]
    public void UnsupportedUrlsAreExcluded(string url)=>Assert.Null(BrowserPrivacy.FromUrl(url,true,true));
    [Fact]
    public void DomainDefaultAndDetailsStripAllQueryFragmentCredentialsAndOpaquePath()
    {
        var basic=BrowserPrivacy.FromUrl("https://example.com/articles/read?token=private#fragment",false,false)!;Assert.Equal(new BrowserPage("example.com"),basic);
        var details=BrowserPrivacy.FromUrl("https://example.com/articles/12345?q=unused&access_token=private#secret",true,false)!;
        Assert.DoesNotContain('?',details.DetailUrl!);Assert.DoesNotContain('#',details.DetailUrl!);Assert.Contains("[redacted]",details.DetailUrl!);
        Assert.Equal("https://example.com/[redacted]",BrowserPrivacy.FromUrl("https://example.com/reset/shortsecret",true,false)!.DetailUrl);
    }
    [Theory]
    [InlineData("https://www.google.com/search?q=hello+world&token=unused","Google","hello world")]
    [InlineData("https://www.bing.com/search?q=merhaba","Bing","merhaba")]
    [InlineData("https://duckduckgo.com/?q=privacy","DuckDuckGo","privacy")]
    [InlineData("https://search.yahoo.com/search?p=test","Yahoo","test")]
    public void SearchTermsNeedSeparateConsentAndExactResultsRoute(string url,string engine,string query)
    {
        Assert.Null(BrowserPrivacy.FromUrl(url,false,false)!.SearchQuery);var page=BrowserPrivacy.FromUrl(url,false,true)!;
        Assert.Equal(engine,page.SearchEngine);Assert.Equal(query,page.SearchQuery);
        Assert.Null(BrowserPrivacy.FromUrl("https://example.com/search?q=private",true,true)!.SearchQuery);
        Assert.Null(BrowserPrivacy.FromUrl("https://www.google.com/profile?q=private",false,true)!.SearchQuery);
    }
    [Fact]
    public void SearchSecretAndDuplicateQueryParametersAreDropped()
    {
        Assert.Null(BrowserPrivacy.FromUrl("https://google.com/search?q=password%3Ddontstore",false,true)!.SearchQuery);
        Assert.Null(BrowserPrivacy.FromUrl("https://google.com/search?q=one&q=two",false,true)!.SearchQuery);
    }
    [Fact]
    public void TimelineSplitsTabsInsideConfirmedIntervalWithoutDoubleCounting()
    {
        var store=new Visits();var ledger=new BrowserTimeline(store);var start=new DateTimeOffset(2026,11,8,9,0,0,TimeSpan.Zero);var id=Guid.NewGuid();
        var first=new BrowserBinding(Guid.NewGuid(),BrowserKind.Chrome,10,1,1,new("one.example"));
        ledger.SetContext(first,start,start.AddMinutes(1));ledger.SetContext(first with{TabId=2,Page=new("two.example")},start.AddSeconds(3),start.AddMinutes(1));
        var record=new ActivityRecord(id,"d","s","Chrome","chrome",start,start.AddSeconds(8),TimeSpan.FromSeconds(8),ActivityStatus.Active);
        ledger.Confirm(new(record,10));ledger.Confirm(new(record,10));Assert.Equal(8,store.Rows.Values.Sum(x=>x.Duration.TotalSeconds));
        Assert.Equal(3,store.Rows.Values.Single(x=>x.Domain=="one.example").Duration.TotalSeconds);Assert.Equal(5,store.Rows.Values.Single(x=>x.Domain=="two.example").Duration.TotalSeconds);
        ledger.Confirm(new(record with{Id=Guid.NewGuid(),ProcessName="game"},20));Assert.Equal(8,store.Rows.Values.Sum(x=>x.Duration.TotalSeconds));
    }
    [Fact]
    public void SameDomainTabAndExtensionWindowChangesStillSplitVisitPeriods()
    {
        var store=new Visits();var ledger=new BrowserTimeline(store);var start=new DateTimeOffset(2026,11,8,9,0,0,TimeSpan.Zero);
        var binding=new BrowserBinding(Guid.NewGuid(),BrowserKind.Chrome,10,1,1,new("example.com"));
        ledger.SetContext(binding,start,start.AddSeconds(20));
        ledger.SetContext(binding with{TabId=2},start.AddSeconds(3),start.AddSeconds(20));
        ledger.SetContext(binding with{WindowId=2,TabId=3},start.AddSeconds(5),start.AddSeconds(20));
        var record=new ActivityRecord(Guid.NewGuid(),"d","s","Chrome","chrome",start,start.AddSeconds(8),TimeSpan.FromSeconds(8),ActivityStatus.Active);
        ledger.Confirm(new(record,10));ledger.Confirm(new(record,10));
        Assert.Equal(3,store.Rows.Count);Assert.Equal(new[]{2d,3d,3d},store.Rows.Values.Select(v=>v.Duration.TotalSeconds).Order().ToArray());
        Assert.Equal(8,store.Rows.Values.Sum(v=>v.Duration.TotalSeconds));
    }
    [Fact]
    public void TimelineCannotUseWrongWindowOrUnconfirmedIdleTimeAndMemoryIsBounded()
    {
        var store=new Visits();var ledger=new BrowserTimeline(store);var start=DateTimeOffset.UtcNow;
        for(var i=0;i<10000;i++)ledger.SetContext(new(Guid.NewGuid(),BrowserKind.Chrome,10,1,i,new("example.com")),start.AddSeconds(i),start.AddSeconds(i+20));
        Assert.Equal(512,ledger.ContextCount);
        var record=new ActivityRecord(Guid.NewGuid(),"d","s","Chrome","chrome",start,start.AddSeconds(10),TimeSpan.FromSeconds(10),ActivityStatus.Completed);
        ledger.Confirm(new(record,99));Assert.Empty(store.Rows);
    }
    [Fact]
    public async Task NativeFramingRejectsOversizeTruncationAndUnknownPayloadShape()
    {
        var bytes=NativeMessageProtocol.Encode(new{version=1,type="ready",seq=1});using var stream=new MemoryStream();await NativeMessageProtocol.WriteAsync(stream,bytes);stream.Position=0;
        Assert.Equal(bytes,await NativeMessageProtocol.ReadAsync(stream));Assert.Null(await NativeMessageProtocol.ReadAsync(stream));
        using var oversize=new MemoryStream([1,32,0,0]);await Assert.ThrowsAsync<InvalidDataException>(()=>NativeMessageProtocol.ReadAsync(oversize));
        using var truncated=new MemoryStream([5,0,0]);await Assert.ThrowsAsync<EndOfStreamException>(()=>NativeMessageProtocol.ReadAsync(truncated));
        Assert.False(NativeMessageProtocol.TrySnapshot(Encoding.UTF8.GetBytes("{\"version\":1,\"type\":\"ready\",\"seq\":1,\"seq\":2}"),out _,out _,out _));
    }
    private static BrowserCorrelator Configured(){var c=new BrowserCorrelator();c.Configure(Options());return c;}
    internal static BrowserOptions Options()=>new(){ConsentVersion=1,DomainTracking=true,ChromeEnabled=true,EdgeEnabled=true,FirefoxEnabled=true};
    private static byte[] Snapshot(string epoch,int seq,bool focused=true,bool privateWindow=false)=>NativeMessageProtocol.Encode(new{version=1,type="snapshot",seq,epoch,windowId=1,tabId=1,focused,@private=privateWindow,page=focused&&!privateWindow?new BrowserPage("example.com"):null});
    private sealed class Visits:IBrowserActivityStore
    {
        public Dictionary<Guid,BrowserVisit> Rows=[];public void SaveBrowser(BrowserVisit v)=>Rows[v.Id]=v;
        public IReadOnlyList<BrowserVisit> ReadBrowser(DateTimeOffset from,DateTimeOffset to,BrowserKind? browser=null,int limit=500)=>Rows.Values.ToArray();
        public IReadOnlyList<BrowserSiteUsage> GetBrowserUsage(DateTimeOffset from,DateTimeOffset to,BrowserKind? browser=null)=>[];
        public void DeleteBrowser()=>Rows.Clear();public void PruneBrowser(DateTimeOffset before){ }
    }
}
