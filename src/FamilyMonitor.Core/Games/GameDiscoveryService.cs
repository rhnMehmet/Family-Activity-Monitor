namespace FamilyMonitor.Core.Games;

/// <summary>Serial scans, provider isolation and revocation-before-commit protection.</summary>
public sealed class GameDiscoveryService : IDisposable
{
    private readonly IReadOnlyList<IGameLibraryProvider> providers;
    private readonly IGameCatalogRepository repository;
    private readonly IClock clock;
    private readonly object gate = new();
    private CancellationTokenSource cancellation = new();
    private int scanning;
    private bool consent;
    private bool disposed;
    private TimeSpan? lastAttemptElapsed;
    private bool gaming;
    private TimeSpan notBefore;
    private bool pending;
    private sealed record CachedResolution(ApplicationIdentity Application, IGameDetector Detector, GameMatch Match);
    private CachedResolution? resolution;
    private long consentRequest;
    private readonly Dictionary<string,TimeSpan> retryAfter = [];
    public long Revision { get; private set; }
    public bool IsDeferred { get { lock(gate)return pending; } }
    private GameInstallation[] catalog = [];
    private ManualGameMapping[] mappings = [];
    public TimeSpan RefreshInterval { get; }
    public DateTimeOffset? LastAttemptUtc { get; private set; }
    public bool HasConsent => Volatile.Read(ref consent);
    public bool IsDue { get { lock (gate) return consent && (pending || lastAttemptElapsed is null || clock.Elapsed - lastAttemptElapsed >= RefreshInterval); } }

    public GameDiscoveryService(IEnumerable<IGameLibraryProvider> providers, IGameCatalogRepository repository, IClock clock, TimeSpan? refreshInterval = null)
    { this.providers = providers.ToArray(); this.repository = repository; this.clock = clock; RefreshInterval=refreshInterval??TimeSpan.FromMinutes(15); }

    public void SetGaming(bool active)
    {
        if(Volatile.Read(ref gaming)==active)return;
        lock(gate)
        {
            if(gaming&&!active)notBefore=clock.Elapsed+TimeSpan.FromSeconds(15);
            if(!gaming&&active&&Volatile.Read(ref scanning)!=0)
            { pending=true; cancellation.Cancel(); cancellation.Dispose(); cancellation=new(); }
            gaming=active;
        }
    }
    public void NotifyLibraryChange()
    { lock(gate){if(!consent||disposed)return;pending=true;notBefore=Max(notBefore,clock.Elapsed+TimeSpan.FromSeconds(5));} }
    private static TimeSpan Max(TimeSpan a,TimeSpan b)=>a>b?a:b;

    public void SetConsent(bool granted)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            consentRequest++;
            if (granted == consent) return;
            cancellation.Cancel();
            cancellation.Dispose();
            cancellation = new();
            var nextCatalog = granted ? repository.GetInstallations().ToArray() : [];
            var nextMappings = granted ? repository.GetMappings().ToArray() : [];
            catalog = nextCatalog;
            mappings = nextMappings;
            consent = granted;
            Volatile.Write(ref resolution,null);Revision++;
            if (granted) { LastAttemptUtc = null; lastAttemptElapsed = null; }
        }
    }

    public async Task<bool> GrantConsentAsync()
    {
        long request;
        lock(gate)
        {
            ObjectDisposedException.ThrowIf(disposed,this);
            if(consent)return true;
            request=++consentRequest;
        }
        var loaded=await Task.Run(()=>(Games:repository.GetInstallations().ToArray(),Mappings:repository.GetMappings().ToArray())).ConfigureAwait(false);
        lock(gate)
        {
            if(disposed||request!=consentRequest)return false;
            cancellation.Cancel();cancellation.Dispose();cancellation=new();
            catalog=loaded.Games;mappings=loaded.Mappings;consent=true;
            Volatile.Write(ref resolution,null);Revision++;LastAttemptUtc=null;lastAttemptElapsed=null;
            return true;
        }
    }

    public GameMatch Resolve(ApplicationIdentity app, IGameDetector detector)
    {
        var cached=Volatile.Read(ref resolution);
        if(HasConsent&&cached is not null&&cached.Application==app&&ReferenceEquals(cached.Detector,detector))return cached.Match;
        lock (gate)
        {
            if(!consent)return new(GameMatchStatus.Unknown,null);
            var match=detector.Detect(app,catalog,mappings);
            Volatile.Write(ref resolution,new(app,detector,match));return match;
        }
    }

    public GameDiscoveryReport Refresh()
    {
        if (Interlocked.CompareExchange(ref scanning, 1, 0) != 0) return new(false, ["Yenileme zaten sürüyor."]);
        try
        {
            CancellationToken token;
            lock (gate)
            {
                if (!consent || disposed) return new(false, ["Oyun keşfi izni verilmedi."]);
                if(gaming || clock.Elapsed<notBefore){pending=true;return new(false,["Oyun modu / sakinleşme aralığı nedeniyle keşif ertelendi."]);}
                token = cancellation.Token;
                pending=false;
                LastAttemptUtc = clock.UtcNow;
                lastAttemptElapsed = clock.Elapsed;
            }
            var messages = new List<string>();
            var catalogChanged = false;
            foreach (var provider in providers)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    GameInstallation[] previous;
                    lock (gate)
                    {
                        if(retryAfter.TryGetValue(provider.Id,out var retry)&&clock.Elapsed<retry)
                        { messages.Add($"{provider.Id}: hata sonrası yeniden deneme bekleniyor."); continue; }
                        previous = catalog.Where(g => g.ProviderId == provider.Id).ToArray();
                    }
                    var scan = provider.Discover(previous, token);
                    var known = previous.ToDictionary(g => g.Id);
                    var changed = scan.Installations.Where(g => !known.TryGetValue(g.Id,out var old) || !SameMetadata(g,old)).ToArray();
                    lock (gate)
                    {
                        token.ThrowIfCancellationRequested();
                        if (!consent || disposed) return new(false, ["İzin geri çekildi; sonuç kaydedilmedi."]);
                        if(changed.Length>0 || scan.Installations.Count==0)
                        { repository.ApplyScan(scan with { Installations=changed }); catalogChanged=true; }
                        retryAfter.Remove(provider.Id);
                    }
                    messages.Add($"{provider.Id}: {scan.Installations.Count} kayıt işlendi.");
                    messages.AddRange(scan.Warnings);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error) when (error is not OutOfMemoryException)
                { lock(gate)retryAfter[provider.Id]=clock.Elapsed+TimeSpan.FromMinutes(1); messages.Add($"{provider.Id}: kaynak okunamadı ({error.GetType().Name}); eski kayıtlar korundu."); }
            }
            lock(gate)
            {
                token.ThrowIfCancellationRequested();
                if(consent&&!disposed&&catalogChanged){catalog=repository.GetInstallations().ToArray();mappings=repository.GetMappings().ToArray();Volatile.Write(ref resolution,null);Revision++;}
            }
            return new(true, messages);
        }
        catch (OperationCanceledException) { return new(false, ["Keşif iptal edildi; sonraki sonuçlar kaydedilmedi."]); }
        finally { Interlocked.Exchange(ref scanning, 0); }
    }

    public void AddManualGame(GameInstallation installation, string executablePath)
    {
        lock (gate)
        {
            if (!consent || disposed) throw new InvalidOperationException("Oyun keşfi izni gereklidir.");
            if (!string.IsNullOrWhiteSpace(executablePath) && new GameMetadataResolver().IsSharedOrHelperExecutable(executablePath))
                throw new ArgumentException("Ortak çalışma zamanı / launcher için kalıcı eşleştirme güvenilir değildir.");
            repository.SaveManualGame(installation, string.IsNullOrWhiteSpace(executablePath) ? "" : GameIdentity.NormalizePath(executablePath));
            catalog = repository.GetInstallations().ToArray();
            mappings = repository.GetMappings().ToArray();
            Volatile.Write(ref resolution,null);Revision++;
        }
    }

    private static bool SameMetadata(GameInstallation a,GameInstallation b) =>
        a.GameId==b.GameId && a.Name==b.Name && a.Platform==b.Platform && a.ProviderId==b.ProviderId &&
        a.Scope==b.Scope && a.InstallDirectory==b.InstallDirectory && a.State==b.State && a.IsConfirmed==b.IsConfirmed &&
        a.PackageFamilyName==b.PackageFamilyName && a.ExecutablePaths.Count==b.ExecutablePaths.Count &&
        new HashSet<string>(a.ExecutablePaths,StringComparer.OrdinalIgnoreCase).SetEquals(b.ExecutablePaths);

    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; consent = false; cancellation.Cancel(); cancellation.Dispose(); catalog = []; mappings = []; }
    }
}
