#!/usr/bin/env node
/**
 * Sync App Store screenshots by checksum — the App Store twin of Play's image sync.
 *
 * deliver's screenshot push is all-or-nothing: it deletes every screenshot on the version and
 * re-creates all of them, every run, even when nothing changed. Apple deletes are eventually
 * consistent, so re-creating a file whose ghost still lingers makes the API 500 that write, and
 * deliver retries it forever — four runs on this app wedged that way, each on one random file near
 * the end ("Waiting for screenshots to appear before uploading … Server error got 500"). A cancelled
 * run then leaves the surviving screenshots in upload-completion order, i.e. scrambled.
 *
 * This tool never does the mass delete. It compares what is live (fileName + sourceFileChecksum —
 * the MD5 Apple stores from the original upload) against the store repo's files and:
 *   - deletes only records whose file no longer exists locally, whose checksum differs, or which
 *     are stuck in a non-COMPLETE delivery state (half-finished uploads left by a dead run);
 *   - uploads only what is missing (reserve → PUT chunks → commit, retrying 5xx with backoff, so a
 *     lingering ghost clears instead of wedging);
 *   - waits — bounded, not forever — for the new uploads to finish processing;
 *   - restores the display order everywhere (the order IS the listing's story);
 *   - prints exactly which file failed if one does. An unchanged listing is a no-op in seconds.
 *
 * Auth: the App Store Connect API key, from the same env vars the CI workflow already has
 * (APPSTORE_CONNECT_API_KEY / _API_KEY_ID / _ISSUER_ID), or --keys-dir <dir> holding
 * appstore_connect_api_key_*.p8, appstore_connect_api_key_id.txt, appstore_connect_issuer_id.txt.
 *
 * Usage:
 *   node store-asc-screenshots.mjs --bundle-id com.vpnhood.client.ios --root <store repo>
 *   node store-asc-screenshots.mjs --bundle-id … --root … --check          report drift, write nothing
 *   node store-asc-screenshots.mjs --bundle-id … --root … --keys-dir <dir>
 *   node store-asc-screenshots.mjs --bundle-id … --root … --platform TV_OS the Apple TV's version and set
 */
import { promises as fs } from 'fs';
import { createSign, createHash } from 'crypto';
import path from 'path';

const args = process.argv.slice(2);
const argValue = (name, fallback) => {
  const i = args.indexOf(name);
  return i >= 0 && args[i + 1] ? args[i + 1] : fallback;
};
const rootArg = argValue('--root', null);
if (!rootArg) throw new Error('--root <store repo> is required.');
const root = path.resolve(rootArg);
const bundleId = argValue('--bundle-id', null) ?? (() => { throw new Error('--bundle-id is required (e.g. com.vpnhood.client.ios).'); })();
const checkOnly = args.includes('--check');
const keysDir = argValue('--keys-dir', null);

/**
 * One app record holds a version per platform, each with its own listing, so every call names its
 * platform: an unfiltered list hands back the Apple TV's draft while the iPhone's version is in review.
 */
const PLATFORMS = {
  IOS: {
    folder: 'ios',
    // Which ASC screenshot set a local file belongs to, by the repo's naming convention.
    families: [
      { type: 'APP_IPHONE_67', match: /^(\d+)\.png$/ },
      { type: 'APP_IPAD_PRO_3GEN_129', match: /^ipad_(\d+)\.png$/ },
    ],
  },
  TV_OS: {
    folder: 'appletv',
    families: [{ type: 'APP_APPLE_TV', match: /^(\d+)\.png$/ }],
  },
};
const platform = argValue('--platform', 'IOS');
if (!PLATFORMS[platform]) throw new Error(`--platform must be one of ${Object.keys(PLATFORMS).join(', ')}.`);
const shotsRoot = path.join(root, 'fastlane', 'screenshots', PLATFORMS[platform].folder);
const FAMILIES = PLATFORMS[platform].families;
const familyOf = (name) => FAMILIES.find((f) => f.match.test(name));
const shotIndex = (family, name) => Number(name.match(family.match)[1]);

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
/** Apple caps token lifetime at 20 minutes; long syncs re-mint before expiry. */
function token() {
  const now = Math.floor(Date.now() / 1000);
  if (cachedToken && cachedExp - now > 60) return cachedToken;
  const b64 = (o) => Buffer.from(JSON.stringify(o)).toString('base64url');
  const unsigned = `${b64({ alg: 'ES256', kid: creds.kid, typ: 'JWT' })}.${b64({ iss: creds.iss, iat: now, exp: now + 900, aud: 'appstoreconnect-v1' })}`;
  const sig = createSign('SHA256').update(unsigned).end().sign({ key: creds.key, dsaEncoding: 'ieee-p1363' }).toString('base64url');
  cachedExp = now + 900;
  return (cachedToken = `${unsigned}.${sig}`);
}

/** 5xx are retried with backoff: Apple's ghost-delete collisions clear after a wait, and transient
 * 500 waves pass. 4xx are real errors and fail immediately. */
async function api(pathname, method = 'GET', body) {
  const delays = [0, 5000, 15000, 30000, 60000];
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
    if (res.status < 500) break;
    console.log(`  retryable ${res.status} on ${method} ${pathname}${delay < 60000 ? ' — backing off' : ''}`);
  }
  throw new Error(last);
}

// ---------- sync ----------

const md5 = async (file) => createHash('md5').update(await fs.readFile(file)).digest('hex');

async function uploadScreenshot(setId, file) {
  const bytes = await fs.readFile(file.absolute);
  const created = await api('/v1/appScreenshots', 'POST', {
    data: {
      type: 'appScreenshots',
      attributes: { fileName: file.name, fileSize: bytes.length },
      relationships: { appScreenshotSet: { data: { type: 'appScreenshotSets', id: setId } } },
    },
  });
  for (const op of created.data.attributes.uploadOperations ?? []) {
    const headers = Object.fromEntries((op.requestHeaders ?? []).map((h) => [h.name, h.value]));
    const res = await fetch(op.url, { method: op.method, headers, body: bytes.subarray(op.offset, op.offset + op.length) });
    if (!res.ok) throw new Error(`chunk upload for ${file.name}: ${res.status} ${await res.text()}`);
  }
  await api(`/v1/appScreenshots/${created.data.id}`, 'PATCH', {
    data: { type: 'appScreenshots', id: created.data.id, attributes: { uploaded: true, sourceFileChecksum: await md5(file.absolute) } },
  });
  return created.data.id;
}

/** Bounded wait — a stuck asset must FAIL the run with its name, never spin forever. */
async function waitComplete(setId, ids, minutes = 5) {
  const deadline = Date.now() + minutes * 60000;
  let pending = new Set(ids);
  while (pending.size && Date.now() < deadline) {
    await new Promise((r) => setTimeout(r, 10000));
    const shots = (await api(`/v1/appScreenshotSets/${setId}/appScreenshots?limit=20`)).data;
    for (const s of shots) {
      const st = s.attributes.assetDeliveryState?.state;
      if (!pending.has(s.id)) continue;
      if (st === 'COMPLETE') pending.delete(s.id);
      else if (st === 'FAILED') throw new Error(`${s.attributes.fileName} FAILED processing on Apple's side`);
    }
  }
  if (pending.size) throw new Error(`${pending.size} screenshot(s) still processing after ${minutes} minutes`);
}

const app = (await api(`/v1/apps?filter[bundleId]=${bundleId}`)).data[0];
if (!app) throw new Error(`no app with bundle id ${bundleId}`);
const versions = (await api(`/v1/apps/${app.id}/appStoreVersions?filter[platform]=${platform}&limit=5`)).data;
// States in which Apple still lets the listing be written. PREPARE_FOR_SUBMISSION is the ordinary
// one; the rejected states matter just as much, because a rejection is precisely when corrected
// metadata has to go up — treating them as locked makes the tool refuse at the only moment it is
// urgently needed (observed 2026-08-31: a REJECTED version silently skipped the whole App Store leg
// while the run stayed green). Anything else — in review, or live with no open version — is locked.
const EDITABLE_STATES = new Set([
  'PREPARE_FOR_SUBMISSION',
  'REJECTED',            // App Review rejected it; the version reopens for editing
  'DEVELOPER_REJECTED',  // we pulled it back ourselves
  'METADATA_REJECTED',   // rejected on listing content alone
  'INVALID_BINARY',      // build failed Apple's processing; metadata still writable
]);
const version = versions.find((v) => EDITABLE_STATES.has(v.attributes.appStoreState));

// --editable-check: answer "can the listing be written at all?" and nothing else. Apple locks the
// listing while a version is in review or live with none open; CI uses this to warn-and-skip the
// whole App Store leg (exit 3) instead of failing red on a state that is normal between releases.
if (args.includes('--editable-check')) {
  if (version) { console.log(`editable: ${version.attributes.versionString} (${version.attributes.appStoreState})`); process.exit(0); }
  console.log(`locked: no editable version — states: ${versions.map((v) => `${v.attributes.versionString}=${v.attributes.appStoreState}`).join(', ')}`);
  process.exit(3);
}

// --whatsnew-check: answer "does the editable version still need its release notes?" and nothing
// else. Apple never carries "What's New" over to a newly created version — the field starts empty
// and is required per localization once the app has a prior release — while the publish gate
// compares only repo content, so an UNCHANGED listing plus a fresh version would be skipped and
// the empty field would later block the submission. The gate runs this before honoring a skip:
// exit 4 = at least one localization with a local release_notes.txt has an empty whatsNew (push
// the unchanged text to fill it), exit 0 = nothing to fill (all set, or this app ships no release
// notes — a FIRST version must not carry any), exit 3 = listing locked (nothing writable anyway).
if (args.includes('--whatsnew-check')) {
  if (!version) {
    console.log(`locked: no editable version — states: ${versions.map((v) => `${v.attributes.versionString}=${v.attributes.appStoreState}`).join(', ')}`);
    process.exit(3);
  }
  const metaRoot = path.join(root, 'fastlane', 'metadata', 'ios');
  const localized = (await api(`/v1/appStoreVersions/${version.id}/appStoreVersionLocalizations?limit=50`)).data;
  const empty = [];
  for (const loc of localized) {
    const file = path.join(metaRoot, loc.attributes.locale, 'release_notes.txt');
    if (!(await fs.stat(file).catch(() => null))) continue; // no local notes for this locale — nothing to fill
    if (!loc.attributes.whatsNew?.trim()) empty.push(loc.attributes.locale);
  }
  if (empty.length) {
    console.log(`whatsNew empty on ${version.attributes.versionString} for: ${empty.join(', ')}`);
    process.exit(4);
  }
  console.log(`whatsNew present on ${version.attributes.versionString} for every locale with local release notes`);
  process.exit(0);
}
if (!version)
  throw new Error(`no editable version — states: ${versions.map((v) => `${v.attributes.versionString}=${v.attributes.appStoreState}`).join(', ')}`);
console.log(`${app.attributes.name} ${version.attributes.versionString} — sync from ${path.relative(process.cwd(), shotsRoot) || '.'}\n`);

const allLocalizations = (await api(`/v1/appStoreVersions/${version.id}/appStoreVersionLocalizations?limit=50`)).data;
const localLocales = (await fs.readdir(shotsRoot, { withFileTypes: true })).filter((e) => e.isDirectory()).map((e) => e.name);
const liveLocales = new Set(allLocalizations.map((l) => l.attributes.locale));
// Screenshots on disk for a locale the listing doesn't have is a config error — that work would
// never ship — so it fails. The REVERSE is a legitimate staged rollout (the text push creates a
// localization before its screenshots exist, e.g. CONNECT's translations landing ahead of its
// screenshot generation), so those locales are skipped with a loud warning, not an error: on the
// storefront they fall back to the primary locale's screenshots until theirs arrive.
const missingLive = localLocales.filter((l) => !liveLocales.has(l));
if (missingLive.length)
  throw new Error(`screenshots exist on disk for locales the listing does not have: [${missingLive.join(', ')}] (deliver's text push creates localizations — run it first)`);
const missingLocal = [...liveLocales].filter((l) => !localLocales.includes(l));
if (missingLocal.length)
  console.log(`  WARNING: no local screenshots for listing locale(s) [${missingLocal.join(', ')}] — skipped; they show the primary locale's screenshots until generated\n`);
const localizations = allLocalizations.filter((l) => localLocales.includes(l.attributes.locale));

// ---------- plan, then execute in phases ----------
//
// Replacing a screenshot is delete + create of the SAME fileName, and Apple 500s a create whose
// deleted predecessor hasn't been garbage-collected yet ("ghost"). Empirically, hammering that
// create keeps failing for 15+ minutes, while the same create done once, later, succeeds first
// try. So the run is phased to put as much time as possible between a delete and the create that
// reuses its name: ALL deletions happen first, then a grace pause (only when a name is reused),
// then the uploads — and an upload that still hits persistent 5xx is queued for one final round
// after another pause instead of being hammered or aborting the run. Reordering runs last for
// every set regardless, so even a failed run leaves the listing orderly.

const plans = [];
for (const loc of localizations.sort((a, b) => a.attributes.locale.localeCompare(b.attributes.locale))) {
  const locale = loc.attributes.locale;
  const dir = path.join(shotsRoot, locale);
  const files = (await fs.readdir(dir)).filter((n) => familyOf(n));
  const sets = (await api(`/v1/appStoreVersionLocalizations/${loc.id}/appScreenshotSets?limit=20`)).data;

  for (const family of FAMILIES) {
    const want = files.filter((n) => familyOf(n) === family).sort((a, b) => shotIndex(family, a) - shotIndex(family, b));
    if (!want.length) continue;
    const set = sets.find((s) => s.attributes.screenshotDisplayType === family.type) ?? null;
    const shots = set ? (await api(`/v1/appScreenshotSets/${set.id}/appScreenshots?limit=20`)).data : [];
    const wantMd5 = new Map();
    for (const name of want) wantMd5.set(name, await md5(path.join(dir, name)));

    const stale = [];
    for (const s of shots) {
      const name = s.attributes.fileName;
      const state = s.attributes.assetDeliveryState?.state;
      if (!wantMd5.has(name)) stale.push([s, 'not in repo']);
      else if (state && state !== 'COMPLETE' && state !== 'UPLOAD_COMPLETE') stale.push([s, `state ${state}`]);
      else if (s.attributes.sourceFileChecksum && s.attributes.sourceFileChecksum !== wantMd5.get(name)) stale.push([s, 'checksum differs']);
    }
    const staleNames = new Set(stale.map(([s]) => s.attributes.fileName));
    const present = new Set(shots.filter((s) => !staleNames.has(s.attributes.fileName)).map((s) => s.attributes.fileName));
    const toUpload = want.filter((n) => !present.has(n));
    plans.push({ locale, family, locId: loc.id, dir, want, set, stale, toUpload });
  }
}

const dirty = plans.filter((p) => !p.set || p.stale.length || p.toUpload.length);
if (checkOnly) {
  let drift = 0;
  for (const p of plans) {
    if (!p.set) { console.log(`  ${p.locale.padEnd(8)} ${p.family.type}: set MISSING (${p.want.length} to upload)`); drift++; continue; }
    if (p.stale.length || p.toUpload.length) {
      console.log(`  ${p.locale.padEnd(8)} ${p.family.type}: delete ${p.stale.length} (${p.stale.map(([s, why]) => `${s.attributes.fileName}: ${why}`).join('; ') || '-'}), upload ${p.toUpload.length} (${p.toUpload.join(' ')})`);
      drift++;
      continue;
    }
    const names = (await api(`/v1/appScreenshotSets/${p.set.id}/appScreenshots?limit=20`)).data.map((s) => s.attributes.fileName);
    if (JSON.stringify(names) !== JSON.stringify(p.want)) {
      console.log(`  ${p.locale.padEnd(8)} ${p.family.type}: order ${names.join(',')} -> ${p.want.join(',')}`);
      drift++;
    }
  }
  console.log(drift ? `\n${drift} set(s) out of sync` : 'listing screenshots are in sync with the repo');
  process.exit(drift ? 1 : 0);
}

let uploads = 0, deletions = 0, reorders = 0;

// Phase 1 — create any missing sets (no ghost hazard; needed before uploads).
for (const p of dirty.filter((p) => !p.set)) {
  p.set = (await api('/v1/appScreenshotSets', 'POST', {
    data: {
      type: 'appScreenshotSets',
      attributes: { screenshotDisplayType: p.family.type },
      relationships: { appStoreVersionLocalization: { data: { type: 'appStoreVersionLocalizations', id: p.locId } } },
    },
  })).data;
  console.log(`  ${p.locale.padEnd(8)} ${p.family.type}: set created`);
}

// Phase 2 — ALL deletions, across every set, before any upload.
let reusedNames = 0;
for (const p of dirty) {
  for (const [s, why] of p.stale) {
    await api(`/v1/appScreenshots/${s.id}`, 'DELETE');
    deletions++;
    if (p.toUpload.includes(s.attributes.fileName)) reusedNames++;
    console.log(`  ${p.locale.padEnd(8)} ${p.family.type}: deleted ${s.attributes.fileName} (${why})`);
  }
}

// Phase 3 — grace pause, only when a deleted fileName is about to be re-created: give Apple's
// eventually-consistent delete time to settle so the create doesn't meet the ghost at all.
const grace = Number(argValue('--grace', '90'));
if (reusedNames > 0 && grace > 0) {
  console.log(`\n  ${reusedNames} fileName(s) are re-used after deletion — waiting ${grace}s for the deletions to settle\n`);
  await new Promise((r) => setTimeout(r, grace * 1000));
}

// Phase 4 — uploads. Persistent 5xx does NOT abort the run: the file goes to a final retry round
// (phase 5) so one sick record can't take down the other 100+ files' work.
const uploaded = []; // { plan, id }
const deferred = []; // { plan, name, error }
for (const p of dirty) {
  for (const name of p.toUpload) {
    try {
      uploaded.push({ plan: p, id: await uploadScreenshot(p.set.id, { name, absolute: path.join(p.dir, name) }) });
      uploads++;
      console.log(`  ${p.locale.padEnd(8)} ${p.family.type}: uploaded ${name}`);
    } catch (error) {
      deferred.push({ plan: p, name, error });
      console.log(`  ${p.locale.padEnd(8)} ${p.family.type}: ${name} deferred (${String(error.message).split('\n')[0]})`);
    }
  }
}

// Phase 5 — one final round for the deferred files after another pause. Before re-creating, check
// whether the failed POST actually persisted a record (a 500 does not always mean nothing landed):
// a good record is kept, a broken one is deleted first.
const failures = [];
if (deferred.length) {
  console.log(`\n  retrying ${deferred.length} deferred file(s) after ${grace}s\n`);
  await new Promise((r) => setTimeout(r, grace * 1000));
  for (const d of deferred) {
    try {
      const shots = (await api(`/v1/appScreenshotSets/${d.plan.set.id}/appScreenshots?limit=20`)).data;
      const existing = shots.find((s) => s.attributes.fileName === d.name);
      if (existing) {
        const ok = existing.attributes.assetDeliveryState?.state === 'COMPLETE'
          && existing.attributes.sourceFileChecksum === await md5(path.join(d.plan.dir, d.name));
        if (ok) { console.log(`  ${d.plan.locale.padEnd(8)} ${d.plan.family.type}: ${d.name} landed after all`); continue; }
        await api(`/v1/appScreenshots/${existing.id}`, 'DELETE');
      }
      uploaded.push({ plan: d.plan, id: await uploadScreenshot(d.plan.set.id, { name: d.name, absolute: path.join(d.plan.dir, d.name) }) });
      uploads++;
      console.log(`  ${d.plan.locale.padEnd(8)} ${d.plan.family.type}: uploaded ${d.name} (retry)`);
    } catch (error) {
      failures.push(`${d.plan.locale}/${d.name}: ${String(error.message).split('\n')[0]}`);
    }
  }
}

// Phase 6 — wait for the new uploads to finish processing, per set, bounded.
const bySet = new Map();
for (const u of uploaded) {
  if (!bySet.has(u.plan.set.id)) bySet.set(u.plan.set.id, []);
  bySet.get(u.plan.set.id).push(u.id);
}
for (const [setId, ids] of bySet) {
  try {
    await waitComplete(setId, ids);
  } catch (error) {
    failures.push(String(error.message).split('\n')[0]);
  }
}

// Phase 7 — restore display order EVERYWHERE, including sets this run never touched and even when
// some upload failed: a red run must still leave the listing orderly.
for (const p of plans) {
  if (!p.set) continue;
  const shots = (await api(`/v1/appScreenshotSets/${p.set.id}/appScreenshots?limit=20`)).data;
  const names = shots.map((s) => s.attributes.fileName);
  const expected = p.want.filter((n) => names.includes(n));
  if (JSON.stringify(names) === JSON.stringify(expected)) continue;
  const byName = new Map(shots.map((s) => [s.attributes.fileName, s.id]));
  await api(`/v1/appScreenshotSets/${p.set.id}/relationships/appScreenshots`, 'PATCH',
    { data: expected.map((n) => ({ type: 'appScreenshots', id: byName.get(n) })) });
  reorders++;
  console.log(`  ${p.locale.padEnd(8)} ${p.family.type}: reordered`);
}

console.log(`\nsync ${failures.length ? 'INCOMPLETE' : 'done'} — uploaded ${uploads}, deleted ${deletions}, reordered ${reorders} set(s)${uploads + deletions + reorders === 0 ? ' (already in sync)' : ''}`);
if (failures.length) {
  console.error(`\nfailed after both rounds — re-run to sync just these:\n  ${failures.join('\n  ')}`);
  process.exit(1);
}
