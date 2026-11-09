declare const FAMILY_MONITOR_BROWSER: "chrome" | "edge" | "firefox";

namespace FamilyMonitorBrowser {
    export type Kind = "chrome" | "edge" | "firefox";
    export interface Choices { enabled: boolean; details: boolean; searches: boolean; }
    export interface Policy { enabled: boolean; detailedUrls: boolean; searchQueries: boolean; epoch: string; }
    export interface Page { domain: string; detailUrl: string | null; searchQuery: string | null; searchEngine: string | null; }
    export const defaults: Choices = { enabled: false, details: false, searches: false };
    export const chromiumId = "mcipbngepoajhbkoecbbdiijckmfejol";
    export const firefoxId = "family-monitor@local.familyactivitymonitor";
    export const off: Policy = { enabled: false, detailedUrls: false, searchQueries: false, epoch: "" };
    export function object(value: unknown): value is Record<string, unknown> {
        return value !== null && typeof value === "object" && !Array.isArray(value);
    }
    export function policyMessage(value: unknown): Policy | null {
        if (!object(value) || value.version !== 1 || value.type !== "policy" || !object(value.policy)) return null;
        if (Object.keys(value).some(k => !["version", "type", "policy"].includes(k))) return null;
        const p = value.policy;
        if (Object.keys(p).length !== 4 || Object.keys(p).some(k => !["enabled", "detailedUrls", "searchQueries", "epoch"].includes(k))) return null;
        if (typeof p.enabled !== "boolean" || typeof p.detailedUrls !== "boolean" || typeof p.searchQueries !== "boolean" ||
            typeof p.epoch !== "string" || !/^[a-f0-9]{32}$/.test(p.epoch)) return null;
        return { enabled: p.enabled, detailedUrls: p.enabled && p.detailedUrls, searchQueries: p.enabled && p.searchQueries, epoch: p.epoch };
    }
    export function choices(value: unknown): Choices {
        if (!object(value)) return { ...defaults };
        return { enabled: value.enabled === true, details: value.enabled === true && value.details === true,
            searches: value.enabled === true && value.searches === true };
    }
}
