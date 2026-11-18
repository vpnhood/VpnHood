// ReSharper disable IdentifierTypo
// ReSharper disable CommentTypo
// ReSharper disable StringLiteralTypo

using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace VpnHood.Core.Filtering.DomainFiltering.SniExtractors.Quic;

/// <summary>
/// Extracts SNI from QUIC Initial packets.
/// This is a stateful extractor that can handle multi-packet CRYPTO frame reassembly.
/// Flow management is handled by PacketSniService.
/// </summary>
internal static class QuicSniExtractor
{
    // RFC9001 (v1) & RFC9369 (v2) salts
    private static readonly byte[] V1Salt = [
        0x38, 0x76, 0x2c, 0xf7, 0xf5, 0x59, 0x34, 0xb3, 0x4d, 0x17, 0x9a, 0xe6, 0xa4, 0xc8, 0x0c, 0xad, 0xcc, 0xbb,
        0x7f, 0x0a
    ];

    private static readonly byte[] V2Salt = [
        0x0d, 0xed, 0xe3, 0xde, 0xf7, 0x00, 0xa6, 0xdb, 0x81, 0x93, 0x81, 0xbe, 0x6e, 0x26, 0x9d, 0xcb, 0xf9, 0xbd,
        0x2e, 0xd9
    ];

    private const uint V1Version = 0x00000001;
    private const uint V2Version = 0x6b3343cf;

    // The time a ClientHello has for all its Initials, as a TCP connection has for its name (DomainFilteringService):
    // long enough for most clients' first resend of a lost datagram
    private static readonly long NameTimeoutTicks = TimeSpan.FromSeconds(1).Ticks;

    private static readonly QuicSniResult NotFoundResult = new() { DomainName = null, NeedMore = false, State = null };

    /// <summary>
    /// Feed a single UDP payload (datagram body, no UDP/IP headers).
    /// Pass back the returned State on the next packet for this flow if Outcome==NeedMore.
    /// </summary>
    /// <remarks>
    /// A flow's first datagram must hold a client Initial of QUIC version 1 or 2 that decrypts with the keys of its
    /// own destination connection ID, or the flow is no QUIC that can be read, and is given up at once: a header check
    /// alone passed half of all DNS queries, which were held for a name they never hold. A flow that decrypted is
    /// QUIC: its ClientHello not complete within three datagrams, or by a datagram that comes after a second, is
    /// incomplete, which fails its flow closed; a whole ClientHello with no name is given up at once. A result that
    /// comes of a decrypted Initial gives its connection ID, which the decryption proved.
    /// </remarks>
    public static QuicSniResult TryExtractSniFromUdpPayload(
        ReadOnlySpan<byte> udpPayload,
        QuicSniState? state = null,
        long nowTicks = 0)
    {
        if (nowTicks == 0) nowTicks = Environment.TickCount64 * TimeSpan.TicksPerMillisecond;

        // bootstrap state from first Initial we see
        var isFirst = state == null;
        if (state == null) {
            if (!LooksLikeInitial(udpPayload, out var isV2, out var dcid))
                return NotFoundResult;

            state = CreateState(isV2, dcid, nowTicks);
        }

        // budget / timeout
        if (state.PacketBudget <= 0 || nowTicks > state.DeadlineTicks)
            return Incomplete(state);

        var (sawAnyInitial, decrypted) = CollectInitials(udpPayload, state);
        if (isFirst && !decrypted)
            return NotFoundResult;

        if (sawAnyInitial)
            state.PacketBudget--;

        // assemble contiguous CRYPTO stream starting at offset 0 and try to parse SNI
        var assembled = AssembleContiguousFromZero(state.Segments, state.MaxBytes);
        if (assembled.Length != 0) {
            var sni = TryParseSniFromClientHelloPartial(assembled);
            if (sni != null)
                return new QuicSniResult { DomainName = sni, NeedMore = false, State = null, ConnectionId = state.Dcid };

            // all of it is in, with no name, as a ClientHello to an address, or it is no ClientHello
            if (assembled[0] != 1 || IsWholeHandshakeMessage(assembled))
                return new QuicSniResult { DomainName = null, NeedMore = false, State = null, ConnectionId = state.Dcid };
        }

        // still need more?
        if (state.PacketBudget <= 0 || nowTicks > state.DeadlineTicks)
            return Incomplete(state);

        return new QuicSniResult { DomainName = null, NeedMore = true, State = state };
    }

    // A state lives only once an Initial decrypted, so its flow is QUIC: a name not found in time fails it closed,
    // whether a ClientHello began or not
    private static QuicSniResult Incomplete(QuicSniState state) =>
        new() { DomainName = null, NeedMore = false, State = null, IsIncomplete = true, ConnectionId = state.Dcid };

    private static QuicSniState CreateState(bool isV2, ReadOnlySpan<byte> dcid, long nowTicks)
    {
        var state = new QuicSniState {
            IsV2 = isV2,
            Dcid = [.. dcid],
            PacketBudget = 3,
            DeadlineTicks = nowTicks + NameTimeoutTicks,
            MaxBytes = 64 * 1024
        };
        DeriveInitialSecrets(state);
        return state;
    }

    // Walks a datagram's coalesced packets, and decrypts the client Initials of the state's connection ID into its
    // CRYPTO segments
    private static (bool SawAnyInitial, bool Decrypted) CollectInitials(ReadOnlySpan<byte> udpPayload,
        QuicSniState state)
    {
        var off = 0;
        var sawAnyInitial = false;
        var decrypted = false;

        while (off < udpPayload.Length) {
            if (!IsLongHeader(udpPayload, off)) break;

            if (!TryParseLongHeader(
                    udpPayload, off,
                    out var version, out var typeBits,
                    out var pnOffset, out var headerLenUpToPn,
                    out var lengthField, out var totalPacketBytes,
                    out var dcidHdr))
                break;

            var pktIsV2 = version == V2Version;
            var isInitialPkt = pktIsV2 ? typeBits == 0b01 : typeBits == 0b00;

            // ensure secrets match the actual packet (first time)
            if (!state.SecretsReady) {
                state.IsV2 = pktIsV2;
                state.Dcid = [.. dcidHdr];
                DeriveInitialSecrets(state);
            }

            // only decrypt client Initials whose DCID matches
            if (isInitialPkt && DcidEquals(dcidHdr, state.Dcid)) {
                sawAnyInitial = true;
                decrypted |= TryDecryptAndCollectCrypto(udpPayload, off, pnOffset, headerLenUpToPn, lengthField,
                    state);
            }

            if (totalPacketBytes <= 0) break;
            off += totalPacketBytes;
        }

        return (sawAnyInitial, decrypted);
    }

    // a handshake message whose length its header gives, all of it here
    private static bool IsWholeHandshakeMessage(ReadOnlySpan<byte> buf) =>
        buf.Length >= 4 && 4 + ((buf[1] << 16) | (buf[2] << 8) | buf[3]) <= buf.Length;

    /// <summary>
    /// Gets the destination connection ID of a datagram whose first packet has the header of a client Initial of QUIC
    /// version 1 or 2, by its header alone, which no decryption has proved.
    /// </summary>
    /// <returns><see langword="true"/> if the header is such an Initial's.</returns>
    public static bool TryGetInitialConnectionId(ReadOnlySpan<byte> udpPayload, out ReadOnlySpan<byte> connectionId)
    {
        return LooksLikeInitial(udpPayload, out _, out connectionId);
    }

    /// <summary>
    /// Gets whether a datagram holds a client Initial that decrypts with the keys of its own destination connection ID,
    /// as a connection's first Initials do.
    /// </summary>
    /// <remarks>
    /// An Initial that a client sends after the server's first answer names the server's connection ID, from which its
    /// keys are not made, so it does not; after a Retry, the client's Initial names the Retry's connection ID and makes
    /// its keys from it, so it does.
    /// </remarks>
    public static bool IsAuthenticInitial(ReadOnlySpan<byte> udpPayload, long nowTicks)
    {
        if (!LooksLikeInitial(udpPayload, out var isV2, out var dcid))
            return false;

        var state = CreateState(isV2, dcid, nowTicks);
        return CollectInitials(udpPayload, state).Decrypted;
    }

    // ---------- Fast checks & header parsing ----------

    // The header of a client Initial of version 1 or 2, the only ones whose keys the reader makes, with a connection
    // ID no longer than version 1 allows: checked before any key is made, as most UDP is no QUIC
    private static bool LooksLikeInitial(ReadOnlySpan<byte> udp, out bool isV2, out ReadOnlySpan<byte> dcid)
    {
        isV2 = false;
        dcid = default;
        if (udp.Length < 7) return false;
        if ((udp[0] & 0x80) == 0) return false; // long header
        var ver = BinaryPrimitives.ReadUInt32BigEndian(udp.Slice(1, 4));
        if (ver is not (V1Version or V2Version)) return false;
        isV2 = ver == V2Version;
        if (((udp[0] >> 4) & 0x03) != (isV2 ? 0b01 : 0b00)) return false; // Initial

        var p = 5;
        if (udp.Length < p + 1) return false;
        int dcidLen = udp[p++];
        if (dcidLen > 20) return false;
        if (udp.Length < p + dcidLen) return false;
        dcid = udp.Slice(p, dcidLen);

        // skip SCID
        p += dcidLen;
        if (udp.Length < p + 1) return false;
        int scidLen = udp[p++];
        p += scidLen;
        if (udp.Length < p) return false;

        // Initial has token; verify length field is present & fits in datagram
        if (!TryReadVarInt(udp, ref p, out var tokenLen)) return false;
        p += (int)tokenLen;
        if (!TryReadVarInt(udp, ref p, out var len)) return false;

        var totalBytes = p + (int)len; // header up to PN + Length
        return totalBytes <= udp.Length;
    }

    private static bool IsLongHeader(ReadOnlySpan<byte> b, int off)
        => b.Length - off >= 1 && (b[off] & 0x80) != 0;

    private static bool TryParseLongHeader(
        ReadOnlySpan<byte> b, int off,
        out uint version, out int typeBits,
        out int pnOffset, out int headerLenUpToPn,
        out ulong lengthField, out int totalPacketBytes,
        out ReadOnlySpan<byte> dcid)
    {
        version = 0;
        typeBits = 0;
        pnOffset = 0;
        headerLenUpToPn = 0;
        lengthField = 0;
        totalPacketBytes = 0;
        dcid = default;

        if (b.Length - off < 7) return false;

        var first = b[off];
        typeBits = (first >> 4) & 0x03;
        version = BinaryPrimitives.ReadUInt32BigEndian(b.Slice(off + 1, 4));

        var p = off + 5;

        if (b.Length < p + 1) return false;
        int dcidLen = b[p++];
        if (b.Length < p + dcidLen) return false;
        dcid = b.Slice(p, dcidLen);
        p += dcidLen;

        if (b.Length < p + 1) return false;
        int scidLen = b[p++];
        p += scidLen;
        if (b.Length < p) return false;

        // only Initial has a token
        var isInitial = version == V2Version ? typeBits == 0b01 : typeBits == 0b00;
        if (isInitial) {
            if (!TryReadVarInt(b, ref p, out var tokenLen)) return false;
            p += (int)tokenLen;
        }

        if (!TryReadVarInt(b, ref p, out lengthField)) return false;

        pnOffset = p;
        headerLenUpToPn = pnOffset - off;
        totalPacketBytes = headerLenUpToPn + (int)lengthField;

        if (totalPacketBytes < 0 || off + totalPacketBytes > b.Length) return false;
        return true;
    }

    private static bool DcidEquals(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        => a.Length == b.Length && a.SequenceEqual(b);


    private static void DeriveInitialSecrets(QuicSniState st)
    {
        var salt = st.IsV2 ? V2Salt : V1Salt;
        var labelKey = st.IsV2 ? "quicv2 key" : "quic key";
        var labelIv = st.IsV2 ? "quicv2 iv" : "quic iv";
        var labelHp = st.IsV2 ? "quicv2 hp" : "quic hp";

        var initialSecret = HkdfExpandLabel(HkdfExtract(salt, st.Dcid), "client in", 32);
        st.Key = HkdfExpandLabel(initialSecret, labelKey, 16);
        st.Iv = HkdfExpandLabel(initialSecret, labelIv, 12);
        st.Hp = HkdfExpandLabel(initialSecret, labelHp, 16);
        st.SecretsReady = true;
    }

    // whether the packet decrypted, which its AEAD tag proves it a client Initial of the state's connection ID
    private static bool TryDecryptAndCollectCrypto(
        ReadOnlySpan<byte> b, int off, int pnOffset, int headerLenUpToPn, ulong lengthField,
        QuicSniState st)
    {
        // sample & mask
        var sampleOffset = pnOffset + 4;
        if (b.Length < sampleOffset + 16) return false;

        // Use stackalloc for small fixed-size buffers
        Span<byte> sample = stackalloc byte[16];
        Span<byte> mask = stackalloc byte[16];
        b.Slice(sampleOffset, 16).CopyTo(sample);

        using (var aes = Aes.Create()) {
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;
            aes.Key = st.Hp;
            using var enc = aes.CreateEncryptor();
            var sampleArr = ArrayPool<byte>.Shared.Rent(16);
            var maskArr = ArrayPool<byte>.Shared.Rent(16);
            try {
                sample.CopyTo(sampleArr);
                enc.TransformBlock(sampleArr, 0, 16, maskArr, 0);
                maskArr.AsSpan(0, 16).CopyTo(mask);
            }
            finally {
                ArrayPool<byte>.Shared.Return(sampleArr);
                ArrayPool<byte>.Shared.Return(maskArr);
            }
        }

        var first = b[off];
        var unmaskedFirst = (byte)(first ^ (mask[0] & 0x0F));
        var pnLen = (unmaskedFirst & 0x03) + 1;

        if (pnLen < 1 || pnLen > 4 || b.Length < pnOffset + pnLen)
            return false;

        // unmask PN using stackalloc
        Span<byte> pnField = stackalloc byte[4];
        for (var i = 0; i < pnLen; i++)
            pnField[i] = (byte)(b[pnOffset + i] ^ mask[1 + i]);

        // AAD = header (to PN) with first byte unmasked + PN
        var aadLen = headerLenUpToPn + pnLen;
        var aad = aadLen <= 128 ? stackalloc byte[aadLen] : new byte[aadLen];
        b.Slice(off, headerLenUpToPn).CopyTo(aad);
        aad[0] = unmaskedFirst;
        for (var i = 0; i < pnLen; i++) aad[headerLenUpToPn + i] = pnField[i];

        // Nonce = iv XOR packet_number (left-padded)
        ulong pnVal = 0;
        for (var i = 0; i < pnLen; i++) pnVal = (pnVal << 8) | pnField[i];

        Span<byte> nonce = stackalloc byte[12];
        st.Iv.CopyTo(nonce);
        for (var i = 0; i < 8; i++)
            nonce[nonce.Length - 1 - i] ^= (byte)(pnVal >> (8 * i));

        // ciphertext (payload incl tag) is after PN; lengthField = PN + payload + tag
        var ctOffset = pnOffset + pnLen;
        var ctLen = (int)lengthField - pnLen;
        if (ctLen <= 16 || b.Length < ctOffset + ctLen)
            return false;

        var ptLen = ctLen - 16;

        // Rent all buffers from pool — plaintext never escapes this method
        var ciphertext = ArrayPool<byte>.Shared.Rent(ptLen);
        var tag = ArrayPool<byte>.Shared.Rent(16);
        var plaintext = ArrayPool<byte>.Shared.Rent(ptLen);

        try {
            b.Slice(ctOffset, ptLen).CopyTo(ciphertext);
            b.Slice(ctOffset + ptLen, 16).CopyTo(tag);

            using var aead = new AesGcm(st.Key, 16);
            aead.Decrypt(nonce, ciphertext.AsSpan(0, ptLen), tag.AsSpan(0, 16), plaintext.AsSpan(0, ptLen), aad);
            CollectCryptoSegments(plaintext.AsSpan(0, ptLen), st.Segments);
            return true;
        }
        catch (AuthenticationTagMismatchException) {
            // not this connection's Initial, or no QUIC at all: nothing to collect
            return false;
        }
        finally {
            ArrayPool<byte>.Shared.Return(ciphertext);
            ArrayPool<byte>.Shared.Return(tag);
            ArrayPool<byte>.Shared.Return(plaintext);
        }
    }

    private static void CollectCryptoSegments(ReadOnlySpan<byte> plain, List<(ulong Off, byte[] Data)> segs)
    {
        var p = 0;
        while (p < plain.Length) {
            var ft = plain[p++];

            if (ft == 0x00 || ft == 0x01) continue; // PADDING, PING
            if (ft == 0x02 || ft == 0x03) {
                if (!SkipAckFrame(plain, ref p, ft == 0x03)) return;
                continue;
            }

            if (ft == 0x06) // CRYPTO
            {
                if (!TryReadVarInt(plain, ref p, out var off)) return;
                if (!TryReadVarInt(plain, ref p, out var len)) return;
                if (len > (ulong)(plain.Length - p)) return;

                var data = plain.Slice(p, (int)len).ToArray();
                segs.Add((off, data));
                p += (int)len;
                continue;
            }

            // other frames in client Initial are uncommon — stop to avoid mis-parse
            return;
        }
    }

    private static bool SkipAckFrame(ReadOnlySpan<byte> s, ref int p, bool withEcn)
    {
        if (!TryReadVarInt(s, ref p, out _)) return false; // largest_acked
        if (!TryReadVarInt(s, ref p, out _)) return false; // ack_delay
        if (!TryReadVarInt(s, ref p, out var rangeCount)) return false;
        if (!TryReadVarInt(s, ref p, out _)) return false; // first_ack_range

        for (ulong i = 0; i < rangeCount; i++) {
            if (!TryReadVarInt(s, ref p, out _)) return false; // gap
            if (!TryReadVarInt(s, ref p, out _)) return false; // ack_range_len
        }

        if (withEcn) {
            if (!TryReadVarInt(s, ref p, out _)) return false;
            if (!TryReadVarInt(s, ref p, out _)) return false;
            if (!TryReadVarInt(s, ref p, out _)) return false;
        }

        return true;
    }

    private static byte[] AssembleContiguousFromZero(List<(ulong Off, byte[] Data)> segments, int maxBytes)
    {
        if (segments.Count == 0) return [];
        segments.Sort((a, b) => a.Off.CompareTo(b.Off));

        // Calculate required size first to avoid reallocations
        ulong totalSize = 0;
        ulong cur = 0;
        foreach (var s in segments) {
            if (s.Off > cur) break;
            var overlap = (int)Math.Max(0, (long)(cur - s.Off));
            var toCopy = Math.Min(s.Data.Length - overlap, maxBytes - (int)cur);
            if (toCopy <= 0) continue;
            totalSize = cur + (ulong)toCopy;
            cur = totalSize;
            if (totalSize >= (ulong)maxBytes) break;
        }

        if (totalSize == 0) return [];

        var result = new byte[totalSize];
        cur = 0;

        foreach (var s in segments) {
            if (s.Off > cur) break;
            var overlap = (int)Math.Max(0, (long)(cur - s.Off));
            var toCopy = Math.Min(s.Data.Length - overlap, (int)(totalSize - cur));
            if (toCopy <= 0) continue;

            Buffer.BlockCopy(s.Data, overlap, result, (int)cur, toCopy);
            cur += (ulong)toCopy;
            if (cur >= totalSize) break;
        }

        return result;
    }

    private static string? TryParseSniFromClientHelloPartial(ReadOnlySpan<byte> buf)
    {
        // TLS Handshake (QUIC carries raw handshake; no TLS record layer)
        if (buf.Length < 4 || buf[0] != 1) return null; // client_hello
        var total = (buf[1] << 16) | (buf[2] << 8) | buf[3];
        var end = 4 + total;
        if (end > buf.Length) return null; // need more

        var p = 4;

        // legacy_version + random
        if (p + 2 + 32 > buf.Length) return null;
        p += 2 + 32;

        // session_id
        if (p + 1 > buf.Length) return null;
        int sidLen = buf[p++];
        if (p + sidLen > buf.Length) return null;
        p += sidLen;

        // cipher_suites
        if (p + 2 > buf.Length) return null;
        var csLen = (buf[p] << 8) | buf[p + 1];
        p += 2;
        if (p + csLen > buf.Length) return null;
        p += csLen;

        // compression_methods
        if (p + 1 > buf.Length) return null;
        int compLen = buf[p++];
        if (p + compLen > buf.Length) return null;
        p += compLen;

        // extensions
        if (p + 2 > buf.Length) return null;
        var extTotal = (buf[p] << 8) | buf[p + 1];
        p += 2;
        var extEnd = p + extTotal;
        if (extEnd > buf.Length) return null;

        while (p + 4 <= extEnd) {
            var extType = (buf[p] << 8) | buf[p + 1];
            p += 2;
            var extLen = (buf[p] << 8) | buf[p + 1];
            p += 2;
            if (p + extLen > extEnd) return null;

            if (extType == 0x0000) // server_name
            {
                var q = p;
                if (q + 2 > p + extLen) return null;
                var listLen = (buf[q] << 8) | buf[q + 1];
                q += 2;
                if (q + listLen > p + extLen) return null;

                while (q + 3 <= p + extLen) {
                    var nameType = buf[q++]; // 0 = host_name
                    var nameLen = (buf[q] << 8) | buf[q + 1];
                    q += 2;
                    if (q + nameLen > p + extLen) return null;
                    if (nameType == 0)
                        return Encoding.ASCII.GetString(buf.Slice(q, nameLen));
                    q += nameLen;
                }

                return null;
            }

            p += extLen;
        }

        return null;
    }

    private static bool TryReadVarInt(ReadOnlySpan<byte> buf, ref int p, out ulong v)
    {
        v = 0;
        if (p >= buf.Length) return false;
        var b = buf[p];
        int prefix = b >> 6, len = 1 << prefix;
        if (p + len > buf.Length) return false;

        switch (len) {
            case 1:
                v = (ulong)(b & 0x3F);
                p += 1;
                return true;
            case 2:
                v = (ulong)(BinaryPrimitives.ReadUInt16BigEndian(buf.Slice(p, 2)) & 0x3FFF);
                p += 2;
                return true;
            case 4:
                v = BinaryPrimitives.ReadUInt32BigEndian(buf.Slice(p, 4)) & 0x3FFF_FFFF;
                p += 4;
                return true;
            case 8:
                v = BinaryPrimitives.ReadUInt64BigEndian(buf.Slice(p, 8)) & 0x3FFF_FFFF_FFFF_FFFF;
                p += 8;
                return true;
        }

        return false;
    }

    private static byte[] HkdfExtract(byte[] salt, ReadOnlySpan<byte> ikm)
    {
        using var hmac = new HMACSHA256(salt);
        // Rent buffer for ikm to avoid ToArray allocation
        var ikmArr = ArrayPool<byte>.Shared.Rent(ikm.Length);
        try {
            ikm.CopyTo(ikmArr);
            return hmac.ComputeHash(ikmArr, 0, ikm.Length);
        }
        finally {
            ArrayPool<byte>.Shared.Return(ikmArr);
        }
    }

    private static byte[] HkdfExpand(byte[] prk, ReadOnlySpan<byte> info, int len)
    {
        using var hmac = new HMACSHA256(prk);
        var result = new byte[len];
        var resultOffset = 0;
        
        Span<byte> T = [];
        byte ctr = 1;
        
        // Rent buffer for input construction
        var inputArr = ArrayPool<byte>.Shared.Rent(32 + info.Length + 1); // max T size + info + counter
        try {
            while (resultOffset < len) {
                var inputLen = T.Length + info.Length + 1;
                T.CopyTo(inputArr);
                info.CopyTo(inputArr.AsSpan(T.Length));
                inputArr[T.Length + info.Length] = ctr++;
                
                var hash = hmac.ComputeHash(inputArr, 0, inputLen);
                var toCopy = Math.Min(hash.Length, len - resultOffset);
                Buffer.BlockCopy(hash, 0, result, resultOffset, toCopy);
                resultOffset += toCopy;
                T = hash;
            }
        }
        finally {
            ArrayPool<byte>.Shared.Return(inputArr);
        }

        return result;
    }

    private static byte[] HkdfExpandLabel(byte[] secret, string label, int len)
    {
        var full = "tls13 " + label;
        var lab = Encoding.ASCII.GetBytes(full);
        Span<byte> info = stackalloc byte[2 + 1 + lab.Length + 1];
        BinaryPrimitives.WriteUInt16BigEndian(info, (ushort)len);
        info[2] = (byte)lab.Length;
        lab.CopyTo(info[3..]);
        info[^1] = 0x00; // empty context
        return HkdfExpand(secret, info, len);
    }
}