// App Store Connect — iOS in-app-purchase (subscription) tooling for the Connect app.
//
// Why this exists: the app expects two auto-renewable subscriptions to exist in App Store Connect
// (the StoreKit product IS the plan+cycle — see AppStoreBillingProvider). This script talks to the
// App Store Connect REST API to (a) REPORT what is configured today and (b) later CREATE/PRICE the
// two products. It is deliberately zero-dependency (Node built-ins only: crypto for the ES256 JWT,
// global fetch for HTTP) so CI needs no `npm install` — the same posture as the WebUI repo's
// store-asc-screenshots.mjs.
//
// Auth: the three standard secrets this repo already uses for the App Store (see .github/DEPLOYMENT.md),
// read from the environment:
//   APPSTORE_CONNECT_API_KEY      the .p8 private-key CONTENTS (PEM)
//   APPSTORE_CONNECT_API_KEY_ID   the key id (the 10-char id, e.g. from the .p8 filename)
//   APPSTORE_CONNECT_ISSUER_ID    the issuer id (UUID)
//
// Usage:
//   node asc-iap.mjs report [--bundle-id com.vpnhood.connect.ios]   # read-only; never mutates
//   node asc-iap.mjs apply  ...                                     # create/price (see below)
//
// The two products the app requires. Since the "rename the app abstractions" refactor the app carries
// NO embedded fallback ids at all — PortalAccountProvider.GetProductIds is the only catalog, so these
// must match the WHMCS vpnhoodiap plan mapping for store "appstore" + package com.vpnhood.connect.ios
// EXACTLY. A product that exists in App Store Connect but is unmapped in the portal can be charged for
// and then has nowhere to be redeemed; one mapped but absent here simply cannot be sold.
const DEFAULT_BUNDLE_ID = "com.vpnhood.connect.ios";
const REQUIRED = [
    { productId: "vpnhood_1_month_subscription", period: "ONE_MONTH", label: "Monthly" },
    { productId: "vpnhood_1_year_subscription", period: "ONE_YEAR", label: "Yearly" }
];

import crypto from "node:crypto";

const API = "https://api.appstoreconnect.apple.com";

// ------------------------------------------------------------------ auth --

function makeToken() {
    const keyId = requireEnv("APPSTORE_CONNECT_API_KEY_ID");
    const issuerId = requireEnv("APPSTORE_CONNECT_ISSUER_ID");
    const pem = requireEnv("APPSTORE_CONNECT_API_KEY");

    const header = { alg: "ES256", kid: keyId, typ: "JWT" };
    const now = Math.floor(Date.now() / 1000);
    // 20 min is the max ASC accepts; the whole run is far shorter.
    const payload = { iss: issuerId, iat: now, exp: now + 20 * 60, aud: "appstoreconnect-v1" };
    const signingInput = `${b64url(JSON.stringify(header))}.${b64url(JSON.stringify(payload))}`;
    const key = crypto.createPrivateKey(pem);
    // ES256 wants the raw r||s (JOSE) signature, not DER — dsaEncoding:'ieee-p1363' yields exactly that.
    const sig = crypto.sign("sha256", Buffer.from(signingInput), { key, dsaEncoding: "ieee-p1363" });
    return `${signingInput}.${sig.toString("base64url")}`;
}

function requireEnv(name) {
    const v = process.env[name];
    if (!v || !v.trim())
        fail(`Missing required environment variable ${name}. See .github/DEPLOYMENT.md for the App Store secrets.`);
    return v.trim();
}

function b64url(s) {
    return Buffer.from(s).toString("base64url");
}

// ------------------------------------------------------------------- http --

let TOKEN;

async function api(path, { method = "GET", body } = {}) {
    const url = path.startsWith("http") ? path : API + path;
    const res = await fetch(url, {
        method,
        headers: { Authorization: `Bearer ${TOKEN}`, "Content-Type": "application/json" },
        body: body ? JSON.stringify(body) : undefined
    });
    const text = await res.text();
    let json = {};
    try { json = text ? JSON.parse(text) : {}; } catch { json = { raw: text }; }
    if (!res.ok) {
        const errs = (json.errors || [])
            .map(e => `${e.status} ${e.code} — ${e.title}${e.detail ? `: ${e.detail}` : ""}`)
            .join(" | ");
        throw new Error(`ASC ${method} ${url.replace(API, "")} → HTTP ${res.status}${errs ? `\n    ${errs}` : `\n    ${text}`}`);
    }
    return json;
}

// GETs every page, concatenating `data`; keeps the `included` from every page for lookups.
async function apiAll(path) {
    let out = [];
    let included = [];
    let next = path;
    while (next) {
        const page = await api(next);
        out = out.concat(page.data || []);
        if (page.included) included = included.concat(page.included);
        next = page.links?.next || null;
    }
    return { data: out, included };
}

// ----------------------------------------------------------------- report --

async function report() {
    line();
    log(`App Store Connect · in-app-purchase inventory · ${BUNDLE_ID}`);
    line();

    // 1) Resolve the app. A key scoped without access to this app (or a wrong bundle id) surfaces here.
    const apps = await api(`/v1/apps?filter[bundleId]=${encodeURIComponent(BUNDLE_ID)}&limit=1`);
    const app = apps.data?.[0];
    if (!app)
        fail(`App ${BUNDLE_ID} not found for this API key. Check the key's access (Users and Access → Integrations) and that the app record exists.`);
    log(`App: "${app.attributes.name}" (id ${app.id}, sku ${app.attributes.sku || "—"})`);

    // 2) Subscription groups. Zero groups = nothing has been set up yet (the common first-run state).
    const groups = (await apiAll(`/v1/apps/${app.id}/subscriptionGroups?limit=200`)).data;
    log(`Subscription groups: ${groups.length}`);

    // 3) Gather every subscription across all groups, keyed by product id.
    const byProduct = new Map();
    for (const g of groups) {
        const subs = (await apiAll(`/v1/subscriptionGroups/${g.id}/subscriptions?limit=200`)).data;
        log(`  • group "${g.attributes.referenceName}" (id ${g.id}) — ${subs.length} subscription(s)`);
        for (const s of subs) byProduct.set(s.attributes.productId, { sub: s, group: g });
    }

    // 4) Line up the two products the app needs against what exists.
    line();
    log("Required products:");
    const todo = [];
    for (const req of REQUIRED) {
        const found = byProduct.get(req.productId);
        if (!found) {
            log(`  ✗ ${req.label.padEnd(8)} ${req.productId} — MISSING`);
            todo.push(`Create subscription ${req.productId} (${req.period})`);
            continue;
        }
        const a = found.sub.attributes;
        const periodOk = a.subscriptionPeriod === req.period;
        log(`  ✓ ${req.label.padEnd(8)} ${req.productId}`);
        log(`      state=${a.state}  period=${a.subscriptionPeriod}${periodOk ? "" : ` (EXPECTED ${req.period})`}  name="${a.name}"  group="${found.group.attributes.referenceName}"`);
        if (!periodOk) todo.push(`Fix period of ${req.productId}: is ${a.subscriptionPeriod}, expected ${req.period}`);
        if (a.state !== "APPROVED" && a.state !== "READY_TO_SUBMIT")
            todo.push(`${req.productId} is in state ${a.state} — needs metadata/price/review to become sellable`);

        await reportOneSubscription(found.sub, todo);
    }

    // 5) Bottom line.
    line();
    if (todo.length === 0) {
        log("READY: both products exist and look complete. Verify a sandbox purchase end-to-end.");
    } else {
        log("ACTION NEEDED:");
        for (const t of todo) log(`  - ${t}`);
    }
    line();
    log("Manual (not doable via API) — confirm before selling:");
    log("  - Paid Applications Agreement ACTIVE (App Store Connect → Business); banking + tax complete.");
    log("  - A Sandbox test account exists (Users and Access → Sandbox) to test the buy flow.");
    line();
}

// Prices (USA + territory count), localizations, availability, and any free-trial offer for one sub.
async function reportOneSubscription(sub, todo) {
    const id = sub.id;
    const pid = sub.attributes.productId;

    // Prices: pull with the price point + territory included so we can show a real amount.
    try {
        const prices = await apiAll(
            `/v1/subscriptions/${id}/prices?include=subscriptionPricePoint,territory&limit=200`);
        const points = index(prices.included, "subscriptionPricePoints");
        if (prices.data.length === 0) {
            log(`      price: NONE set`);
            todo.push(`Set a price for ${pid}`);
        } else {
            const usa = prices.data.find(p => rel(p, "territory") === "USA");
            const show = usa || prices.data[0];
            const pp = points.get(rel(show, "subscriptionPricePoint"));
            const terr = rel(show, "territory");
            const amount = pp ? `${pp.attributes.customerPrice} (${terr})` : `(${terr})`;
            log(`      price: ${amount} across ${prices.data.length} territor${prices.data.length === 1 ? "y" : "ies"}`);
        }
    } catch (e) {
        log(`      price: (could not read — ${short(e)})`);
    }

    // Localizations: a locale needs BOTH a name and a description. A name-only localization is the
    // most common reason a fully-priced subscription still sits in MISSING_METADATA.
    try {
        const locs = (await apiAll(`/v1/subscriptions/${id}/subscriptionLocalizations?limit=200`)).data;
        if (locs.length === 0) {
            log(`      localizations: NONE`);
            todo.push(`Add a localization (display name + description) for ${pid}`);
        } else {
            const names = locs.slice(0, 4).map(l => `${l.attributes.locale}:"${l.attributes.name}"`).join(", ");
            log(`      localizations: ${locs.length} — ${names}${locs.length > 4 ? " …" : ""}`);
            const noDesc = locs.filter(l => !l.attributes.description);
            if (noDesc.length) {
                log(`      ⚠ no DESCRIPTION in: ${noDesc.map(l => l.attributes.locale).join(", ")}`);
                todo.push(`Add a description (not just a name) for ${pid} in ${noDesc.map(l => l.attributes.locale).join(", ")}`);
            }
        }
    } catch (e) {
        log(`      localizations: (could not read — ${short(e)})`);
    }

    // Review screenshot: Apple requires one per subscription before it can be submitted, and its
    // absence keeps the product in MISSING_METADATA no matter how complete everything else is.
    try {
        const shot = await api(`/v1/subscriptions/${id}/appStoreReviewScreenshot`);
        if (shot.data) {
            const a = shot.data.attributes ?? {};
            log(`      review screenshot: ${a.fileName ?? "present"} (${a.assetDeliveryState?.state ?? "?"})`);
        } else {
            log(`      review screenshot: NONE`);
            todo.push(`Upload an App Review screenshot for ${pid} (required to submit)`);
        }
    } catch {
        log(`      review screenshot: NONE`);
        todo.push(`Upload an App Review screenshot for ${pid} (required to submit)`);
    }

    // Availability: which territories the product is sold in.
    try {
        const avail = await api(
            `/v1/subscriptions/${id}/subscriptionAvailability?include=availableTerritories&limit=1`);
        const all = avail.data?.attributes?.availableInNewTerritories;
        const count = (avail.included || []).filter(x => x.type === "territories").length;
        log(`      availability: ${count} territor${count === 1 ? "y" : "ies"}${all ? " (+ auto new)" : ""}`);
    } catch {
        // availability endpoint 404s until set — not worth surfacing as an error
        log(`      availability: not set`);
    }

    // Intro offer (free trial). Optional; the app already renders TrialPeriodIso when present.
    try {
        const offers = (await apiAll(`/v1/subscriptions/${id}/introductoryOffers?limit=200`)).data;
        if (offers.length) {
            const o = offers[0].attributes;
            log(`      intro offer: ${o.offerMode} ${o.duration}${o.numberOfPeriods ? ` ×${o.numberOfPeriods}` : ""}`);
        }
    } catch { /* none */ }
}

// ----------------------------------------------------- App ID capabilities --

// Sign in with Apple is a DEVELOPER-PORTAL capability on the App ID — a different thing from
// anything configured in App Store Connect's app record (products, pricing, listing). Without it,
// any build whose Entitlements.plist requests com.apple.developer.applesignin fails to sign with
// "MT7140: … the provisioning profile doesn't contain this entitlement".
const APPLE_SIGN_IN = "APPLE_ID_AUTH";

async function resolveBundle() {
    // filter[identifier] matches by PREFIX/substring, so "com.vpnhood.connect.ios" also returns the
    // extension's "…ios.networkextension". Enabling a capability on the wrong App ID would be silent
    // and wrong, so require an EXACT identifier match.
    const res = await apiAll(`/v1/bundleIds?filter[identifier]=${encodeURIComponent(BUNDLE_ID)}&limit=200`);
    const bundle = res.data.find(b => b.attributes.identifier === BUNDLE_ID);
    if (!bundle) {
        const seen = res.data.map(b => b.attributes.identifier).join(", ") || "(none)";
        fail(`No App ID with the exact identifier ${BUNDLE_ID}. Near matches: ${seen}`);
    }
    return bundle;
}

async function capabilities({ enable }) {
    line();
    log(`App ID capabilities · ${BUNDLE_ID}${enable ? "  (enable Sign in with Apple)" : "  (read-only)"}`);
    line();

    const bundle = await resolveBundle();
    log(`App ID: "${bundle.attributes.name}" (${bundle.attributes.identifier}, id ${bundle.id})`);

    // this relationship rejects ?limit — ask for it bare
    const caps = (await api(`/v1/bundleIds/${bundle.id}/bundleIdCapabilities`)).data ?? [];
    const types = caps.map(c => c.attributes.capabilityType).sort();
    log(`Capabilities (${types.length}):`);
    for (const t of types) log(`  - ${t}${t === APPLE_SIGN_IN ? "   <-- Sign in with Apple" : ""}`);

    const hasSignIn = types.includes(APPLE_SIGN_IN);
    line();
    if (hasSignIn) {
        log("✓ Sign in with Apple is ENABLED on this App ID.");
        log("  If a build still fails MT7140, the cached provisioning profile predates this change —");
        log("  regenerate/download the App + Extension profiles (Apple does not update them in place).");
        return;
    }

    log("✗ Sign in with Apple is NOT enabled on this App ID.");
    if (!enable) {
        log("  Re-run with --enable to add it.");
        return;
    }

    log("  Enabling …");
    await api("/v1/bundleIdCapabilities", {
        method: "POST",
        body: {
            data: {
                type: "bundleIdCapabilities",
                attributes: { capabilityType: APPLE_SIGN_IN },
                relationships: { bundleId: { data: { type: "bundleIds", id: bundle.id } } }
            }
        }
    });
    log("✓ Enabled.");
    line();
    log("NEXT — Apple does NOT retrofit existing profiles, so they must be regenerated:");
    log("  • Dev:      delete the stale local profile, then rebuild (automatic provisioning re-fetches).");
    log("  • AppStore: regenerate 'VpnHood Connect AppStore' in the portal, download it, and refresh");
    log("              .user/VpnHoodConnect/ios/ios_provision_app.mobileprovision + the CI secret");
    log("              IOS_PROVISION_APP_BASE64 — otherwise the CI iOS leg fails with the same MT7140.");
}

// --------------------------------------------------------------- apply (stub) --

async function apply() {
    // Intentionally not implemented yet. Creating/pricing auto-renewable subscriptions is a mutation
    // against the LIVE store, and the exact price-point ids + current group/subscription ids differ per
    // account — so this step is written from the `report` output (run it first), which is why report is
    // the safe default. Once report confirms the current state, the create/price/localize/availability
    // calls are filled in here and driven by the workflow's price inputs.
    fail("`apply` is not enabled yet. Run `report` first and hand back its output; the create/price step is built from it.");
}

// -------------------------------------------------------------- utilities --

function index(included, type) {
    const m = new Map();
    for (const x of included || []) if (x.type === type) m.set(x.id, x);
    return m;
}
function rel(obj, name) {
    return obj.relationships?.[name]?.data?.id ?? null;
}
function short(e) {
    return String(e.message || e).split("\n")[0];
}
function log(s) { process.stdout.write(s + "\n"); }
function line() { log("─".repeat(78)); }
function fail(msg) {
    process.stderr.write(`\n✗ ${msg}\n`);
    process.exit(1);
}

// ------------------------------------------------------------------- main --

const argv = process.argv.slice(2);
const mode = argv.find(a => !a.startsWith("--")) || "report";
const bundleFlag = argv.indexOf("--bundle-id");
const BUNDLE_ID = bundleFlag !== -1 ? argv[bundleFlag + 1] : DEFAULT_BUNDLE_ID;

TOKEN = makeToken();
try {
    if (mode === "report") await report();
    else if (mode === "capabilities") await capabilities({ enable: argv.includes("--enable") });
    else if (mode === "apply") await apply();
    else fail(`Unknown mode "${mode}". Use "report", "capabilities [--enable]" or "apply".`);
} catch (e) {
    fail(String(e.message || e));
}
