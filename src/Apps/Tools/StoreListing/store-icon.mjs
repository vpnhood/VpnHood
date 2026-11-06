#!/usr/bin/env node
/**
 * Re-encode iOS app icons without an alpha channel.
 *
 * Apple rejects an App Store icon that carries an alpha channel (ITMS-90717, "The App Store Icon
 * ... can't be transparent nor contain an alpha channel") and it rejects on the channel being
 * PRESENT, not on any pixel actually being translucent. The guidance is no transparency at any
 * icon size, so this walks the whole appiconset rather than only the 1024.
 *
 * Every icon is checked for real translucency first and left alone if it has any: dropping alpha
 * over translucent pixels changes how they composite, and the right backdrop is a design decision
 * rather than something to guess at. Where the alpha is fully opaque — which is the case for the
 * Client icons today — the re-encode is pixel-identical.
 *
 * Both directions are written by hand on Node's own zlib, so the tool needs no package: landing PNG
 * colour type 2 is the entire point, and no image library's defaults get a say in it.
 *
 * Usage:
 *   node store-icon.mjs --in <file-or-dir> --out <dir>    report, and write the results there
 *   node store-icon.mjs --in <file-or-dir> --in-place     rewrite the icons where they live
 */
import { promises as fs } from 'fs';
import path from 'path';
import zlib from 'zlib';

const args = process.argv.slice(2);
const argValue = (name, fallback) => {
  const i = args.indexOf(name);
  return i >= 0 && args[i + 1] ? args[i + 1] : fallback;
};
const inArg = argValue('--in', null);
if (!inArg) throw new Error('--in <file-or-dir> is required.');
const inPath = path.resolve(inArg);
const inPlace = args.includes('--in-place');
const outArg = argValue('--out', null);
if (!inPlace && !outArg) throw new Error('--out <dir> or --in-place is required.');
const outDir = inPlace ? null : path.resolve(outArg);

const CRC_TABLE = Array.from({ length: 256 }, (_, n) => {
  let c = n;
  for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
  return c >>> 0;
});

function crc32(buffer) {
  let c = 0xffffffff;
  for (const byte of buffer) c = CRC_TABLE[(c ^ byte) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}

function chunk(type, data) {
  const length = Buffer.alloc(4);
  length.writeUInt32BE(data.length);
  const typed = Buffer.concat([Buffer.from(type, 'ascii'), data]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(typed));
  return Buffer.concat([length, typed, crc]);
}

/** Truecolour (colour type 2), 8-bit, non-interlaced — the format Apple wants for the icon. */
function encodeRgbPng(rgba, width, height) {
  const stride = width * 3;
  const raw = Buffer.alloc((stride + 1) * height);
  for (let y = 0; y < height; y++) {
    raw[y * (stride + 1)] = 0; // filter: None
    for (let x = 0; x < width; x++) {
      const from = (y * width + x) * 4;
      const to = y * (stride + 1) + 1 + x * 3;
      raw[to] = rgba[from];
      raw[to + 1] = rgba[from + 1];
      raw[to + 2] = rgba[from + 2];
    }
  }

  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(width, 0);
  ihdr.writeUInt32BE(height, 4);
  ihdr[8] = 8;  // bit depth
  ihdr[9] = 2;  // colour type: truecolour, no alpha
  ihdr[10] = 0; // compression
  ihdr[11] = 0; // filter
  ihdr[12] = 0; // interlace

  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', ihdr),
    chunk('IDAT', zlib.deflateSync(raw, { level: 9 })),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}

/** PNG colour types 4 and 6 are the two that carry alpha. */
function hasAlphaChannel(buffer) {
  return buffer[25] === 4 || buffer[25] === 6;
}

/** PNG's filter for one byte, from the bytes to its left, above it and above-left of it. */
function predict(filter, left, up, upLeft) {
  switch (filter) {
    case 0: return 0;
    case 1: return left;
    case 2: return up;
    case 3: return (left + up) >> 1;
    case 4: {
      const estimate = left + up - upLeft;
      const fromLeft = Math.abs(estimate - left);
      const fromUp = Math.abs(estimate - up);
      const fromUpLeft = Math.abs(estimate - upLeft);
      if (fromLeft <= fromUp && fromLeft <= fromUpLeft) return left;
      return fromUp <= fromUpLeft ? up : upLeft;
    }
    default: throw new Error(`unknown PNG filter ${filter}.`);
  }
}

/** The pixels of a PNG that carries alpha, as RGBA. App icons are 8-bit and not interlaced. */
function decodeAlphaPng(buffer) {
  const width = buffer.readUInt32BE(16);
  const height = buffer.readUInt32BE(20);
  const bitDepth = buffer[24];
  const interlace = buffer[28];
  if (bitDepth !== 8 || interlace !== 0)
    throw new Error(`only an 8-bit, non-interlaced PNG can be read (bit depth ${bitDepth}, interlace ${interlace}).`);

  const compressed = [];
  for (let at = 8; at < buffer.length;) {
    const length = buffer.readUInt32BE(at);
    if (buffer.toString('ascii', at + 4, at + 8) === 'IDAT')
      compressed.push(buffer.subarray(at + 8, at + 8 + length));
    at += length + 12; // length, type and CRC around the data
  }
  const raw = zlib.inflateSync(Buffer.concat(compressed));

  const channels = buffer[25] === 6 ? 4 : 2; // RGBA, or grey and alpha
  const stride = width * channels;
  const rgba = Buffer.alloc(width * height * 4);
  let translucent = 0;
  let above = Buffer.alloc(stride);
  for (let y = 0; y < height; y++) {
    const start = y * (stride + 1);
    const filter = raw[start];
    const row = Buffer.from(raw.subarray(start + 1, start + 1 + stride));
    for (let i = 0; i < stride; i++) {
      const left = i >= channels ? row[i - channels] : 0;
      const upLeft = i >= channels ? above[i - channels] : 0;
      row[i] = (row[i] + predict(filter, left, above[i], upLeft)) & 0xff;
    }

    for (let x = 0; x < width; x++) {
      const from = x * channels;
      const to = (y * width + x) * 4;
      rgba[to] = row[from];
      rgba[to + 1] = channels === 4 ? row[from + 1] : row[from];
      rgba[to + 2] = channels === 4 ? row[from + 2] : row[from];
      rgba[to + 3] = row[from + channels - 1];
      if (rgba[to + 3] < 255) translucent++;
    }
    above = row;
  }

  return { width, height, rgba, translucent };
}

const stat = await fs.stat(inPath);
const files = stat.isDirectory()
  ? (await fs.readdir(inPath)).filter(f => f.toLowerCase().endsWith('.png')).map(f => path.join(inPath, f))
  : [inPath];
if (files.length === 0)
  throw new Error(`No PNGs found at ${inPath}.`);

if (!inPlace) await fs.mkdir(outDir, { recursive: true });

let rewritten = 0;
let skipped = 0;

for (const file of files) {
  const name = path.basename(file);
  const source = await fs.readFile(file);
  if (!hasAlphaChannel(source)) {
    console.log(`${name.padEnd(16)} already alpha-free — left alone`);
    continue;
  }

  const { width, height, rgba, translucent } = decodeAlphaPng(source);
  if (translucent > 0) {
    console.log(`${name.padEnd(16)} SKIPPED — ${translucent} translucent pixel(s); flatten against an intended background first`);
    skipped++;
    continue;
  }

  const encoded = encodeRgbPng(rgba, width, height);
  await fs.writeFile(inPlace ? file : path.join(outDir, name), encoded);
  console.log(`${name.padEnd(16)} ${String(width).padStart(4)}x${String(height).padEnd(4)} alpha removed, pixel-identical`);
  rewritten++;
}

const where = inPlace ? 'in place' : `to ${outDir}`;
console.log(`\n${rewritten} icon(s) rewritten ${where}${skipped ? `, ${skipped} skipped` : ''}.`);
