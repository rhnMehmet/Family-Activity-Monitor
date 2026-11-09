namespace FamilyMonitorBrowser {
    export interface Event<T extends (...args: never[]) => unknown> { addListener(callback: T): void; }
    export interface NativePort {
        postMessage(message: unknown): void;
        disconnect(): void;
        onMessage: { addListener(callback: (message: unknown) => void): void };
        onDisconnect: { addListener(callback: () => void): void };
    }
    export interface Tab { id?: number; windowId?: number; incognito?: boolean; url?: string; }
    export interface WindowInfo { id?: number; focused?: boolean; incognito?: boolean; }
    export interface Permissions { permissions?: string[]; data_collection?: string[]; }
    export interface Api {
        runtime: {
            id: string;
            lastError?: { message?: string };
            connectNative(name: string): NativePort;
            sendMessage(message: unknown): Promise<unknown>;
            onMessage: { addListener(callback: (message: unknown, sender: { id?: string }, respond: (value: unknown) => void) => boolean | void): void };
        };
        storage: {
            local: { get(key: string): Promise<Record<string, unknown>>; set(items: Record<string, unknown>): Promise<void> };
            onChanged: { addListener(callback: (changes: Record<string, unknown>, area: string) => void): void };
        };
        tabs: {
            query(query: { active: true; windowId: number }): Promise<Tab[]>;
            onActivated: { addListener(callback: () => void): void };
            onUpdated: { addListener(callback: (id: number, changes: { url?: string; status?: string }, tab: Tab) => void): void };
            onRemoved: { addListener(callback: () => void): void };
            onAttached: { addListener(callback: () => void): void };
        };
        windows: {
            getAll(options: { windowTypes: string[] }): Promise<WindowInfo[]>;
            onFocusChanged: { addListener(callback: () => void): void };
            onRemoved: { addListener(callback: () => void): void };
        };
        permissions: {
            getAll(): Promise<Permissions>;
            request(permissions: Permissions): Promise<boolean>;
            onRemoved: { addListener(callback: () => void): void };
        };
        alarms: {
            create(name: string, options: { delayInMinutes: number }): Promise<void> | void;
            clear(name: string): Promise<boolean>;
            onAlarm: { addListener(callback: (alarm: { name: string }) => void): void };
        };
    }
    export function platform(): Api {
        const g = globalThis as unknown as { browser?: Api; chrome?: Api };
        const api = g.browser ?? g.chrome;
        if (!api) throw new Error("Extension API unavailable.");
        return api;
    }
    export function identityMatches(api: Api, kind: Kind): boolean {
        return api.runtime.id === (kind === "firefox" ? firefoxId : chromiumId);
    }
}
