namespace FamilyMonitorBrowser {
    const api = platform(), kind = FAMILY_MONITOR_BROWSER;
    const enabled = document.getElementById("enabled") as HTMLInputElement;
    const details = document.getElementById("details") as HTMLInputElement;
    const searches = document.getElementById("searches") as HTMLInputElement;
    const status = document.getElementById("status")!;
    async function load(): Promise<void> {
        const saved = choices((await api.storage.local.get("choices")).choices);
        enabled.checked = saved.enabled; details.checked = saved.details; searches.checked = saved.searches;
        try {
            const response = await api.runtime.sendMessage({ type: "status" });
            status.textContent = object(response) && typeof response.status === "string" ? response.status : "Agent bağlantısı bekleniyor.";
        } catch { status.textContent = "Agent bağlantısı yok; veri gönderilmiyor."; }
    }
    document.getElementById("save")!.addEventListener("click", async () => {
        const requested: Choices = { enabled: enabled.checked, details: enabled.checked && details.checked, searches: enabled.checked && searches.checked };
        // Invoke request directly in this user gesture, before another await loses activation.
        const grant: Permissions = { permissions: ["tabs"] };
        if (kind === "firefox") grant.data_collection = ["browsingActivity", ...(requested.searches ? ["searchTerms"] : [])];
        const permission = requested.enabled ? api.permissions.request(grant) : Promise.resolve(true);
        if (!await permission) { status.textContent = "Tarayıcı izni verilmedi; seçim kaydedilmedi."; return; }
        await api.storage.local.set({ choices: requested });
        await api.runtime.sendMessage({ type: "reload" });
        await load();
    });
    document.getElementById("stop")!.addEventListener("click", async () => {
        await api.storage.local.set({ choices: { ...defaults } }); await api.runtime.sendMessage({ type: "reload" }); await load();
    });
    document.getElementById("reconnect")!.addEventListener("click", async () => {
        await api.runtime.sendMessage({ type: "reconnect" }); await load();
    });
    void load();
}
