// The paired phone opens this page over plain http at a LAN address, which a browser does not count as a secure
// context, so it gives the page no crypto.subtle; .NET's runtime will not start without one (dotnet/runtime#133695).
// The runtime calls one thing of it, digest("SHA-256"), so that is all this provides. Where the browser has its own,
// on the app's own loopback page or over https, this does nothing.
if (globalThis.crypto && !globalThis.crypto.subtle) {
    const k = new Uint32Array([
        0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
        0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
        0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
        0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
        0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
        0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
        0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
        0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2]);

    // FIPS 180-4: the message, a 1 bit, zeros, and its length in bits as 64 bits, in 64-byte blocks
    const sha256 = bytes => {
        const length = bytes.length;
        const padded = new Uint8Array(((length + 72) >> 6) << 6);
        padded.set(bytes);
        padded[length] = 0x80;
        const view = new DataView(padded.buffer);
        view.setUint32(padded.length - 8, Math.floor(length / 0x20000000));
        view.setUint32(padded.length - 4, (length << 3) >>> 0);

        const h = new Uint32Array([
            0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a, 0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19]);
        const w = new Uint32Array(64);
        for (let offset = 0; offset < padded.length; offset += 64) {
            for (let i = 0; i < 16; i++)
                w[i] = view.getUint32(offset + i * 4);
            for (let i = 16; i < 64; i++) {
                const x = w[i - 15], y = w[i - 2];
                const s0 = ((x >>> 7) | (x << 25)) ^ ((x >>> 18) | (x << 14)) ^ (x >>> 3);
                const s1 = ((y >>> 17) | (y << 15)) ^ ((y >>> 19) | (y << 13)) ^ (y >>> 10);
                w[i] = w[i - 16] + s0 + w[i - 7] + s1;
            }
            let a = h[0], b = h[1], c = h[2], d = h[3], e = h[4], f = h[5], g = h[6], hh = h[7];
            for (let i = 0; i < 64; i++) {
                const s1 = ((e >>> 6) | (e << 26)) ^ ((e >>> 11) | (e << 21)) ^ ((e >>> 25) | (e << 7));
                const t1 = (hh + s1 + ((e & f) ^ (~e & g)) + k[i] + w[i]) | 0;
                const s0 = ((a >>> 2) | (a << 30)) ^ ((a >>> 13) | (a << 19)) ^ ((a >>> 22) | (a << 10));
                const t2 = (s0 + ((a & b) ^ (a & c) ^ (b & c))) | 0;
                hh = g; g = f; f = e; e = (d + t1) | 0; d = c; c = b; b = a; a = (t1 + t2) | 0;
            }
            h[0] += a; h[1] += b; h[2] += c; h[3] += d; h[4] += e; h[5] += f; h[6] += g; h[7] += hh;
        }

        const digest = new Uint8Array(32);
        const digestView = new DataView(digest.buffer);
        for (let i = 0; i < 8; i++)
            digestView.setUint32(i * 4, h[i]);
        return digest.buffer;
    };

    const subtle = {
        digest: async (algorithm, data) => {
            const name = typeof algorithm === 'string' ? algorithm : algorithm?.name;
            if (String(name).toUpperCase() !== 'SHA-256')
                throw new Error(`crypto.subtle.digest("${name}"): a page that is not a secure context has SHA-256 only.`);
            return sha256(ArrayBuffer.isView(data)
                ? new Uint8Array(data.buffer, data.byteOffset, data.byteLength)
                : new Uint8Array(data));
        }
    };
    Object.defineProperty(globalThis.crypto, 'subtle', { value: subtle, configurable: true });
}
