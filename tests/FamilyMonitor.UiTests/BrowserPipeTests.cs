using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using FamilyMonitor.Core;
using FamilyMonitor.Core.Browsers;
using FamilyMonitor.Infrastructure.Browsers;
using FamilyMonitor.Tests;
using Xunit;

namespace FamilyMonitor.UiTests;

public sealed class BrowserPipeTests
{
    [Fact]
    public async Task ServerShutdownDoesNotRequireUiMessagePump()
    {
        var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new NonPumpingContext());
                var clock=new FakeClock();var tracker=new ActivityTracker(clock,new FakeSource(),new MemoryStore(),"test","test");
                using var runtime=new BrowserRuntime(tracker,clock,new Visits());
                var server=new BrowserPipeServer(runtime,Path.GetTempPath(),"FamilyMonitor.Tests."+Guid.NewGuid().ToString("N"),null);
                Thread.Sleep(100);server.Dispose();completion.SetResult();
            }
            catch(Exception error){completion.SetException(error);}
        }){IsBackground=true};
        thread.SetApartmentState(ApartmentState.STA);thread.Start();await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
    private sealed class NonPumpingContext:SynchronizationContext
    {public override void Post(SendOrPostCallback callback,object? state){ }}
    [Fact]
    public async Task LocalFramedProtocolExchangesPolicyAndStopsAtDisconnect()
    {
        var clock=new FakeClock();var source=new FakeSource{Application=new("Chrome","chrome",1)};var apps=new MemoryStore();var visits=new Visits();
        var tracker=new ActivityTracker(clock,source,apps,"synthetic","synthetic");using var runtime=new BrowserRuntime(tracker,clock,visits);
        runtime.Configure(new(){ConsentVersion=1,DomainTracking=true,ChromeEnabled=true});tracker.GrantConsent();tracker.Start();
        var name="FamilyMonitor.Tests."+Guid.NewGuid().ToString("N");
        // Test-only injectable process verifier; production constructor always validates the installed host executable.
        using var server=new BrowserPipeServer(runtime,Path.GetTempPath(),name,(_,_)=>true);
        using var client=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous);
        await client.ConnectAsync(3000);
        await NativeMessageProtocol.WriteAsync(client,NativeMessageProtocol.Encode(new{version=1,type="hello",browser="Chrome",extensionId=BrowserIdentity.ChromiumExtensionId}));
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var bytes=await NativeMessageProtocol.ReadAsync(client,timeout.Token);Assert.NotNull(bytes);
        using var policy=JsonDocument.Parse(bytes);var epoch=policy.RootElement.GetProperty("policy").GetProperty("epoch").GetString()!;
        Assert.True(policy.RootElement.GetProperty("policy").GetProperty("enabled").GetBoolean());
        await NativeMessageProtocol.WriteAsync(client,NativeMessageProtocol.Encode(new{version=1,type="snapshot",seq=1,epoch,windowId=1,tabId=1,focused=true,@private=false,page=new BrowserPage("example.test")}));
        await Until(()=>runtime.Correlator.Binding(clock.Elapsed)is not null);
        clock.Advance(2);tracker.Tick();Assert.Equal(2,visits.Rows.Values.Sum(v=>v.Duration.TotalSeconds));
        client.Dispose();await Until(()=>runtime.Correlator.Connections(BrowserKind.Chrome)==0);
        clock.Advance(2);tracker.Tick();Assert.Equal(2,visits.Rows.Values.Sum(v=>v.Duration.TotalSeconds));tracker.Stop();
    }
    [Fact]
    public async Task ProductionProcessCheckRejectsArbitrarySameUserClient()
    {
        var clock=new FakeClock();var tracker=new ActivityTracker(clock,new FakeSource(),new MemoryStore(),"test","test");
        using var runtime=new BrowserRuntime(tracker,clock,new Visits());var name="FamilyMonitor.Tests."+Guid.NewGuid().ToString("N");
        using var server=new BrowserPipeServer(runtime,Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")),name,null);
        using var client=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous);await client.ConnectAsync(3000);
        await NativeMessageProtocol.WriteAsync(client,NativeMessageProtocol.Encode(new{version=1,type="hello",browser="Chrome",extensionId=BrowserIdentity.ChromiumExtensionId}));
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try{Assert.Null(await NativeMessageProtocol.ReadAsync(client,timeout.Token));}catch(IOException){ }
        Assert.Equal(0,runtime.Correlator.Connections(BrowserKind.Chrome));
    }
    [Fact]
    public async Task UnknownExtensionIdIsRejectedEvenWhenTransportProcessIsTrusted()
    {
        var clock=new FakeClock();var tracker=new ActivityTracker(clock,new FakeSource(),new MemoryStore(),"test","test");
        using var runtime=new BrowserRuntime(tracker,clock,new Visits());var name="FamilyMonitor.Tests."+Guid.NewGuid().ToString("N");
        using var server=new BrowserPipeServer(runtime,Path.GetTempPath(),name,(_,_)=>true);
        using var client=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous);await client.ConnectAsync(3000);
        await NativeMessageProtocol.WriteAsync(client,NativeMessageProtocol.Encode(new{version=1,type="hello",browser="Chrome",extensionId="untrusted"}));
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try{Assert.Null(await NativeMessageProtocol.ReadAsync(client,timeout.Token));}catch(IOException){ }
        Assert.Equal(0,runtime.Correlator.Connections(BrowserKind.Chrome));
    }
    private static async Task Until(Func<bool> ready)
    {var watch=Stopwatch.StartNew();while(!ready()&&watch.Elapsed<TimeSpan.FromSeconds(3))await Task.Delay(10);Assert.True(ready(),"Local protocol state did not settle.");}
    private sealed class Visits:IBrowserActivityStore
    {
        public Dictionary<Guid,BrowserVisit> Rows=[];public void SaveBrowser(BrowserVisit value)=>Rows[value.Id]=value;
        public IReadOnlyList<BrowserVisit> ReadBrowser(DateTimeOffset from,DateTimeOffset to,BrowserKind? browser=null,int limit=500)=>Rows.Values.ToArray();
        public IReadOnlyList<BrowserSiteUsage> GetBrowserUsage(DateTimeOffset from,DateTimeOffset to,BrowserKind? browser=null)=>[];
        public void DeleteBrowser()=>Rows.Clear();public void PruneBrowser(DateTimeOffset before){ }
    }
}
