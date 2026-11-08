using FamilyMonitor.Core;
using FamilyMonitor.Core.Games;
using FamilyMonitor.Infrastructure.Games;

namespace FamilyMonitor.Infrastructure;

/// <summary>Bounded coalescing queue; one FULL-synchronous transaction at most every five seconds. Dispose flushes.</summary>
public sealed class BufferedActivityStore : IActivityStore, IGameForegroundStore, IDisposable
{
    private readonly SqliteActivityStore persistent;
    private readonly IGameForegroundStore? foreground;
    private readonly IClock clock;
    private readonly Func<bool> mayMaintain;
    private readonly object dataGate=new();private readonly object writerGate=new();
    private readonly Dictionary<Guid,ActivityRecord> pending=new(),writtenActive=new();
    private readonly Dictionary<Guid,GameForegroundPeriod> pendingForeground=new(),writtenForeground=new();
    private readonly AutoResetEvent wake=new(false);
    private readonly Thread? worker;private volatile bool stopping;
    private volatile bool urgent;
    private TimeSpan lastFlush;private string? lastError;
    public string? LastWriteError => Volatile.Read(ref lastError);
    public int PendingCount {get{lock(dataGate)return pending.Count+pendingForeground.Count;}}
    public long Transactions {get;private set;}
    public TimeSpan FlushInterval {get;}=TimeSpan.FromSeconds(5);

    public BufferedActivityStore(SqliteActivityStore persistent,IClock clock,IGameForegroundStore? foreground=null,Func<bool>? mayMaintain=null,bool startWorker=true)
    {
        this.persistent=persistent;this.clock=clock;this.foreground=foreground;this.mayMaintain=mayMaintain??(()=>true);lastFlush=clock.Elapsed;
        if(startWorker){worker=new Thread(Work){IsBackground=true,Name="FamilyMonitor local writer",Priority=ThreadPriority.BelowNormal};worker.Start();}
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
            ActivityRecord[] records;GameForegroundPeriod[] periods;
            lock(dataGate){records=pending.Values.ToArray();periods=pendingForeground.Values.ToArray();urgent=false;}
            persistent.SaveBatch(records,periods);
            if(records.Length+periods.Length>0)Transactions++;
            lock(dataGate)
            {
                foreach(var record in records)
                {if(pending.TryGetValue(record.Id,out var value)&&value==record)pending.Remove(record.Id);if(record.Status==ActivityStatus.Active)writtenActive[record.Id]=record;else writtenActive.Remove(record.Id);}
                foreach(var period in periods)
                {if(pendingForeground.TryGetValue(period.Id,out var value)&&value==period)pendingForeground.Remove(period.Id);if(period.Status==ActivityStatus.Active)writtenForeground[period.Id]=period;else writtenForeground.Remove(period.Id);}
                while(writtenActive.Count>128)writtenActive.Remove(writtenActive.Keys.First());
                while(writtenForeground.Count>128)writtenForeground.Remove(writtenForeground.Keys.First());
            }
            lastFlush=clock.Elapsed;Volatile.Write(ref lastError,null);
            if(mayMaintain()&&(persistent.MaintenancePending||persistent.CheckpointNeeded))
                HeavyWorkCoordinator.TryRun(() => { if(mayMaintain()) persistent.PerformMaintenance(); });
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
    {lock(writerGate){lock(dataGate){persistent.DeleteAll();pending.Clear();pendingForeground.Clear();writtenActive.Clear();writtenForeground.Clear();}}}
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
