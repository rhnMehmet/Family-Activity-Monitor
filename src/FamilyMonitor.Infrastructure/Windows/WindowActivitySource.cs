using FamilyMonitor.Core;

namespace FamilyMonitor.Infrastructure.Windows;

public interface IWindowsActivityApi
{
    TimeSpan GetIdleTime();
    bool IsInputDesktopAvailable();
    nint GetForegroundWindow();
    uint GetProcessId(nint window);
    string? GetProcessName(uint processId);
    string? GetExecutablePath(uint processId) => null;
    string? GetPackageFamilyName(uint processId) => null;
}

/// <summary>Adapter boundary is injectable, so process races and access failures can be tested without Windows.</summary>
public sealed class WindowActivitySource(IWindowsActivityApi api, Func<bool>? gameConsent = null,IClock? clock=null) : IActivitySource
{
    private ApplicationIdentity? cached;
    private bool cacheInitialized;
    private nint cachedWindow;
    private uint cachedProcess;
    private bool cachedPermission;
    private long expiresAt;
    public ActivitySample Read()
    {
        if (!api.IsInputDesktopAvailable()) return new(null, TimeSpan.Zero, IsSessionLocked: true);
        var idle = api.GetIdleTime();
        var window = api.GetForegroundWindow();
        if (window == 0) return new(null, idle);
        var processId = api.GetProcessId(window);
        if (processId == 0) return new(null, idle);
        var canReadGameMetadata = gameConsent?.Invoke() == true;
        var now=clock is null?Environment.TickCount64:(long)clock.Elapsed.TotalMilliseconds;
        if (cacheInitialized && window==cachedWindow && processId==cachedProcess && canReadGameMetadata==cachedPermission && now<expiresAt)
            return new(cached,idle);
        cachedWindow=window; cachedProcess=processId; cachedPermission=canReadGameMetadata;
        expiresAt=now+5000; cacheInitialized=true; cached=null;
        var process = api.GetProcessName(processId);
        if (string.IsNullOrWhiteSpace(process)) return new(null, idle);
        var application = process.ToLowerInvariant() switch
        {
            "chrome" => "Google Chrome", "msedge" => "Microsoft Edge", "firefox" => "Mozilla Firefox",
            "notepad" => "Not Defteri", "explorer" => "Windows Gezgini", _ => process
        };
        cached = new(application, process, window.ToInt64(),
            canReadGameMetadata ? api.GetExecutablePath(processId) : null,
            canReadGameMetadata ? api.GetPackageFamilyName(processId) : null);
        return new(cached,idle);
    }
}
