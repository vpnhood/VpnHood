/**
 * Push subscription texts (group name, per-product display name and description) to App Store
 * Connect, for every store locale.
 *
 * WHY THIS EXISTS AS ITS OWN TOOL. The listing compiler (store-metadata.mjs) writes fastlane trees
 * and `deliver` ships them, but fastlane has no concept of in-app-purchase localizations — they live
 * only in App Store Connect, reachable through the API. Without this, the paywall is translated by
 * the app while the NATIVE StoreKit sheet — the one screen where money changes hands — shows the
 * English display name to every locale.
 *
 * WHY NOW RATHER THAN LATER. Subscription metadata goes through App Review. Localizations added
 * alongside a version submission cost nothing extra; added after approval they are a separate IAP
 * review round. So this is cheapest run before the first submission, and on each product change.
 *
 * SOURCE OF TRUTH. `<root>/store-i18n/en-US/subscriptions.json`, hand-written, translated into the
 * sibling locales by vhtranslator like every other store text (invariant 1: only en-US is written by
 * hand). Keys are:
 *
 *     group.name                     the subscription GROUP's display name
 *     <productId>.name               that product's display name
 *     <productId>.description        that product's description
 *
 * Product ids are the keys themselves, so a fork edits one file and needs no code change — the same
 * decision asc-iap.mjs makes with --products.
 *
 * LOCALES come from store-i18n/locales.json, the same file the text compiler and the screenshot
 * engine read, mapped through `stores.appStore` — so subscription texts can never cover a different
 * locale set than the listing. `"appStore": null` (Persian) is skipped: Apple has no such storefront.
 *
 * LIMITS are Apple's and are enforced here rather than discovered on submission: display name 30
 * characters, description 45. Over-length fails the run (invariant 4: fail loud, never truncate —
 * a silently cut name ships to a store).
 *
 * Credentials: env (APPSTORE_CONNECT_API_KEY / _API_KEY_ID / _ISSUER_ID) or --keys-dir <dir>
 * holding appstore_connect_api_key*.p8 + appstore_connect_api_key_id.txt + appstore_connect_issuer_id.txt.
 *
 * Usage:
 *   node store-subscriptions.mjs --bundle-id com.vpnhood.connect.ios --root <store repo> --keys-dir <dir>
 *   node store-subscriptions.mjs --bundle-id … --root … --keys-dir … --check   # report drift, write nothing
 */
import fs from 'node:fs/promises';
import path from 'node:path';
import { createSign } from 'node:crypto';

const args = process.argv.slice(2);
const argValue = (name, fallback) => {
  const i = args.indexOf(name);
  return i >= 0 && args[i + 1] ? args[i + 1] : fallback;
};
const root = path.resolve(argValue('--root', '.'));
const bundleId = argValue('--bundle-id', null) ?? (() => { throw new Error('--bundle-id is required (e.g. com.vpnhood.connect.ios).'); })();
const checkOnly = args.includes('--check');
const keysDir = argValue('--keys-dir', null);

/** Apple's field limits. Exceeding either is a submission-time rejection, so it is a run-time error here. */
const LIMITS = { name: 30, description: 45 };

// ---------- App Store Connect client (no dependencies; Node's crypto signs the JWT) ----------

async function credentials() {
  if (keysDir) {
    const dir = path.resolve(keysDir);
    const p8 = (await fs.readdir(dir)).find((f) => /^appstore_connect_api_key.*\.p8$/.test(f));
    if (!p8) throw new Error(`no appstore_connect_api_key*.p8 in ${dir}`);
    return {
      key: await fs.readFile(path.join(dir, p8), 'utf8'),
      kid: (await fs.readFile(path.join(dir, 'appstore_connect_api_key_id.txt'), 'utf8')).trim(),
      iss: (await fs.readFile(path.join(dir, 'appstore_connect_issuer_id.txt'), 'utf8')).trim(),
    };
  }
  const { APPSTORE_CONNECT_API_KEY: key, APPSTORE_CONNECT_API_KEY_ID: kid, APPSTORE_CONNECT_ISSUER_ID: iss } = process.env;
  if (!key || !kid || !iss)
    throw new Error('APPSTORE_CONNECT_API_KEY / _API_KEY_ID / _ISSUER_ID must be set (or pass --keys-dir).');
  return { key, kid, iss };
}

const creds = await credentials();
let cachedToken = null, cachedExp = 0;
/** One token per run, re-minted before Apple's 20-minute cap. Minting per request intermittently 401s. */
function token() {
  const now = Math.floor(Date.now() / 1000);
  if (cachedToken && cachedExp - now > 60) return cachedToken;
  const b64 = (o) => Buffer.from(JSON.stringify(o)).toString('base64url');
  const unsigned = `${b64({ alg: 'ES256', kid: creds.kid, typ: 'JWT' })}.${b64({ iss: creds.iss, iat: now, exp: now + 900, aud: 'appstoreconnect-v1' })}`;
  const sig = createSign('SHA256').update(unsigned).end().sign({ key: creds.key, dsaEncoding: 'ieee-p1363' }).toString('base64url');
  cachedExp = now + 900;
  return (cachedToken = `${unsigned}.${sig}`);
}

/** 5xx and the intermittent 401 are retried with backoff; other 4xx are real and fail immediately. */
async function api(pathname, method = 'GET', body) {
  const delays = [0, 3000, 10000, 30000];
  let last;
  for (const delay of delays) {
    if (delay) await new Promise((r) => setTimeout(r, delay));
    const res = await fetch(`https://api.appstoreconnect.apple.com${pathname}`, {
      method,
      headers: { Authorization: `Bearer ${token()}`, ...(body ? { 'Content-Type': 'application/json' } : {}) },
      body: body ? JSON.stringify(body) : undefined,
    });
    if (res.ok) return res.status === 204 ? null : res.json();
    last = `${res.status} ${method} ${pathname} :: ${(await res.text()).slice(0, 300)}`;
    if (res.status < 500 && res.status !== 401) break;
    console.log(`  retryable ${res.status} on ${method} ${pathname}${delay < 30000 ? ' — backing off' : ''}`);
  }
  throw new Error(last);
}

// ---------- texts ----------

/** The store locales, in the listing's own order, mapped to App Store codes. */
async function storeLocales() {
  const file = path.join(root, 'store-i18n', 'locales.json');
  const { locales } = JSON.parse(await fs.readFile(file, 'utf8'));
  return locales
    .map((l) => ({ tag: l.tag, appStore: l.stores?.appStore === undefined ? l.tag : l.stores.appStore }))
    .filter((l) => l.appStore !== null); // Persian: no such storefront
}

/** Reads one locale's texts and enforces Apple's limits before anything is sent. */
async function textsFor(tag) {
  const file = path.join(root, 'store-i18n', tag, 'subscriptions.json');
  const texts = JSON.parse(await fs.readFile(file, 'utf8'));
  for (const [key, value] of Object.entries(texts)) {
    const kind = key.endsWith('.description') ? 'description' : 'name';
    if (value.length > LIMITS[kind])
      throw new Error(`${tag}/subscriptions.json: "${key}" is ${value.length} characters, Apple's limit for a ${kind} is ${LIMITS[kind]}: ${value}`);
  }
  return texts;
}

// ---------- sync ----------

const app = (await api(`/v1/apps?filter[bundleId]=${encodeURIComponent(bundleId)}`)).data[0];
if (!app) throw new Error(`no app in App Store Connect for bundle id ${bundleId}`);
console.log(`${app.attributes.name} — subscription texts (${checkOnly ? 'check' : 'sync'})`);

const locales = await storeLocales();
const groups = (await api(`/v1/apps/${app.id}/subscriptionGroups?limit=50`)).data;
if (!groups.length) throw new Error('the app has no subscription group; create it before pushing texts.');

let changed = 0;

/** Create or update one localization, in place, without disturbing the ones already correct. */
async function put(kind, existing, parentType, parentId, locale, attributes) {
  const current = existing.find((e) => e.attributes.locale === locale);
  const same = current && Object.entries(attributes).every(([k, v]) => current.attributes[k] === v);
  if (same) return;
  changed++;
  const label = `${kind} ${locale}: ${Object.values(attributes).join(' / ')}`;
  if (checkOnly) return console.log(`  would ${current ? 'update' : 'create'} ${label}`);
  if (current) await api(`/v1/${kind}/${current.id}`, 'PATCH', { data: { type: kind, id: current.id, attributes } });
  else await api(`/v1/${kind}`, 'POST', { data: { type: kind, attributes: { locale, ...attributes }, relationships: { [parentType]: { data: { type: `${parentType}s`, id: parentId } } } } });
  console.log(`  ${current ? 'updated' : 'created'} ${label}`);
}

for (const group of groups) {
  console.log(`\ngroup "${group.attributes.referenceName}"`);
  const groupLocs = (await api(`/v1/subscriptionGroups/${group.id}/subscriptionGroupLocalizations?limit=50`)).data;
  const subs = (await api(`/v1/subscriptionGroups/${group.id}/subscriptions?limit=100`)).data;

  for (const { tag, appStore } of locales) {
    const texts = await textsFor(tag);

    const groupName = texts['group.name'];
    if (!groupName) throw new Error(`${tag}/subscriptions.json has no "group.name".`);
    await put('subscriptionGroupLocalizations', groupLocs, 'subscriptionGroup', group.id, appStore, { name: groupName });

    for (const sub of subs) {
      const productId = sub.attributes.productId;
      const name = texts[`${productId}.name`];
      const description = texts[`${productId}.description`];
      // A product present in the store but absent from the texts is a mistake worth stopping for:
      // silently skipping it ships an English name to every locale, which is what this tool exists
      // to prevent.
      if (!name || !description)
        throw new Error(`${tag}/subscriptions.json is missing "${productId}.name" or "${productId}.description" (the product exists in App Store Connect).`);
      const subLocs = (await api(`/v1/subscriptions/${sub.id}/subscriptionLocalizations?limit=50`)).data;
      await put('subscriptionLocalizations', subLocs, 'subscription', sub.id, appStore, { name, description });
    }
  }

  // States are reported because editing a READY_TO_SUBMIT product can move it back to a state that
  // needs attention before the version can be submitted with it.
  for (const sub of subs) {
    const fresh = await api(`/v1/subscriptions/${sub.id}`);
    console.log(`  ${sub.attributes.productId}: state=${fresh.data.attributes.state}`);
  }
}

console.log(changed
  ? `\n${changed} localization(s) ${checkOnly ? 'would change' : 'written'}.`
  : '\nsubscription texts already match store-i18n.');
