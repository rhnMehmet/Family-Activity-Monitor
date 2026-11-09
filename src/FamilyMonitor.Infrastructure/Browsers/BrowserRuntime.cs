using System.Collections.Concurrent;
using FamilyMonitor.Core;
using FamilyMonitor.Core.Browsers;

namespace FamilyMonitor.Infrastructure.Browsers;

public sealed class BrowserRuntime : IDisposable
{
    private readonly ActivityTracker tracker;
    private readonly IClock clock;
    private readonly BrowserTimeline timeline;
    private readonly IBrowserActivityStore store;
    private readonly ConcurrentQueue<(TimeSpan At,string Epoch,BrowserBinding? Binding)> updates=new();
    private int pending;
    private BrowserOptions options=new();
    private TimeSpan lastSample;
    private TimeSpan nextPrune;
    private volatile bool disposed;
    private string? lastError;
    public BrowserCorrelator Correlator { get; }=new();
    public string? LastError { get=>Volatile.Read(ref lastError);private set=>Volatile.Write(ref lastError,value); }

    public BrowserRuntime(ActivityTracker tracker,IClock clock,IBrowserActivityStore store)
    {
        this.tracker=tracker;this.clock=clock;this.store=store;timeline=new(store);
        tracker.SampleObserved+=Observed;tracker.IntervalConfirmed+=Confirmed;tracker.BoundaryChanged+=Boundary;
        Correlator.PolicyChanged+=EpochChanged;
    }
    public void Configure(BrowserOptions value)
    {
        options=value.Normalize();Boundary(tracker.ToUtc(clock.Elapsed));Correlator.Configure(options);LastError=null;
        nextPrune=TimeSpan.Zero;
    }
    public bool Receive(Guid connection,byte[] message)
    {
        if(disposed)return false;
        var at=clock.Elapsed;
        var epoch=Correlator.CurrentEpoch;
        if(!Correlator.Accept(connection,message,at,out var stale))return stale;
        if(!Correlator.Policy(connection).Enabled)return true;
        var binding=Correlator.Binding(at);
        if(epoch!=Correlator.CurrentEpoch)return true;
        if(Interlocked.Increment(ref pending)>1024)
        {Interlocked.Decrement(ref pending);LastError="Tarayıcı mesaj kuyruğu sınırı aşıldı";Correlator.Suspend();return false;}
        updates.Enqueue((at,epoch,binding));return true;
    }
    private void EpochChanged()
    {
        if(disposed||LastError is not null)return;
        if(Interlocked.Increment(ref pending)>1024){Interlocked.Decrement(ref pending);LastError="Tarayıcı durum kuyruğu sınırı aşıldı";return;}
        updates.Enqueue((clock.Elapsed,Correlator.CurrentEpoch,null));
    }
    private void Observed(ObservedActivity observed)
    {
        if(disposed)return;
        if(observed.Elapsed-lastSample>TimeSpan.FromSeconds(5))Boundary(observed.Utc);
        lastSample=observed.Elapsed;
        Correlator.ObserveForeground(observed.Sample,tracker.HasConsent&&tracker.IsEnabled&&!tracker.IsPaused&&!tracker.IsSuspended&&
            !tracker.IsSessionLocked&&observed.Sample.IdleTime<tracker.IdleThreshold&&LastError is null);
        var ready=new List<(TimeSpan At,string Epoch,BrowserBinding? Binding)>();
        var count=Volatile.Read(ref pending);
        for(var i=0;i<count&&updates.TryDequeue(out var update);i++)
        {
            Interlocked.Decrement(ref pending);
            if(update.At>observed.Elapsed){updates.Enqueue(update);Interlocked.Increment(ref pending);continue;}
            ready.Add(update);
        }
        foreach(var update in ready.OrderBy(x=>x.At))
        {
            if(update.Epoch==Correlator.CurrentEpoch)
                timeline.SetContext(update.Binding,tracker.ToUtc(update.At),tracker.ToUtc(update.At+TimeSpan.FromSeconds(20)));
        }
        var binding=Correlator.Binding(observed.Elapsed);
        timeline.SetContext(binding,observed.Utc,tracker.ToUtc(observed.Elapsed+TimeSpan.FromSeconds(20)));
        Maintenance();
    }
    private void Confirmed(ConfirmedActivity activity)
    {
        if(disposed||LastError is not null)return;
        try{timeline.Confirm(activity);}
        catch(Exception error)when(error is not OutOfMemoryException){LastError=error.GetType().Name;Correlator.Suspend();throw;}
    }
    private void Boundary(DateTimeOffset at)
    {
        try{timeline.SetContext(null,at,at);}
        finally{timeline.Reset();while(updates.TryDequeue(out _))Interlocked.Decrement(ref pending);Correlator.Suspend();}
    }
    public void DeleteHistory()
    {Boundary(tracker.ToUtc(clock.Elapsed));store.DeleteBrowser();}
    public void Maintenance()
    {
        if(clock.Elapsed<nextPrune||disposed)return;
        store.PruneBrowser(tracker.ToUtc(clock.Elapsed).AddDays(-options.RetentionDays));nextPrune=clock.Elapsed+TimeSpan.FromHours(1);
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;tracker.SampleObserved-=Observed;tracker.IntervalConfirmed-=Confirmed;tracker.BoundaryChanged-=Boundary;Correlator.PolicyChanged-=EpochChanged;
        Boundary(tracker.ToUtc(clock.Elapsed));
    }
}
