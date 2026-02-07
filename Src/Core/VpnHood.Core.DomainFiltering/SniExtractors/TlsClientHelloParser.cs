using System.Text;

namespace VpnHood.Core.SniFiltering.SniExtractors;

/// <summary>
/// Shared TLS ClientHello parser for extracting SNI from TLS handshake data.
/// Used by both TCP (TLS) and QUIC (which uses TLS 1.3 internally) SNI extractors.
/// </summary>
public static class TlsClientHelloParser
{
    /// <summary>
    /// Extracts SNI from a TLS ClientHello message.
    /// </summary>
    /// <param name="data">The TLS record data (with or without record header).</param>
    /// <param name="hasRecordHeader">True if data includes TLS record header (TCP), false for raw handshake (QUIC).</param>
    /// <returns>The SNI domain name if found, null otherwise.</returns>
    public static string? ExtractSni(ReadOnlySpan<byte> data, bool hasRecordHeader = true)
    {
        if (data.Length == 0)
            return null;

        var p = 0;

        if (hasRecordHeader) {
            // TLS Record Layer: ContentType (1) + Version (2) + Length (2)
            if (data.Length < 5)
                return null;

            // Check if it's a TLS Handshake record (0x16)
            if (data[0] != 0x16)
                return null;

            // Skip record header to get to handshake message
            p = 5;
        }

        // Handshake message: Type (1) + Length (3)
        if (p + 4 > data.Length)
            return null;

        // Check if it's a ClientHello (0x01)
        if (data[p] != 0x01)
            return null;

        // Read handshake message length
        var handshakeLen = (data[p + 1] << 16) | (data[p + 2] << 8) | data[p + 3];
        p += 4;

        // Calculate handshake end position
        var handshakeEnd = p + handshakeLen;
        if (handshakeEnd > data.Length)
            handshakeEnd = data.Length; // Partial data, try to parse what we have

        // ClientHello body:
        // - Version (2)
        // - Random (32)
        // - Session ID length (1) + Session ID (variable)
        // - Cipher Suites length (2) + Cipher Suites (variable)
        // - Compression Methods length (1) + Compression Methods (variable)
        // - Extensions length (2) + Extensions (variable)

        // Skip Version (2) + Random (32) = 34 bytes
        p += 34;
        if (p >= handshakeEnd)
            return null;

        // Skip Session ID
        if (p + 1 > handshakeEnd)
            return null;
        var sessionIdLen = data[p++];
        p += sessionIdLen;
        if (p > handshakeEnd)
            return null;

        // Skip Cipher Suites
        if (p + 2 > handshakeEnd)
            return null;
        var cipherSuitesLen = (data[p] << 8) | data[p + 1];
        p += 2 + cipherSuitesLen;
        if (p > handshakeEnd)
            return null;

        // Skip Compression Methods
        if (p + 1 > handshakeEnd)
            return null;
        var compressionLen = data[p++];
        p += compressionLen;
        if (p > handshakeEnd)
            return null;

        // Extensions
        if (p + 2 > handshakeEnd)
            return null;
        var extensionsLen = (data[p] << 8) | data[p + 1];
        p += 2;

        var extensionsEnd = p + extensionsLen;
        if (extensionsEnd > handshakeEnd)
            extensionsEnd = handshakeEnd;

        // Parse extensions to find SNI (type 0x0000)
        while (p + 4 <= extensionsEnd) {
            var extType = (data[p] << 8) | data[p + 1];
            var extLen = (data[p + 2] << 8) | data[p + 3];
            p += 4;

            if (p + extLen > extensionsEnd)
                return null;

            if (extType == 0x0000) // Server Name Indication
                return ParseSniExtension(data.Slice(p, extLen));

            p += extLen;
        }

        return null;
    }

    private static string? ParseSniExtension(ReadOnlySpan<byte> extData)
    {
        if (extData.Length < 2)
            return null;

        // Server Name List length
        var listLen = (extData[0] << 8) | extData[1];
        var p = 2;

        if (p + listLen > extData.Length)
            listLen = extData.Length - p;

        var listEnd = p + listLen;

        while (p + 3 <= listEnd) {
            var nameType = extData[p++];
            var nameLen = (extData[p] << 8) | extData[p + 1];
            p += 2;

            if (p + nameLen > listEnd)
                return null;

            if (nameType == 0) // host_name
                return Encoding.ASCII.GetString(extData.Slice(p, nameLen));

            p += nameLen;
        }

        return null;
    }
}
