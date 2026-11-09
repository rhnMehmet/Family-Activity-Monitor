namespace FamilyMonitor.Core.Browsers;

public enum BrowserKind { Chrome, Edge, Firefox }

public static class BrowserIdentity
{
    public const string ChromiumExtensionId = "mcipbngepoajhbkoecbbdiijckmfejol";
    public const string FirefoxExtensionId = "family-monitor@local.familyactivitymonitor";
    public const int PrivacyVersion = 1;
    public static BrowserKind? FromProcess(string? name) => name?.ToLowerInvariant() switch
    { "chrome" => BrowserKind.Chrome, "msedge" => BrowserKind.Edge, "firefox" => BrowserKind.Firefox, _ => null };
    public static string Text(BrowserKind kind) => kind switch
    { BrowserKind.Chrome => "Chrome", BrowserKind.Edge => "Edge", _ => "Firefox" };
    public static string ExtensionId(BrowserKind kind) => kind == BrowserKind.Firefox ? FirefoxExtensionId : ChromiumExtensionId;
    public static string HostName(BrowserKind kind) => "com.familymonitor." + kind.ToString().ToLowerInvariant();
}

public sealed record BrowserOptions
{
    public int ConsentVersion { get; init; }
    public bool DomainTracking { get; init; }
    public bool DetailedUrls { get; init; }
    public bool SearchQueries { get; init; }
    public bool ChromeEnabled { get; init; }
    public bool EdgeEnabled { get; init; }
    public bool FirefoxEnabled { get; init; }
    public int RetentionDays { get; init; } = 30;
    public bool Allows(BrowserKind kind) => ConsentVersion == BrowserIdentity.PrivacyVersion && DomainTracking && kind switch
    { BrowserKind.Chrome => ChromeEnabled, BrowserKind.Edge => EdgeEnabled, _ => FirefoxEnabled };
    public BrowserOptions Normalize() => this with
    {
        DomainTracking = ConsentVersion == BrowserIdentity.PrivacyVersion && DomainTracking,
        DetailedUrls = ConsentVersion == BrowserIdentity.PrivacyVersion && DomainTracking && DetailedUrls,
        SearchQueries = ConsentVersion == BrowserIdentity.PrivacyVersion && DomainTracking && SearchQueries,
        RetentionDays = Math.Clamp(RetentionDays, 1, 365)
    };
}

public sealed record BrowserPage(string Domain, string? DetailUrl = null, string? SearchQuery = null, string? SearchEngine = null);
public sealed record BrowserSnapshot(long Sequence, string Epoch, int WindowId, int TabId, bool Focused, bool Private, BrowserPage? Page);
public sealed record BrowserPolicy(bool Enabled, bool DetailedUrls, bool SearchQueries, string Epoch);
public sealed record BrowserBinding(Guid Connection, BrowserKind Browser, long NativeWindow, int WindowId, int TabId, BrowserPage Page);
public sealed record BrowserVisit(Guid Id, Guid ActivityId, BrowserKind Browser, string Domain, string? DetailUrl,
    string? SearchQuery, string? SearchEngine, DateTimeOffset StartUtc, DateTimeOffset EndUtc, ActivityStatus Status,
    string Attribution = "FocusedWindowConfirmed")
{ public TimeSpan Duration => EndUtc - StartUtc; }
public sealed record BrowserSiteUsage(BrowserKind Browser, string Domain, TimeSpan Duration, int Visits);

public interface IBrowserActivityStore
{
    void SaveBrowser(BrowserVisit visit);
    IReadOnlyList<BrowserVisit> ReadBrowser(DateTimeOffset from, DateTimeOffset to, BrowserKind? browser = null, int limit = 500);
    IReadOnlyList<BrowserSiteUsage> GetBrowserUsage(DateTimeOffset from, DateTimeOffset to, BrowserKind? browser = null);
    void DeleteBrowser();
    void PruneBrowser(DateTimeOffset before);
}

public sealed record ObservedActivity(ActivitySample Sample, DateTimeOffset Utc, TimeSpan Elapsed);
public sealed record ConfirmedActivity(ActivityRecord Record, long NativeWindow);
