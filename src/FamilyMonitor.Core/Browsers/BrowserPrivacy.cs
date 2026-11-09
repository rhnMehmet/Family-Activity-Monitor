using System.Net;
using System.Text.RegularExpressions;

namespace FamilyMonitor.Core.Browsers;

/// <summary>No query parameters/fragments or userinfo in detailed URLs. Search terms use a separate allowlist/permission.</summary>
public static partial class BrowserPrivacy
{
    [GeneratedRegex(@"(?i)(token|session|password|passwd|secret|oauth|authorize|callback|credential|reset|verify|invitation|invite|login|auth|api[-_]?key)")]
    private static partial Regex SensitiveRoute();
    [GeneratedRegex(@"(?i)(?:gh[pousr]_[a-z0-9_]{16,}|github_pat_[a-z0-9_]{16,}|eyJ[a-z0-9_-]+\.[a-z0-9_-]+\.[a-z0-9_-]+|(?:password|passwd|secret|access_token|api_key)\s*[:=]|[a-z0-9_-]{32,})")]
    private static partial Regex Secret();

    public static BrowserPage? FromUrl(string? value, bool details, bool searches)
    {
        if (value is null || value.Length > 8192 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http") || uri.UserInfo.Length != 0 ||
            Uri.CheckHostName(uri.IdnHost) != UriHostNameType.Dns || IPAddress.TryParse(uri.IdnHost, out _)) return null;
        var domain = uri.IdnHost.ToLowerInvariant().TrimEnd('.');
        if (domain.Length > 253 || !domain.Contains('.') || domain.Split('.').Any(p => p.Length is 0 or > 63) ||
            !domain.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-')) return null;
        string? detail = null;
        if (details)
        {
            var path = Uri.UnescapeDataString(uri.AbsolutePath);
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var safePath = SensitiveRoute().IsMatch(path) ? "/[redacted]" : "/" + string.Join('/', segments.Take(16).Select(s =>
                s.Length > 24 || Secret().IsMatch(s) || s.Any(c => !char.IsLetter(c) && c is not ('-' or '_'))
                    ? "[redacted]" : Uri.EscapeDataString(s)));
            detail = uri.GetLeftPart(UriPartial.Authority) + safePath;
            if (detail.Length > 1024) detail = null;
        }
        var (engine, parameter) = SearchSource(domain, uri.AbsolutePath);
        string? query = null;
        if (searches && parameter is not null)
        {
            var values = uri.Query.TrimStart('?').Split('&').Select(x => x.Split('=', 2)).Where(x => x.Length == 2 && x[0] == parameter).ToArray();
            if (values.Length == 1)
            {
                var candidate = Uri.UnescapeDataString(values[0][1].Replace('+', ' ')).Trim();
                if (candidate.Length is > 0 and <= 240 && !candidate.Any(char.IsControl) && !Secret().IsMatch(candidate)) query = candidate;
            }
        }
        return new(domain, detail, query, query is null ? null : engine);
    }

    // Defence in depth for messages already sanitized by the extension.
    public static BrowserPage? Validate(BrowserPage page, BrowserOptions options)
    {
        if((!options.DetailedUrls&&page.DetailUrl is not null)||(!options.SearchQueries&&page.SearchQuery is not null))return null;
        var basic = FromUrl("https://" + page.Domain + "/", false, false);
        if (basic is null || basic.Domain != page.Domain) return null;
        string? detail = null;
        if (options.DetailedUrls && page.DetailUrl is not null)
        {
            var cleaned = FromUrl(page.DetailUrl, true, false);
            if (cleaned is null || cleaned.Domain != page.Domain || page.DetailUrl.Contains('?') || page.DetailUrl.Contains('#')) return null;
            detail = cleaned.DetailUrl;
        }
        string? search = null, engine = null;
        if (options.SearchQueries && page.SearchQuery is { Length: > 0 and <= 240 } q &&
            !q.Any(char.IsControl) && !Secret().IsMatch(q) && KnownEngine(page.Domain, page.SearchEngine))
        { search = q; engine = page.SearchEngine; }
        return new(basic.Domain, detail, search, engine);
    }

    private static (string? Engine, string? Parameter) SearchSource(string domain, string path)
    {
        if (new[] { "google.com", "www.google.com", "google.com.tr", "www.google.com.tr", "google.co.uk", "www.google.co.uk" }.Contains(domain) && path == "/search") return ("Google", "q");
        if (domain is "bing.com" or "www.bing.com" && path == "/search") return ("Bing", "q");
        if (domain is "duckduckgo.com" or "www.duckduckgo.com" && path == "/") return ("DuckDuckGo", "q");
        if (domain == "search.yahoo.com" && path == "/search") return ("Yahoo", "p");
        return (null, null);
    }
    private static bool KnownEngine(string domain, string? engine) => new[] { "/", "/search" }.Any(path => SearchSource(domain, path).Engine == engine && engine is not null);
}
