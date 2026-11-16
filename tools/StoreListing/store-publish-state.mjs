#!/usr/bin/env node
/**
 * Decide whether a store LISTING actually changed since it was last published.
 *
 * fastlane never asks: `supply` and `deliver` upload whatever sits in the fastlane trees on every
 * run, with no comparison against the live listing (the one exception is Play's image sync, which
 * hashes images and uploads only the differences). On iOS our lane overwrites screenshots, so an
 * unconditional publish deletes and re-uploads every screenshot in every locale even when nothing
 * moved — slow, and one more thing that can fail halfway through a release.
 *
 * So the gate lives here instead: fingerprint exactly what a publish would upload, compare it with
 * the fingerprint recorded by the last successful publish, and let the workflow skip the push when
 * they match. Listing content is generated and committed (store-metadata.mjs for the texts,
 * the StoreScreenshots tool for the images), so the fingerprint is a property of the commit — no need
 * to interrogate the stores.
 *
 * The record lives in the store-asset repo at fastlane/publish-state.json, written only AFTER a
 * publish succeeds. Keeping it in git (rather than CI cache) makes it durable and reviewable: the
 * diff shows when each store last went out. An unknown store — no state file, or a first-ever
 * publish — always counts as changed, so the gate can never silently skip the first upload.
 *
 * Scope follows what the listing lanes actually send. Android changelogs are excluded because the
 * listing lane skips them (they belong to the release, keyed by version code); iOS release notes
 * are included because deliver does push them.
 *
 * Usage:
 *   node store-publish-state.mjs --root <store repo>               report (+ GITHUB_OUTPUT)
 *   node store-publish-state.mjs --root <store repo> --update ios  record ios as published
 */
import { promises as fs } from 'fs';
import { createHash } from 'crypto';
import path from 'path';

const args = process.argv.slice(2);
const argValue = (name, fallback) => {
  const i = args.indexOf(name);
  return i >= 0 && args[i + 1] ? args[i + 1] : fallback;
};
const rootArg = argValue('--root', null);
if (!rootArg) throw new Error('--root <store repo> is required.');
const root = path.resolve(rootArg);
const stateFile = path.join(root, 'fastlane', 'publish-state.json');
const updateStore = argValue('--update', null);

/**
 * The directories each store's listing publish reads, and what to leave out of the fingerprint.
 * `skip` names directories that exist in the tree but are never part of a listing upload.
 */
const STORES = {
  android: {
    dirs: [path.join(root, 'fastlane', 'metadata', 'android')],
    // Per-version release notes, uploaded by the release lane keyed on version code — not listing.
    skip: new Set(['changelogs']),
  },
  ios: {
    dirs: [
      path.join(root, 'fastlane', 'metadata', 'ios'),
      // deliver keeps screenshots OUTSIDE metadata_path (every subdir there is treated as a locale).
      path.join(root, 'fastlane', 'screenshots', 'ios'),
    ],
    skip: new Set(),
  },
};

/** Every file under `dir`, as paths relative to it, with `skip` directories pruned. */
async function walk(dir, skip, prefix = '') {
  const entries = await fs.readdir(dir, { withFileTypes: true }).catch(() => null);
  if (entries === null) return []; // an app without this store simply has no tree
  const files = [];
  for (const entry of entries.sort((a, b) => a.name.localeCompare(b.name))) {
    if (entry.isDirectory() && skip.has(entry.name)) continue;
    const relative = prefix ? `${prefix}/${entry.name}` : entry.name;
    if (entry.isDirectory()) files.push(...await walk(path.join(dir, entry.name), skip, relative));
    else files.push({ relative, absolute: path.join(dir, entry.name) });
  }
  return files;
}

/**
 * A stable digest of a store's listing: every file's path and content. Paths are included so that
 * renaming or dropping a locale registers as a change even when the bytes elsewhere are untouched.
 * Returns null when the store has no tree at all, which means "nothing to publish".
 */
async function fingerprint(store) {
  // Sequential on purpose: the directories feed ONE hash, so their bytes must arrive in a fixed
  // order. Walking them concurrently interleaves the updates and yields a different digest every
  // run, which would report "changed" forever.
  const hash = createHash('sha256');
  let total = 0;
  for (const dir of store.dirs) {
    const files = await walk(dir, store.skip);
    for (const file of files) {
      hash.update(path.basename(dir) + '/' + file.relative + '\0');
      hash.update(normalize(file.relative, await fs.readFile(file.absolute)));
      hash.update('\0');
    }
    total += files.length;
  }
  return total === 0 ? null : { digest: hash.digest('hex'), files: total };
}

/**
 * Text files are hashed with LF line endings so the fingerprint means the same thing everywhere.
 * With git's autocrlf a Windows checkout holds CRLF while Linux CI holds LF, so hashing raw bytes
 * made the same commit fingerprint differently per platform — CI would record one value and a
 * developer's machine would compute another and report a phantom change. Screenshots are compared
 * byte for byte; only the listing's text files are normalized.
 */
function normalize(relative, bytes) {
  return /\.(txt|json)$/i.test(relative)
    ? Buffer.from(bytes.toString('utf8').replaceAll('\r\n', '\n'), 'utf8')
    : bytes;
}

async function readState() {
  const raw = await fs.readFile(stateFile, 'utf8').catch(() => null);
  return raw === null ? {} : JSON.parse(raw);
}

async function main() {
  const state = await readState();

  if (updateStore !== null) {
    const store = STORES[updateStore];
    if (!store) throw new Error(`--update ${updateStore}: unknown store (expected ${Object.keys(STORES).join(' | ')}).`);
    const current = await fingerprint(store);
    if (current === null)
      throw new Error(`--update ${updateStore}: no listing files under ${store.dirs.join(' or ')} — refusing to record a publish that had nothing to send.`);
    state[updateStore] = { fingerprint: current.digest, publishedAt: new Date().toISOString() };
    await fs.mkdir(path.dirname(stateFile), { recursive: true });
    await fs.writeFile(stateFile, JSON.stringify(state, null, 2) + '\n');
    console.log(`recorded  ${updateStore}  ${current.digest.slice(0, 12)}  (${current.files} files)`);
    return;
  }

  const outputs = [];
  let anyChanged = false;
  for (const [name, store] of Object.entries(STORES)) {
    const current = await fingerprint(store);
    const previous = state[name]?.fingerprint ?? null;
    // No tree: nothing to publish. Never published: always publish (the gate must not eat a first upload).
    const changed = current !== null && current.digest !== previous;
    const reason = current === null ? 'no listing files'
      : previous === null ? 'never published'
        : changed ? `changed (was ${previous.slice(0, 12)})`
          : 'unchanged';
    console.log(`${changed ? 'PUBLISH ' : 'skip    '}  ${name.padEnd(7)} ${current ? current.digest.slice(0, 12) : '-'.padEnd(12)}  ${reason}`);
    outputs.push(`${name}_changed=${changed}`);
    anyChanged = anyChanged || changed;
  }
  outputs.push(`any_changed=${anyChanged}`);

  if (process.env.GITHUB_OUTPUT)
    await fs.appendFile(process.env.GITHUB_OUTPUT, outputs.join('\n') + '\n');
}

await main();
