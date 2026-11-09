namespace FamilyMonitorBrowser {
    const sensitiveRoute = /token|session|password|passwd|secret|oauth|authorize|callback|credential|reset|verify|invitation|invite|login|auth|api[-_]?key/i;
    const secret = /gh[pousr]_[a-z0-9_]{16,}|github_pat_[a-z0-9_]{16,}|eyJ[a-z0-9_-]+\.[a-z0-9_-]+\.[a-z0-9_-]+|(?:password|passwd|secret|access_token|api_key)\s*[:=]|[a-z0-9_-]{32,}/i;
    export function cleanUrl(value: unknown, details: boolean, searches: boolean): Page | null {
        if (typeof value !== "string" || value.length > 8192) return null;
        try {
            const u = new URL(value);
            if (!["https:", "http:"].includes(u.protocol) || u.username || u.password) return null;
            const domain = u.hostname.toLowerCase().replace(/\.$/, "");
            if (domain.length > 253 || !domain.includes(".") || !/^[a-z0-9.-]+$/.test(domain) ||
                /^[0-9.]+$/.test(domain) || domain.split(".").some(p => p.length === 0 || p.length > 63)) return null;
            let detailUrl: string | null = null;
            if (details) {
                const path = decodeURIComponent(u.pathname);
                const segments = path.split("/").filter(Boolean).slice(0, 16);
                const safe = sensitiveRoute.test(path) ? "/[redacted]" : "/" + segments.map(s =>
                    s.length > 24 || secret.test(s) || /[^\p{L}_-]/u.test(s) ? "[redacted]" : encodeURIComponent(s)).join("/");
                detailUrl = u.origin + safe;
                if (detailUrl.length > 1024) detailUrl = null;
            }
            let searchEngine: string | null = null, parameter: string | null = null, searchQuery: string | null = null;
            if (["google.com", "www.google.com", "google.com.tr", "www.google.com.tr", "google.co.uk", "www.google.co.uk"].includes(domain) && u.pathname === "/search") {
                searchEngine = "Google"; parameter = "q";
            } else if (["bing.com", "www.bing.com"].includes(domain) && u.pathname === "/search") {
                searchEngine = "Bing"; parameter = "q";
            } else if (["duckduckgo.com", "www.duckduckgo.com"].includes(domain) && u.pathname === "/") {
                searchEngine = "DuckDuckGo"; parameter = "q";
            } else if (domain === "search.yahoo.com" && u.pathname === "/search") {
                searchEngine = "Yahoo"; parameter = "p";
            }
            if (searches && parameter !== null) {
                const values = u.searchParams.getAll(parameter);
                const q = values.length === 1 ? values[0].trim() : "";
                if (q.length > 0 && q.length <= 240 && !/[\u0000-\u001f\u007f-\u009f]/.test(q) && !secret.test(q)) searchQuery = q;
            }
            return { domain, detailUrl, searchQuery, searchEngine: searchQuery === null ? null : searchEngine };
        } catch { return null; }
    }
}
