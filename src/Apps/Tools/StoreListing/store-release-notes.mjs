#!/usr/bin/env node
/**
 * Store RELEASE NOTES ("What's New") from the monorepo CHANGELOG, translated per line.
 *
 * The monorepo's hand-maintained CHANGELOG.md is the single authoring surface: its FIRST H1
 * section (the leading "# Latest" block — the same rule the GitHub release note uses, see
 * pub/lib/utils/ChangelogUtils.ps1 in vpnhood/VpnHood) is the current release's notes. Lines
 * carry trailing tags that decide where each one ships:
 *
 *   #client / #connect                 product: untagged lines ship with both apps, tagged lines
 *                                      only with the named product(s)
 *   #android #ios #windows #linux     platforms, INCLUSIVE: no platform tag = every platform;
 *                                      tagged = only the platforms listed (`#android #ios` ships
 *                                      to both stores and is dropped for desktop)
 *   #store                             marks the line for Google Play's short per-release note
 *                                      (Play caps a changelog at 500 characters — the full
 *                                      section does not fit; iOS gets the full note, cap 4000)
 *
 * Two subcommands, run in this order with vhtranslator between them (the store repo's
 * update-release-notes.yml drives all three):
 *
 *   extract   CHANGELOG -> store-i18n/en-US/release-notes.json   flat {key: line} — the
 *                          vhtranslator source (translated per line, incrementally: an unchanged
 *                          line is never retranslated, a recurring line reuses its translation)
 *                       -> store-i18n/release-notes.map.json     ordered line list with each
 *                          line's platforms/store routing (kept out of the translated file so
 *                          the model never sees routing data)
 *   compile   store-i18n -> fastlane/metadata/ios/<locale>/release_notes.txt        (full note)
 *                        -> fastlane/metadata/android/<locale>/changelogs/default.txt (#store
 *                           lines; fastlane supply uses default.txt for whatever versionCode the
 *                           release uploads, so the file needs no per-version naming)
 *
 * Keys are content hashes of the English line, so editing a line replaces its key (old
 * translations are pruned by vhtranslator, the new line is translated fresh) while inserting or
 * reordering lines touches nothing else.
 *
 * Per-app config is two arguments: `--product client|connect` picks the product tag, and
 * `--no-ios` suppresses — and DELETES — the iOS files, because Apple allows no "What's New" on an
 * app's FIRST App Store version and rejects a v1 submission that carries one (drop it once 1.0 is
 * live).
 *
 * Fails loudly on: unknown or mid-line tags, duplicate lines, store character limits per locale,
 * platform names in iOS notes (Guideline 2.3.10), and a locale whose translations are out of
 * step with the source (run vhtranslator). A repo without release-notes files has simply not
 * adopted the feature: compile warns and skips (the fork-friendliness convention), while every
 * present-but-wrong state is an error.
 *
 * Usage:
 *   node store-release-notes.mjs extract --changelog ../VpnHood/CHANGELOG.md \
 *        --root <store repo> --product client|connect
 *   node store-release-notes.mjs compile --root <store repo> --product client|connect [--no-ios] [--check]
 */
import { createHash } from 'crypto';
import { promises as fs } from 'fs';
import path from 'path';

const args = process.argv.slice(2);
const command = args[0];
if (command !== 'extract' && command !== 'compile')
  throw new Error('first argument must be "extract" or "compile" — see the header comment.');
const argValue = (name, fallback) => {
  const i = args.indexOf(name);
  return i >= 0 && args[i + 1] ? args[i + 1] : fallback;
};
const checkOnly = args.includes('--check');

const rootArg = argValue('--root', null);
if (!rootArg) throw new Error('--root <store repo> is required.');
const root = path.resolve(rootArg);
const i18nDir = path.join(root, 'store-i18n');
const sourceFile = path.join(i18nDir, 'en-US', 'release-notes.json');
const mapFile = path.join(i18nDir, 'release-notes.map.json');

const PRODUCTS = ['client', 'connect'];
const product = argValue('--product', null);
if (!PRODUCTS.includes(product)) throw new Error(`--product must be one of: ${PRODUCTS.join(', ')}.`);
const skipIos = args.includes('--no-ios');
const PLATFORMS = ['android', 'ios', 'windows', 'linux'];
const KNOWN_TAGS = new Set([...PRODUCTS, ...PLATFORMS, 'store']);

/** Store limits count characters, not code units; a trailing newline is layout, not content. */
const charCount = (text) => [...text.replace(/\n$/, '')].length;

/** Same per-store locale-folder rule as store-metadata.mjs: a `stores`
 * override wins (null = the store has no such locale), the tag otherwise. */
const storeFolder = (storeKey, locale) =>
  locale.stores && storeKey in locale.stores ? locale.stores[storeKey] : locale.tag;

async function readLocales() {
  const file = path.join(i18nDir, 'locales.json');
  const raw = await fs.readFile(file, 'utf8').catch(() => null);
  if (raw === null)
    throw new Error(`${file} is missing — it names the app's store locales (first entry = source language).`);
  const { locales } = JSON.parse(raw);
  if (!locales?.length) throw new Error(`${file} declares no locales.`);
  return locales;
}

// ---------------------------------------------------------------------------------------------
// extract: CHANGELOG first H1 section -> store-i18n source + routing map
// ---------------------------------------------------------------------------------------------
async function extract() {
  const changelogFile = argValue('--changelog', null);
  if (!changelogFile) throw new Error('extract needs --changelog <path to the monorepo CHANGELOG.md>.');
  const content = await fs.readFile(path.resolve(changelogFile), 'utf8');

  // The first H1 section: from the first "# " heading up to (excluding) the next one.
  const lines = content.split(/\r\n|\n|\r/);
  const start = lines.findIndex(l => /^#\s+/.test(l.trimEnd()));
  if (start < 0) throw new Error(`${changelogFile} has no H1 ("# ...") section.`);
  const section = [];
  for (let i = start + 1; i < lines.length && !/^#\s+/.test(lines[i].trimEnd()); i++) section.push(lines[i]);

  const entries = [];
  for (const rawLine of section) {
    if (!rawLine.trim()) continue;

    // Trailing tag run only — the CHANGELOG convention ("... #connect #android"). A known tag
    // anywhere else would silently disagree with the GitHub release note's substring filter, so
    // it is an error, not a guess.
    const tagRun = rawLine.match(/(\s*#\w+)+\s*$/);
    const tags = tagRun ? [...tagRun[0].matchAll(/#(\w+)/g)].map(m => m[1].toLowerCase()) : [];
    const body = (tagRun ? rawLine.slice(0, tagRun.index) : rawLine).trim();
    for (const tag of tags)
      if (!KNOWN_TAGS.has(tag))
        throw new Error(`CHANGELOG line "${rawLine.trim()}": unknown tag "#${tag}" — known: ${[...KNOWN_TAGS].map(t => '#' + t).join(' ')}.`);
    const strayTag = [...body.matchAll(/#(\w+)/g)].find(m => KNOWN_TAGS.has(m[1].toLowerCase()));
    if (strayTag)
      throw new Error(`CHANGELOG line "${rawLine.trim()}": tag "#${strayTag[1]}" must be at the END of the line.`);

    // Store the line without its "* " bullet — the compiler re-adds a uniform one, and the
    // translator is never shown markup it could damage.
    const text = body.replace(/^[*-]\s+/, '').replace(/\s+/g, ' ').trim();
    if (!text) continue;

    const products = tags.filter(t => PRODUCTS.includes(t));
    if (products.length && !products.includes(product)) continue; // the other product's line

    const key = 'n_' + createHash('sha1').update(text, 'utf8').digest('hex').slice(0, 10);
    const clash = entries.find(e => e.key === key);
    if (clash)
      throw new Error(clash.text === text
        ? `CHANGELOG: duplicate line "${text}" in the current section.`
        : `hash collision between "${clash.text}" and "${text}" — widen the key length.`);
    entries.push({ key, text, platforms: tags.filter(t => PLATFORMS.includes(t)), store: tags.includes('store') });
  }
  if (!entries.length)
    throw new Error(`the CHANGELOG's first H1 section has no lines for product "${product}".`);

  const source = Object.fromEntries(entries.map(e => [e.key, e.text]));
  const map = {
    comment: 'Generated by store-release-notes.mjs from the monorepo CHANGELOG — never hand-edit. ' +
      'Order is release-note order; platforms [] = every platform; store = in Google Play\'s short note.',
    lines: entries.map(({ key, platforms, store }) => ({ key, platforms, store })),
  };
  await fs.mkdir(path.dirname(sourceFile), { recursive: true });
  await fs.writeFile(sourceFile, JSON.stringify(source, null, 2) + '\n');
  await fs.writeFile(mapFile, JSON.stringify(map, null, 2) + '\n');
  console.log(`extracted ${entries.length} lines (${entries.filter(e => e.store).length} #store) -> ${path.relative(root, sourceFile)}`);
}

// ---------------------------------------------------------------------------------------------
// compile: store-i18n translations -> fastlane release-note files
// ---------------------------------------------------------------------------------------------

/** What each store's release note is made of. Limits are the stores' own hard caps. */
const OUTPUTS = {
  ios: {
    storeKey: 'appStore',
    max: 4000,
    file: (tag) => path.join(root, 'fastlane', 'metadata', 'ios', tag, 'release_notes.txt'),
    pick: (line) => !line.platforms.length || line.platforms.includes('ios'),
    // Guideline 2.3.10 — same lint as store-metadata.mjs: iOS copy never names another platform.
    forbidden: /android|google play|windows|linux|samsung/i,
  },
  android: {
    storeKey: 'googlePlay',
    max: 500,
    file: (tag) => path.join(root, 'fastlane', 'metadata', 'android', tag, 'changelogs', 'default.txt'),
    pick: (line) => line.store && (!line.platforms.length || line.platforms.includes('android')),
  },
};

async function compile() {
  const mapRaw = await fs.readFile(mapFile, 'utf8').catch(() => null);
  if (mapRaw === null) {
    console.warn(`::warning title=Release notes not compiled::${path.relative(root, mapFile)} does not exist — ` +
      'this repo has not adopted CHANGELOG-driven release notes (run the extract step first).');
    return;
  }
  const map = JSON.parse(mapRaw);
  const locales = await readLocales();
  const source = locales[0];
  const mapKeys = map.lines.map(l => l.key);

  const readNotes = async (locale) => {
    const file = path.join(i18nDir, locale.tag, 'release-notes.json');
    const raw = await fs.readFile(file, 'utf8').catch(() => null);
    if (raw === null)
      throw new Error(`${file} is missing — run vhtranslator to generate it from the source folder.`);
    const data = JSON.parse(raw);
    const missing = mapKeys.filter(k => !(k in data));
    const extra = Object.keys(data).filter(k => !mapKeys.includes(k));
    if (missing.length || extra.length)
      throw new Error(`${locale.tag}/release-notes.json is out of step with release-notes.map.json` +
        `${missing.length ? `; missing: ${missing.join(', ')}` : ''}${extra.length ? `; stale: ${extra.join(', ')}` : ''} — ` +
        (locale === source ? 're-run the extract step.' : 'run vhtranslator.'));
    return data;
  };
  await readNotes(source); // validates the source/map pair before any locale work

  for (const [store, spec] of Object.entries(OUTPUTS)) {
    const picked = map.lines.filter(spec.pick);

    // iOS off (pre-1.0): the files must not merely be skipped — an already-present one would ride
    // the next listing publish into a first-version submission Apple rejects, so delete them.
    if (store === 'ios' && skipIos) {
      for (const locale of locales) {
        const folder = storeFolder(spec.storeKey, locale);
        if (folder === null) continue;
        if (await fs.stat(spec.file(folder)).catch(() => null)) {
          if (!checkOnly) await fs.rm(spec.file(folder));
          console.log(`${checkOnly ? 'would remove' : 'removed'}   ios ${locale.tag}: --no-ios (no "What's New" before the first App Store version)`);
        }
      }
      continue;
    }
    if (!picked.length)
      throw new Error(store === 'android'
        ? 'no #store lines: tag the few CHANGELOG lines that make Google Play\'s short note (500-char cap) and re-extract.'
        : `no release-note lines ship to ${store} — a release must say something.`);

    for (const locale of locales) {
      const folder = storeFolder(spec.storeKey, locale);
      if (folder === null) {
        console.log(`skipped   ${store.padEnd(7)} ${locale.tag}: this store has no such locale`);
        continue;
      }
      const data = await readNotes(locale);
      const text = picked.map(l => `* ${data[l.key]}`).join('\n') + '\n';
      const count = charCount(text);
      if (count > spec.max)
        throw new Error(`${store}/${locale.tag}: the release note is ${count} characters — the store allows ${spec.max}` +
          (store === 'android' ? ' (fewer or shorter #store lines).' : '.'));
      if (spec.forbidden) {
        const banned = text.match(spec.forbidden);
        if (banned)
          throw new Error(`${store}/${locale.tag}: the release note contains "${banned[0]}" — App Store metadata must ` +
            'not name another platform (Guideline 2.3.10). Tag the line with its platforms, or reword it.');
      }
      if (checkOnly) continue;
      await fs.mkdir(path.dirname(spec.file(folder)), { recursive: true });
      await fs.writeFile(spec.file(folder), text);
      console.log(`compiled  ${store.padEnd(7)} ${locale.tag} (${count} chars) -> ${path.relative(root, spec.file(folder))}`);
    }
  }
  if (checkOnly) console.log('check passed — nothing written');
}

await (command === 'extract' ? extract() : compile());
