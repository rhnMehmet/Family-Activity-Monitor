namespace FamilyMonitorBrowser {
    export class Bridge {
        private choice: Choices = { ...defaults };
        private policy: Policy = { ...off };
        private port: NativePort | null = null;
        private sequence = 0;
        private generation = 0;
        private lastSent = "";
        private timer: ReturnType<typeof setTimeout> | null = null;
        private heartbeat: ReturnType<typeof setInterval> | null = null;
        private reading = false;
        private again = false;
        private reconnectAttempt = 0;
        private grants: Permissions = {};
        private status = "Kapalı; veri gönderilmiyor.";
        constructor(private readonly api: Api, private readonly kind: Kind) {
            api.tabs.onActivated.addListener(() => this.schedule());
            api.tabs.onUpdated.addListener((_id, changes, tab) => { if (!tab.incognito && (changes.url !== undefined || changes.status !== undefined)) this.schedule(); });
            api.tabs.onRemoved.addListener(() => this.schedule());
            api.tabs.onAttached.addListener(() => this.schedule());
            api.windows.onFocusChanged.addListener(() => this.schedule());
            api.windows.onRemoved.addListener(() => this.schedule());
            api.storage.onChanged.addListener((change, area) => {
                if (area !== "local" || !("choices" in change)) return;
                this.stop();
                this.choice = choices(object(change.choices) ? change.choices.newValue : null);
                void this.start();
            });
            api.permissions.onRemoved.addListener(() => { this.stop(); this.grants = {}; void this.start(); });
            api.alarms.onAlarm.addListener(alarm => { if (alarm.name === "reconnect") this.connect(); });
            api.runtime.onMessage.addListener((message, sender, respond) => {
                if (sender.id !== api.runtime.id || !object(message)) return;
                if (message.type === "status") respond({ status: this.status, connected: this.port !== null, enabled: this.policy.enabled && this.choice.enabled });
                else if (message.type === "reload") { void this.start().then(() => respond({ ok: true })); return true; }
                else if (message.type === "reconnect") { this.stop(); this.reconnectAttempt = 0; this.connect(); respond({ ok: true }); }
            });
        }
        async start(): Promise<void> {
            const request = ++this.generation;
            this.policy = { ...off }; this.lastSent = "";
            const [saved, grants] = await Promise.all([this.api.storage.local.get("choices"), this.api.permissions.getAll()]);
            if (request !== this.generation) return;
            this.choice = choices(saved.choices); this.grants = grants;
            if (!this.choice.enabled || !this.allowed() || !identityMatches(this.api, this.kind)) { this.stop(); return; }
            if (this.port) { this.stop(); }
            this.connect();
        }
        private allowed(): boolean {
            return this.grants.permissions?.includes("tabs") === true &&
                (this.kind !== "firefox" || this.grants.data_collection?.includes("browsingActivity") === true);
        }
        private connect(): void {
            if (this.port || !this.choice.enabled || !this.allowed() || !identityMatches(this.api, this.kind)) return;
            try {
                const port = this.api.runtime.connectNative("com.familymonitor." + this.kind);
                this.port = port; this.sequence = 0; this.policy = { ...off }; this.lastSent = "";
                this.status = "Agent bağlantısı / açık izin bekleniyor.";
                port.onMessage.addListener(message => {
                    if (this.port !== port) return;
                    const policy = policyMessage(message);
                    if (!policy) { this.stop(); return; }
                    this.generation++; this.policy = policy; this.lastSent = ""; this.reconnectAttempt = 0;
                    this.status = policy.enabled ? "Ön plan doğrulaması etkin; yalnızca izinli alanlar gönderilir." : "Agent duraklatılmış, kapalı veya bu tarayıcı ön planda değil.";
                    if (policy.enabled) this.schedule();
                });
                port.onDisconnect.addListener(() => {
                    void this.api.runtime.lastError;
                    if (this.port !== port) return;
                    this.clear(); this.status = "Agent bağlantısı yok; veri gönderilmiyor.";
                    if (this.choice.enabled && this.allowed()) {
                        const minutes = [1, 2, 5, 15][Math.min(this.reconnectAttempt++, 3)];
                        void this.api.alarms.create("reconnect", { delayInMinutes: minutes });
                    }
                });
                this.send({ version: 1, type: "ready", seq: ++this.sequence });
                this.heartbeat = setInterval(() => {
                    if (this.port) this.send({ version: 1, type: "heartbeat", seq: ++this.sequence });
                }, 10000);
            } catch { this.clear(); this.status = "Native host kullanılamıyor; veri gönderilmiyor."; }
        }
        private clear(): void {
            this.generation++; this.port = null; this.policy = { ...off }; this.lastSent = "";
            if (this.timer !== null) clearTimeout(this.timer);
            if (this.heartbeat !== null) clearInterval(this.heartbeat);
            this.timer = null; this.heartbeat = null;
        }
        stop(): void {
            const port = this.port; this.clear(); port?.disconnect();
            void this.api.alarms.clear("reconnect");
            this.status = "Kapalı veya izin eksik; veri gönderilmiyor.";
        }
        private send(message: unknown): void {
            try { this.port?.postMessage(message); } catch { this.stop(); }
        }
        private schedule(): void {
            if (!this.policy.enabled || !this.choice.enabled || !this.allowed()) return;
            if (this.reading) { this.again = true; return; }
            if (this.timer !== null) return;
            this.timer = setTimeout(() => { this.timer = null; void this.collect(); }, 250);
        }
        private async collect(): Promise<void> {
            if (this.reading || !this.policy.enabled || !this.choice.enabled || !this.allowed()) return;
            this.reading = true;
            const generation = this.generation, epoch = this.policy.epoch;
            try {
                const windows = (await this.api.windows.getAll({ windowTypes: ["normal"] })).filter(w => w.focused && !w.incognito);
                let windowId = 0, tabId = 0, focused = false, page: Page | null = null;
                if (windows.length === 1 && windows[0].id !== undefined) {
                    windowId = windows[0].id;
                    const tabs = await this.api.tabs.query({ active: true, windowId });
                    if (tabs.length === 1 && !tabs[0].incognito && tabs[0].id !== undefined && tabs[0].windowId === windowId) {
                        tabId = tabs[0].id; focused = true;
                        page = cleanUrl(tabs[0].url, this.choice.details && this.policy.detailedUrls,
                            this.choice.searches && this.policy.searchQueries && (this.kind !== "firefox" || this.grants.data_collection?.includes("searchTerms") === true));
                    }
                }
                if (generation !== this.generation || !this.policy.enabled || !this.port) return;
                const payload = { version: 1, type: "snapshot", epoch, windowId, tabId, focused, private: false, page };
                const fingerprint = JSON.stringify(payload);
                if (fingerprint !== this.lastSent) { this.lastSent = fingerprint; this.send({ ...payload, seq: ++this.sequence }); }
            } catch {
                if (generation === this.generation && this.port && this.policy.enabled)
                    this.send({ version: 1, type: "snapshot", seq: ++this.sequence, epoch, windowId: 0, tabId: 0, focused: false, private: false, page: null });
            } finally {
                this.reading = false;
                if (this.again) { this.again = false; this.schedule(); }
            }
        }
    }
    export const bridge = new Bridge(platform(), FAMILY_MONITOR_BROWSER);
    void bridge.start();
}
