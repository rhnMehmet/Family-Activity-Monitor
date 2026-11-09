using FamilyMonitor.Core;
using FamilyMonitor.Core.Games;
using FamilyMonitor.Infrastructure.Games;
using FamilyMonitor.Core.Browsers;
using FamilyMonitor.Infrastructure.Browsers;

namespace FamilyMonitor.Infrastructure;

/// <summary>Bounded coalescing queue; one FULL-synchronous transaction at most every five seconds. Dispose flushes.</summary>
public sealed class BufferedActivityStore : IActivityStore, IGameForegroundStore, IBrowserActivityStore, IDisposable
{
    private readonly SqliteActivityStore persistent;
    private readonly IGameForegroundStore? foreground;
    private readonly IClock clock;
    private readonly Func<bool> mayMaintain;
    private readonly object dataGate=new();private readonly object writerGate=new();
    private readonly Dictionary<Guid,ActivityRecord> pending=new(),writtenActive=new();
    private readonly Dictionary<Guid,GameForegroundPeriod> pendingForeground=new(),writtenForeground=new();
    private readonly Dictionary<Guid,BrowserVisit> pendingBrowser=new(),writtenBrowser=new();
    private readonly SqliteBrowserActivityStore? browserStore;
    private DateTimeOffset? pruneBefore;
    private DateTimeOffset? browserCutoff;
    private readonly AutoResetEvent wake=new(false);
    private readonly Thread? worker;private volatile bool stopping;
    private volatile bool urgent;
    private TimeSpan lastFlush;private string? lastError;
    public string? LastWriteError => Volatile.Read(ref lastError);
    public int PendingCount {get{lock(dataGate)return pending.Count+pendingForeground.Count+pendingBrowser.Count;}}
    public long Transactions {get;private set;}
    public TimeSpan FlushInterval {get;}=TimeSpan.FromSeconds(5);

    public BufferedActivityStore(SqliteActivityStore persistent,IClock clock,IGameForegroundStore? foreground=null,Func<bool>? mayMaintain=null,bool startWorker=true,SqliteBrowserActivityStore? browserStore=null)
    {
        this.browserStore=browserStore;
        this.persistent=persistent;this.clock=clock;this.foreground=foreground;this.mayMaintain=mayMaintain??(()=>true);lastFlush=clock.Elapsed;
        if(startWorker){worker=new Thread(Work){IsBackground=true,Name="FamilyMonitor local writer",Priority=ThreadPriority.BelowNormal};worker.Start();}
    }
    public void SaveBrowser(BrowserVisit visit)
    {
        if(browserStore is null)throw new InvalidOperationException("Browser store not configured.");
        lock(dataGate)
        {
            ObjectDisposedException.ThrowIf(stopping,this);
            if(browserCutoff is {} cutoff)
            {
                if(visit.EndUtc<=cutoff)return;
                if(visit.StartUtc<cutoff)visit=visit with{StartUtc=cutoff};
            }
            if(pendingBrowser.TryGetValue(visit.Id,out var prior)&&prior==visit)return;
            if(!pendingBrowser.ContainsKey(visit.Id)&&writtenBrowser.TryGetValue(visit.Id,out var saved)&&saved==visit)return;
            if(!pendingBrowser.ContainsKey(visit.Id)&&PendingCount>=1024)throw new IOException("Local write queue is full.");
            pendingBrowser[visit.Id]=visit;if(visit.Status!=ActivityStatus.Active){urgent=true;wake.Set();}
        }
    }
    public void Save(ActivityRecord record)
    {
        lock(dataGate)
        {
            ObjectDisposedException.ThrowIf(stopping,this);
            if(pending.TryGetValue(record.Id,out var existing)&&existing==record)return;
            if(!pending.ContainsKey(record.Id)&&writtenActive.TryGetValue(record.Id,out var saved)&&saved==record)return;
            if(!pending.ContainsKey(record.Id)&&PendingCount>=1024)throw new IOException("Local write queue is full.");
            pending[record.Id]=record;
            if(record.Status!=ActivityStatus.Active){urgent=true;wake.Set();}
        }
    }
    public void SaveForeground(GameForegroundPeriod period)
    {
        lock(dataGate)
        {
            ObjectDisposedException.ThrowIf(stopping,this);
            if(pendingForeground.TryGetValue(period.Id,out var existing)&&existing==period)return;
            if(!pendingForeground.ContainsKey(period.Id)&&writtenForeground.TryGetValue(period.Id,out var saved)&&saved==period)return;
            if(!pendingForeground.ContainsKey(period.Id)&&PendingCount>=1024)throw new IOException("Local write queue is full.");
            pendingForeground[period.Id]=period;
            if(period.Status!=ActivityStatus.Active){urgent=true;wake.Set();}
        }
    }
    private void Work()
    {
        while(!stopping)
        {
            wake.WaitOne(1000);if(stopping)break;
            try{FlushIfDue();}catch(Exception error)when(error is not OutOfMemoryException){Volatile.Write(ref lastError,error.GetType().Name);urgent=false;lastFlush=clock.Elapsed+TimeSpan.FromSeconds(25);}
        }
    }
    public void FlushIfDue(){if(urgent||clock.Elapsed-lastFlush>=FlushInterval)Flush();}
    public void Flush()
    {
        lock(writerGate)
        {
            ActivityRecord[] records;GameForegroundPeriod[] periods;BrowserVisit[] visits;
            lock(dataGate){records=pending.Values.ToArray();periods=pendingForeground.Values.ToArray();visits=pendingBrowser.Values.ToArray();urgent=false;}
            persistent.SaveBatch(records,periods,visits);
            if(records.Length+periods.Length+visits.Length>0)Transactions++;
            lock(dataGate)
            {
                foreach(var record in records)
                {if(pending.TryGetValue(record.Id,out var value)&&value==record)pending.Remove(record.Id);if(record.Status==ActivityStatus.Active)writtenActive[record.Id]=record;else writtenActive.Remove(record.Id);}
                foreach(var period in periods)
                {if(pendingForeground.TryGetValue(period.Id,out var value)&&value==period)pendingForeground.Remove(period.Id);if(period.Status==ActivityStatus.Active)writtenForeground[period.Id]=period;else writtenForeground.Remove(period.Id);}
                foreach(var visit in visits)
                {if(pendingBrowser.TryGetValue(visit.Id,out var value)&&value==visit)pendingBrowser.Remove(visit.Id);if(visit.Status==ActivityStatus.Active)writtenBrowser[visit.Id]=visit;else writtenBrowser.Remove(visit.Id);}
                while(writtenActive.Count>128)writtenActive.Remove(writtenActive.Keys.First());
                while(writtenForeground.Count>128)writtenForeground.Remove(writtenForeground.Keys.First());
                while(writtenBrowser.Count>128)writtenBrowser.Remove(writtenBrowser.Keys.First());
            }
            lastFlush=clock.Elapsed;Volatile.Write(ref lastError,null);
            if(mayMaintain()&&(persistent.MaintenancePending||persistent.CheckpointNeeded))
                HeavyWorkCoordinator.TryRun(() => { if(mayMaintain()) persistent.PerformMaintenance(); });
            DateTimeOffset? cutoff;lock(dataGate)cutoff=pruneBefore;
            if(cutoff is not null&&browserStore is not null&&mayMaintain())
                HeavyWorkCoordinator.TryRun(()=>{if(!mayMaintain())return;browserStore.PruneBrowser(cutoff.Value);lock(dataGate){if(pruneBefore==cutoff)pruneBefore=null;}});
        }
    }
    public IReadOnlyList<ActivityRecord> Read(DateTimeOffset fromUtc,DateTimeOffset toUtc)=>ReadMerged(fromUtc,toUtc,false);
    public IReadOnlyList<ActivityRecord> ReadRecent(DateTimeOffset fromUtc,DateTimeOffset toUtc)=>ReadMerged(fromUtc,toUtc,true);
    private IReadOnlyList<ActivityRecord> ReadMerged(DateTimeOffset fromUtc,DateTimeOffset toUtc,bool recent)
    {
        lock(writerGate)
        {
            var result=(recent?persistent.ReadRecent(fromUtc,toUtc):persistent.Read(fromUtc,toUtc)).ToDictionary(x=>x.Id);
            lock(dataGate)foreach(var record in pending.Values)if(record.EndUtc>fromUtc&&record.StartUtc<toUtc)result[record.Id]=record;
            var sorted=result.Values.OrderBy(x=>x.StartUtc);return (recent?sorted.TakeLast(500):sorted).ToArray();
        }
    }
    public IReadOnlyList<ApplicationUsage> GetDailyUsage(DateOnly day,TimeZoneInfo zone)
    {
        lock(writerGate)
        {
            var from=GameUsageService.LocalDayBoundary(day,zone);var to=GameUsageService.LocalDayBoundary(day.AddDays(1),zone);
            ActivityRecord[] dirty;lock(dataGate)dirty=pending.Values.ToArray();
            var sums=persistent.GetDailyUsage(day,zone).ToDictionary(x=>(x.ApplicationName,x.ProcessName),x=>x.Duration.Ticks);
            foreach(var old in persistent.ReadByIds(dirty.Select(x=>x.Id)))Add(old,-1);
            foreach(var record in dirty)Add(record,1);
            void Add(ActivityRecord record,int sign){var key=(record.ApplicationName,record.ProcessName);sums.TryGetValue(key,out var value);sums[key]=value+sign*Clip(record.StartUtc,record.EndUtc,from,to);}
            return sums.Where(x=>x.Value>0).OrderByDescending(x=>x.Value).Select(x=>new ApplicationUsage(x.Key.ApplicationName,x.Key.ProcessName,TimeSpan.FromTicks(x.Value))).ToArray();
        }
    }
    public DateTimeOffset? GetLatestEnd()
    {lock(writerGate){var end=persistent.GetLatestEnd();lock(dataGate)foreach(var record in pending.Values)if(end is null||record.EndUtc>end)end=record.EndUtc;return end;}}
    public void DeleteAll()
    {lock(writerGate){lock(dataGate){persistent.DeleteAll();pending.Clear();pendingForeground.Clear();pendingBrowser.Clear();writtenActive.Clear();writtenForeground.Clear();writtenBrowser.Clear();}}}

    public IReadOnlyList<BrowserVisit> ReadBrowser(DateTimeOffset from,DateTimeOffset to,BrowserKind? browser=null,int limit=500)
    {
        lock(writerGate)
        {
            if(browserCutoff is {} cutoff && from<cutoff)from=cutoff;
            if(from>=to)return [];
            var rows=(browserStore?.ReadBrowser(from,to,browser,limit)??[]).ToDictionary(x=>x.Id);
            lock(dataGate)foreach(var v in pendingBrowser.Values)if(v.EndUtc>from&&v.StartUtc<to&&(browser is null||v.Browser==browser))rows[v.Id]=v;
            return rows.Values.Select(v=>browserCutoff is {} c && v.StartUtc<c?v with{StartUtc=c}:v).OrderByDescending(x=>x.StartUtc).Take(Math.Clamp(limit,1,500)).ToArray();
        }
    }
    public IReadOnlyList<BrowserSiteUsage> GetBrowserUsage(DateTimeOffset from,DateTimeOffset to,BrowserKind? browser=null)
    {
        lock(writerGate)
        {
            if(browserCutoff is {} cutoff && from<cutoff)from=cutoff;
            if(from>=to)return [];
            BrowserVisit[] dirty;lock(dataGate)dirty=pendingBrowser.Values.ToArray();
            var sums=(browserStore?.GetBrowserUsage(from,to,browser)??[]).ToDictionary(x=>(x.Browser,x.Domain),x=>(Ticks:x.Duration.Ticks,Visits:x.Visits));
            foreach(var old in browserStore?.ReadByIds(dirty.Select(x=>x.Id))??[])Add(old,-1);
            foreach(var visit in dirty)Add(visit,1);
            void Add(BrowserVisit v,int sign)
            {
                if(browser is not null&&v.Browser!=browser)return;
                var ticks=Clip(v.StartUtc,v.EndUtc,from,to);if(ticks<=0)return;
                var key=(v.Browser,v.Domain);sums.TryGetValue(key,out var value);sums[key]=(value.Ticks+sign*ticks,value.Visits+sign);
            }
            return sums.Where(x=>x.Value.Ticks>0).OrderByDescending(x=>x.Value.Ticks).Select(x=>new BrowserSiteUsage(x.Key.Browser,x.Key.Domain,TimeSpan.FromTicks(x.Value.Ticks),x.Value.Visits)).Take(500).ToArray();
        }
    }
    public void DeleteBrowser()
    {lock(writerGate){lock(dataGate){browserStore?.DeleteBrowser();pendingBrowser.Clear();writtenBrowser.Clear();persistent.RequestMaintenance();}}}
    public void PruneBrowser(DateTimeOffset before)
    {
        lock(writerGate)lock(dataGate)
        {
            if(browserCutoff is {} existing && before<=existing)return;
            browserCutoff=before;pruneBefore=before;
            foreach(var id in pendingBrowser.Keys.ToArray())
            {
                var visit=pendingBrowser[id];
                if(visit.EndUtc<=before)pendingBrowser.Remove(id);
                else if(visit.StartUtc<before)pendingBrowser[id]=visit with{StartUtc=before};
            }
            writtenBrowser.Clear();
        }
    }
    public IReadOnlyList<GameForegroundPeriod> ReadForeground(DateTimeOffset fromUtc,DateTimeOffset toUtc)
    {
        lock(writerGate){var rows=(foreground?.ReadForeground(fromUtc,toUtc)??[]).ToDictionary(x=>x.Id);lock(dataGate)foreach(var p in pendingForeground.Values)if(p.EndUtc>fromUtc&&p.StartUtc<toUtc)rows[p.Id]=p;return rows.Values.OrderBy(x=>x.StartUtc).ToArray();}
    }
    public IReadOnlyDictionary<string,TimeSpan> GetForegroundDurations(DateTimeOffset fromUtc,DateTimeOffset toUtc)
    {
        lock(writerGate)
        {
            GameForegroundPeriod[] dirty;lock(dataGate)dirty=pendingForeground.Values.ToArray();
            var sums=(foreground?.GetForegroundDurations(fromUtc,toUtc)??new Dictionary<string,TimeSpan>()).ToDictionary(x=>x.Key,x=>x.Value.Ticks);
            var previous=foreground is SqliteGameForegroundStore sqlite ? sqlite.ReadByIds(dirty.Select(x=>x.Id)) : (foreground?.ReadForeground(fromUtc,toUtc)??[]).Where(x=>dirty.Any(p=>p.Id==x.Id));
            foreach(var p in previous)Add(p,-1);foreach(var p in dirty)Add(p,1);
            void Add(GameForegroundPeriod p,int sign){sums.TryGetValue(p.InstallationId,out var value);sums[p.InstallationId]=value+sign*Clip(p.StartUtc,p.EndUtc,fromUtc,toUtc);}
            return sums.Where(x=>x.Value>0).ToDictionary(x=>x.Key,x=>TimeSpan.FromTicks(x.Value));
        }
    }
    private static long Clip(DateTimeOffset start,DateTimeOffset end,DateTimeOffset from,DateTimeOffset to)=>Math.Max(0,Math.Min(end.UtcTicks,to.UtcTicks)-Math.Max(start.UtcTicks,from.UtcTicks));
    public void Dispose(){if(stopping)return;stopping=true;wake.Set();worker?.Join();try{Flush();}finally{wake.Dispose();}}
}
