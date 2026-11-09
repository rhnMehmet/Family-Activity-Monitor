import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import path from "node:path";
import { fileURLToPath } from "node:url";
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
function pure() {
    const context = vm.createContext({ URL, URLSearchParams });
    vm.runInContext(fs.readFileSync(path.join(root, ".build/core.js"), "utf8"), context);
    return context.FamilyMonitorBrowser;
}
const plain = x => JSON.parse(JSON.stringify(x));
const event = () => ({ listeners: [], addListener(callback) { this.listeners.push(callback); }, fire(...args) { for (const listener of this.listeners) listener(...args); } });
const settle = () => new Promise(resolve => setImmediate(resolve));
async function fixture(kind = "chrome", saved = { enabled: true, details: false, searches: false }) {
    const identity = JSON.parse(fs.readFileSync(path.join(root, "identity.json"), "utf8"));
    const timers = new Map(), intervals = new Map(), ports = [], alarms = [], counts = { windows: 0, tabs: 0 };
    let next = 1, choice = saved;
    const state = { windows: [{ id: 1, focused: true, incognito: false }], tabs: [{ id: 11, windowId: 1, incognito: false, url: "https://example.com/path?token=dontsend#fragment" }] };
    const api = {
        runtime: {
            id: kind === "firefox" ? identity.firefoxId : identity.chromiumId,
            onMessage: event(),
            connectNative(name) {
                const port = { name, sent: [], onMessage: event(), onDisconnect: event(),
                    postMessage(message) { this.sent.push(plain(message)); }, disconnect() { this.onDisconnect.fire(); } };
                ports.push(port); return port;
            }
        },
        storage: { local: { async get() { return { choices: choice }; }, async set(value) { choice = value.choices; } }, onChanged: event() },
        tabs: { async query() { counts.tabs++; return state.tabs; }, onActivated: event(), onUpdated: event(), onRemoved: event(), onAttached: event() },
        windows: { async getAll() { counts.windows++; return state.windows; }, onFocusChanged: event(), onRemoved: event() },
        permissions: { async getAll() { return { permissions: ["tabs"], data_collection: ["browsingActivity", "searchTerms"] }; }, onRemoved: event() },
        alarms: { create(name, options) { alarms.push({ name, ...options }); }, async clear() { return true; }, onAlarm: event() }
    };
    const context = vm.createContext({
        URL, URLSearchParams, FAMILY_MONITOR_BROWSER: kind, chrome: api,
        setTimeout(callback) { const id = next++; timers.set(id, callback); return id; },
        clearTimeout(id) { timers.delete(id); },
        setInterval(callback) { const id = next++; intervals.set(id, callback); return id; },
        clearInterval(id) { intervals.delete(id); }
    });
    vm.runInContext(fs.readFileSync(path.join(root, ".build/background.js"), "utf8"), context);
    await settle();
    return {
        api, ports, alarms, counts, state, timers, intervals, bridge: context.FamilyMonitorBrowser.bridge,
        policy(enabled = true, epoch = "a".repeat(32), detailedUrls = false, searchQueries = false) {
            ports.at(-1).onMessage.fire({ version: 1, type: "policy", policy: { enabled, detailedUrls, searchQueries, epoch } });
        },
        async flush() { const callbacks = [...timers.values()]; timers.clear(); callbacks.forEach(callback => callback()); await settle(); },
        async choices(value) { choice = value; api.storage.onChanged.fire({ choices: { newValue: value } }, "local"); await settle(); },
        snapshots() { return ports.flatMap(port => port.sent).filter(m => m.type === "snapshot"); }
    };
}
test("all choices default off and no unconsented metadata is returned", () => {
    const core = pure(); assert.deepEqual(plain(core.choices(null)), { enabled: false, details: false, searches: false });
    assert.deepEqual(plain(core.cleanUrl("https://example.com/private?q=secret#part", false, false)),
        { domain: "example.com", detailUrl: null, searchQuery: null, searchEngine: null });
});
for (const url of ["file:///C:/private.txt", "chrome://settings", "about:privatebrowsing", "javascript:alert(1)", "https://user:password@example.com/", "http://127.0.0.1/"]) {
    test("reject URL scheme/address: " + url.split(":")[0] + " " + url.length, () => assert.equal(pure().cleanUrl(url, true, true), null));
}
test("detailed paths remove query, fragment and sensitive route/opaque ID", () => {
    const core = pure();
    const detail = core.cleanUrl("https://example.com/articles/1234?access_token=private#fragment", true, false);
    assert.equal(detail.detailUrl, "https://example.com/articles/[redacted]");
    assert.equal(core.cleanUrl("https://example.com/reset/smallsecret", true, false).detailUrl, "https://example.com/[redacted]");
});
for (const [url, engine, query] of [
    ["https://google.com/search?q=hello+world", "Google", "hello world"],
    ["https://bing.com/search?q=hello", "Bing", "hello"],
    ["https://duckduckgo.com/?q=test", "DuckDuckGo", "test"],
    ["https://search.yahoo.com/search?p=test", "Yahoo", "test"]
]) {
    test("search allowlist and separate consent: " + engine, () => {
        const core = pure(); assert.equal(core.cleanUrl(url, false, false).searchQuery, null);
        assert.equal(core.cleanUrl(url, false, true).searchEngine, engine);
        assert.equal(core.cleanUrl(url, false, true).searchQuery, query);
    });
}
test("unknown engines, non-results paths, duplicated terms and recognizable secrets are excluded", () => {
    const core = pure();
    for (const url of ["https://example.com/search?q=private", "https://google.com/profile?q=private",
        "https://google.com/search?q=one&q=two", "https://google.com/search?q=password%3Dprivate"])
        assert.equal(core.cleanUrl(url, false, true).searchQuery, null);
});
for (const kind of ["chrome", "edge", "firefox"]) {
    test(kind + " uses matching native host, opt-in focus and sanitized active tab", async () => {
        const f = await fixture(kind); assert.equal(f.ports[0].name, "com.familymonitor." + kind);
        assert.deepEqual(f.counts, { windows: 0, tabs: 0 });
        f.policy(); await f.flush(); const snapshot = f.snapshots()[0];
        assert.equal(snapshot.page.domain, "example.com"); assert.equal(snapshot.page.detailUrl, null);
        assert.equal(snapshot.tabId, 11); assert.equal(snapshot.windowId, 1);
        assert.ok(!JSON.stringify(snapshot).includes("dontsend")); assert.ok(!JSON.stringify(snapshot).includes("fragment"));
    });
}
test("disabled extension does not spawn native process or query tabs", async () => {
    const f = await fixture("chrome", { enabled: false, details: false, searches: false });
    f.api.tabs.onActivated.fire(); await f.flush();
    assert.equal(f.ports.length, 0); assert.deepEqual(f.counts, { windows: 0, tabs: 0 });
});
test("background/gaming policy stops all metadata reads immediately", async () => {
    const f = await fixture(); f.policy(); await f.flush(); const before = { ...f.counts };
    f.policy(false, "b".repeat(32)); f.api.tabs.onActivated.fire(); f.api.windows.onFocusChanged.fire(); await f.flush();
    assert.deepEqual(f.counts, before);
});
test("private windows/tabs never appear in payloads", async () => {
    const f = await fixture(); f.state.tabs[0].incognito = true; f.state.tabs[0].url = "https://private.example/search?q=never";
    f.policy(); await f.flush(); assert.equal(f.snapshots()[0].page, null);
    assert.ok(!JSON.stringify(f.snapshots()).includes("private.example"));
    f.state.windows[0].incognito = true; f.api.windows.onFocusChanged.fire(); await f.flush();
    assert.equal(f.snapshots().at(-1).focused, false);
});
test("event storms coalesce and unchanged snapshots are not resent", async () => {
    const f = await fixture(); f.policy();
    for (let i = 0; i < 10000; i++) f.api.tabs.onActivated.fire();
    assert.equal(f.timers.size, 1); await f.flush(); assert.equal(f.counts.tabs, 1); assert.equal(f.snapshots().length, 1);
    f.api.tabs.onActivated.fire(); await f.flush(); assert.equal(f.snapshots().length, 1);
});
test("new window, tab or challenge sends fresh snapshot", async () => {
    const f = await fixture(); f.policy(); await f.flush();
    f.state.windows[0].id = 2; f.state.tabs[0].windowId = 2; f.state.tabs[0].id = 22;
    f.api.windows.onFocusChanged.fire(); await f.flush(); assert.equal(f.snapshots().at(-1).tabId, 22);
    f.policy(true, "b".repeat(32)); await f.flush(); assert.equal(f.snapshots().at(-1).epoch, "b".repeat(32));
});
test("URL and search transmission require both local and Agent permissions", async () => {
    const f = await fixture("chrome", { enabled: true, details: true, searches: true });
    f.state.tabs[0].url = "https://google.com/search?q=merhaba&token=never";
    f.policy(true, "a".repeat(32), false, false); await f.flush(); assert.equal(f.snapshots()[0].page.searchQuery, null);
    f.policy(true, "b".repeat(32), true, true); await f.flush();
    const page = f.snapshots().at(-1).page; assert.equal(page.detailUrl, "https://google.com/search"); assert.equal(page.searchQuery, "merhaba");
    assert.ok(!JSON.stringify(page).includes("never"));
});
test("disconnect clears policy/timers and schedules bounded exponential reconnect", async () => {
    const f = await fixture(); f.policy(); await f.flush(); f.ports[0].onDisconnect.fire();
    assert.equal(f.intervals.size, 0); const before = { ...f.counts }; f.api.tabs.onActivated.fire(); await f.flush(); assert.deepEqual(f.counts, before);
    assert.equal(f.alarms[0].delayInMinutes, 1);
    for (let i = 0; i < 6; i++) { f.api.alarms.onAlarm.fire({ name: "reconnect" }); f.ports.at(-1).onDisconnect.fire(); }
    assert.equal(f.alarms.at(-1).delayInMinutes, 15);
});
test("heartbeat carries no site, URL or query and does not poll tabs", async () => {
    const f = await fixture(); f.policy(); await f.flush(); const before = { ...f.counts };
    for (const callback of f.intervals.values()) callback();
    const heartbeat = f.ports[0].sent.at(-1); assert.deepEqual(Object.keys(heartbeat).sort(), ["seq", "type", "version"]);
    assert.deepEqual(f.counts, before);
});
test("revocation during an asynchronous tab query discards its result", async () => {
    const f = await fixture(); let release;
    f.api.tabs.query = () => new Promise(resolve => { release = resolve; });
    f.policy(); await f.flush(); await f.choices({ enabled: false, details: false, searches: false });
    release([{ id: 11, windowId: 1, url: "https://sensitive.example/" }]); await settle();
    assert.equal(f.snapshots().length, 0);
});
test("manifests use distinct MV3 contexts, optional permissions and no content scripts/network", () => {
    for (const kind of ["chrome", "edge", "firefox"]) {
        const manifest = JSON.parse(fs.readFileSync(path.join(root, "../artifacts/extensions-2.0.0-prototype", kind, "manifest.json"), "utf8"));
        assert.equal(manifest.manifest_version, 3); assert.equal(manifest.incognito, "not_allowed");
        assert.deepEqual(manifest.optional_permissions, ["tabs"]); assert.equal(manifest.content_scripts, undefined); assert.equal(manifest.host_permissions, undefined);
        assert.ok(manifest.content_security_policy.extension_pages.includes("connect-src 'none'"));
        if (kind === "firefox") { assert.deepEqual(manifest.background, { scripts: ["background.js"] }); assert.equal(manifest.browser_specific_settings.gecko.strict_min_version, "140.0"); }
        else assert.deepEqual(manifest.background, { service_worker: "background.js" });
    }
});
