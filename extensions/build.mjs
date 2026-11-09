import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";
import { fileURLToPath } from "node:url";
const root = path.dirname(fileURLToPath(import.meta.url));
const identity = JSON.parse(fs.readFileSync(path.join(root, "identity.json"), "utf8"));
const calculated = [...crypto.createHash("sha256").update(Buffer.from(identity.chromiumPublicKey, "base64")).digest().subarray(0, 16)]
    .map(b => String.fromCharCode(97 + (b >> 4), 97 + (b & 15))).join("");
if (calculated !== identity.chromiumId) throw new Error("Extension public key and ID differ.");
const constants = fs.readFileSync(path.join(root, "../src/FamilyMonitor.Core/Browsers/BrowserModels.cs"), "utf8");
if (!constants.includes(identity.chromiumId) || !constants.includes(identity.firefoxId)) throw new Error("Native host and extension identities differ.");
const bundle = names => names.map(name => fs.readFileSync(path.join(root, ".build/modules", name + ".js"), "utf8")).join("\n");
fs.writeFileSync(path.join(root, ".build/background.js"), bundle(["protocol", "privacy", "platform", "background"]));
fs.writeFileSync(path.join(root, ".build/popup.js"), bundle(["protocol", "platform", "popup"]));
fs.writeFileSync(path.join(root, ".build/core.js"), bundle(["protocol", "privacy"]));
for (const browser of ["chrome", "edge", "firefox"]) {
    const target = path.join(root, "../artifacts/extensions-2.0.0-prototype", browser);
    fs.mkdirSync(target, { recursive: true });
    const manifest = {
        manifest_version: 3,
        name: "Family Activity Monitor — " + browser,
        version: "2.0.0",
        version_name: "2.0.0-prototype",
        description: "Açık izinle, yalnızca yerel Windows Agent'a etkin sekmenin izinli metaverisini iletir.",
        permissions: ["storage", "alarms", "nativeMessaging"],
        optional_permissions: ["tabs"],
        incognito: "not_allowed",
        action: { default_popup: "popup.html" },
        content_security_policy: { extension_pages: "script-src 'self'; object-src 'none'; connect-src 'none';" }
    };
    if (browser === "firefox") {
        manifest.background = { scripts: ["background.js"] };
        manifest.browser_specific_settings = { gecko: {
            id: identity.firefoxId, strict_min_version: "140.0",
            data_collection_permissions: { required: ["none"], optional: ["browsingActivity", "searchTerms"] }
        } };
        // version_name is Chromium-specific; no unsupported Firefox manifest warning.
        delete manifest.version_name;
    } else {
        manifest.background = { service_worker: "background.js" };
        manifest.minimum_chrome_version = "120";
        manifest.key = identity.chromiumPublicKey;
    }
    fs.writeFileSync(path.join(target, "manifest.json"), JSON.stringify(manifest, null, 2) + "\n");
    for (const file of ["background.js", "popup.js"])
        fs.writeFileSync(path.join(target, file), "const FAMILY_MONITOR_BROWSER = " + JSON.stringify(browser) + ";\n" +
            fs.readFileSync(path.join(root, ".build", file), "utf8"));
    for (const file of ["popup.html", "popup.css"]) fs.copyFileSync(path.join(root, file), path.join(target, file));
}
console.log("Built Chrome/Edge MV3 and Firefox MV3 event-page prototypes; no browser/registry installation performed.");
