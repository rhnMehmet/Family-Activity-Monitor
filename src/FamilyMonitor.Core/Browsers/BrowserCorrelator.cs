namespace FamilyMonitor.Core.Browsers;

/// <summary>Bounded authenticated connections; every foreground/policy change requires fresh focus acknowledgements.</summary>
public sealed class BrowserCorrelator
{
    private sealed class Client(BrowserKind browser)
    {
        public BrowserKind Browser=browser;
        public long Sequence;
        public BrowserSnapshot? Snapshot;
        public TimeSpan Expires, RateWindow,ObservedAt;
        public int RateCount;
    }
    private readonly object gate=new();
    private readonly Dictionary<Guid,Client> clients=[];
    private BrowserOptions options=new();
    private BrowserKind? foreground;
    private long nativeWindow;
    private bool collecting;
    private string epoch=Guid.NewGuid().ToString("N");
    public event Action? PolicyChanged;
    public string CurrentEpoch {get{lock(gate)return epoch;}}
    public string State {get;private set;}="İzin kapalı";

    public bool Register(Guid id,BrowserKind browser,string extension)
    {
        lock(gate)
        {
            if(extension!=BrowserIdentity.ExtensionId(browser)||clients.Count>=16||clients.ContainsKey(id))return false;
            clients[id]=new(browser);Invalidate();return true;
        }
    }
    public void Disconnect(Guid id){lock(gate){if(clients.Remove(id))Invalidate();}}
    public void Configure(BrowserOptions value)
    {lock(gate){value=value.Normalize();if(value==options)return;options=value;Invalidate();}}
    public void ObserveForeground(ActivitySample sample,bool enabled)
    {
        lock(gate)
        {
            var browser=BrowserIdentity.FromProcess(sample.Application?.ProcessName);
            var window=sample.Application?.WindowId??0;
            enabled=enabled&&!sample.IsSessionLocked&&sample.Application?.GameInstallationId is null;
            if(browser==foreground&&window==nativeWindow&&collecting==enabled)return;
            foreground=browser;nativeWindow=window;collecting=enabled;Invalidate();
        }
    }
    public void Suspend(){lock(gate){collecting=false;Invalidate();}}
    private void Invalidate()
    {
        epoch=Guid.NewGuid().ToString("N");foreach(var c in clients.Values)c.Snapshot=null;
        State=collecting?"Taze pencere eşleşmesi bekleniyor":"İzleme kapalı / uygun ön plan yok";PolicyChanged?.Invoke();
    }
    public BrowserPolicy Policy(Guid id)
    {
        lock(gate)
        {
            var enabled=clients.TryGetValue(id,out var client)&&collecting&&foreground==client.Browser&&options.Allows(client.Browser);
            return new(enabled,enabled&&options.DetailedUrls,enabled&&options.SearchQueries,epoch);
        }
    }
    public bool Accept(Guid id,byte[] message,TimeSpan now)
        =>Accept(id,message,now,out _);
    public bool Accept(Guid id,byte[] message,TimeSpan now,out bool stale)
    {
        stale=false;
        if(message.Length>NativeMessageProtocol.MaximumBytes||!NativeMessageProtocol.TrySnapshot(message,out var snapshot,out var type,out var sequence))return false;
        lock(gate)
        {
            if(!clients.TryGetValue(id,out var c)||sequence<=c.Sequence)return false;
            if(now-c.RateWindow>=TimeSpan.FromSeconds(1)){c.RateWindow=now;c.RateCount=0;}
            if(++c.RateCount>20)return false;
            c.Sequence=sequence;
            if(type=="ready")return true;
            if(type=="heartbeat")
            {if(c.Snapshot?.Epoch==epoch&&Policy(id).Enabled)c.Expires=now+TimeSpan.FromSeconds(20);return true;}
            if(!Policy(id).Enabled||snapshot!.Epoch!=epoch){stale=true;return false;}
            BrowserPage? page=null;
            if(snapshot.Page is not null)
            {page=BrowserPrivacy.Validate(snapshot.Page,options);if(page is null)return false;}
            c.Snapshot=snapshot with{Page=page};c.ObservedAt=now;c.Expires=now+TimeSpan.FromSeconds(20);return true;
        }
    }
    public BrowserBinding? Binding(TimeSpan now)
    {
        lock(gate)
        {
            if(!collecting||foreground is null||!options.Allows(foreground.Value))
            {State="İzin kapalı / uygun ön plan yok";return null;}
            var relevant=clients.Where(x=>x.Value.Browser==foreground).ToArray();
            if(relevant.Length==0||relevant.Any(x=>x.Value.Snapshot?.Epoch!=epoch||now>=x.Value.Expires||now<x.Value.ObservedAt))
            {State="Bağlantı / taze pencere eşleşmesi bekleniyor";return null;}
            var focused=relevant.Where(x=>x.Value.Snapshot!.Focused).ToArray();
            if(focused.Length!=1){State=focused.Length>1?"Belirsiz eşleşme; site kaydı oluşturulmuyor":"Odak eşleşmedi; site kaydı oluşturulmuyor";return null;}
            var pair=focused[0];var snapshot=pair.Value.Snapshot!;
            if(snapshot.Private||snapshot.Page is null){State="Uygun site eşleşmesi yok";return null;}
            State="Ön plan ve etkin sekme eşleşti";
            return new(pair.Key,foreground.Value,nativeWindow,snapshot.WindowId,snapshot.TabId,snapshot.Page);
        }
    }
    public int Connections(BrowserKind browser){lock(gate)return clients.Values.Count(x=>x.Browser==browser);}
}
