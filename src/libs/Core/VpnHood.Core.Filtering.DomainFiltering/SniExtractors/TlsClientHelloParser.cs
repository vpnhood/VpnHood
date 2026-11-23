using System.Text;

namespace VpnHood.Core.Filtering.DomainFiltering.SniExtractors;

/// <summary>
/// Reads the server name (SNI) of a TLS ClientHello, for every reader: a TCP stream's and a TCP packet's, which have
/// it in TLS records, and QUIC's, which has the handshake message alone.
/// </summary>
/// <remarks>
/// A reader gives it a whole ClientHello, and it tells one that names no server from one that is not well formed,
/// whose name is not known: the first passes as a connection with no name, the second must not. It refuses nothing a
/// TLS server accepts: each of its checks is one the protocol makes a server do.
/// </remarks>
internal static class TlsClientHelloParser
{
    /// <summary>The length that tells a ClientHello no more bytes can complete.</summary>
    public const int NeverComplete = -1;

    private const int RecordHeaderLength = 5;
    private const int HandshakeHeaderLength = 4;
    private const byte HandshakeRecordType = 0x16;
    private const byte ClientHelloType = 0x01;

    /// <summary>
    /// Gets how many bytes of a stream to have before its name is looked for: all the TLS records that hold its
    /// ClientHello, as far as the bytes read tell, or the bytes there already when they begin no ClientHello.
    /// </summary>
    /// <returns>
    /// The length, which is more than the data's while the ClientHello is not complete; <see cref="NeverComplete"/>
    /// for a ClientHello that no more bytes can complete.
    /// </returns>
    public static int GetRecordsLength(ReadOnlySpan<byte> data)
    {
        if (!IsClientHelloStart(data))
            return data.Length;

        // The records until their fragments hold the whole handshake, whose length its own header gives: the first
        // four bytes of the fragments, which a first record shorter than that leaves to the next
        var position = 0;
        var collected = 0;
        var handshakeHeader = 0;
        var handshakeHeaderCount = 0;
        while (true) {
            if (data.Length < position + RecordHeaderLength)
                return position + RecordHeaderLength;

            // A record of another type inside a ClientHello not yet complete: TLS allows none there, and the name may
            // be in a handshake record after it
            if (data[position] != HandshakeRecordType)
                return NeverComplete;

            var recordLength = (data[position + 3] << 8) | data[position + 4];
            var fragmentStart = position + RecordHeaderLength;
            var recordEnd = fragmentStart + recordLength;
            if (data.Length < recordEnd)
                return recordEnd;

            // the header's first byte is the handshake's type, the other three its length
            for (var i = fragmentStart; i < recordEnd && handshakeHeaderCount < HandshakeHeaderLength; i++) {
                handshakeHeader = (handshakeHeader << 8) | data[i];
                handshakeHeaderCount++;
            }

            collected += recordLength;
            if (handshakeHeaderCount == HandshakeHeaderLength &&
                collected >= HandshakeHeaderLength + (handshakeHeader & 0xFFFFFF))
                return recordEnd;

            position = recordEnd;
        }
    }

    /// <summary>
    /// Reads the server name from the TLS records that hold a whole ClientHello, as
    /// <see cref="GetRecordsLength"/> measured them.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> if the records begin a ClientHello that is not well formed, whose name is not known;
    /// otherwise <see langword="true"/>, with the name, or <see langword="null"/> where the ClientHello names no
    /// server or the bytes begin none.
    /// </returns>
    public static bool TryGetServerNameFromRecords(ReadOnlySpan<byte> records, out string? serverName)
    {
        serverName = null;
        if (!IsClientHelloStart(records))
            return true;

        // one record that holds the whole ClientHello, as nearly every one does: read where it is
        if (records.Length >= RecordHeaderLength + HandshakeHeaderLength) {
            var firstRecordLength = (records[3] << 8) | records[4];
            var messageLength = HandshakeHeaderLength + ((records[6] << 16) | (records[7] << 8) | records[8]);
            if (messageLength <= firstRecordLength && records.Length >= RecordHeaderLength + firstRecordLength)
                return TryGetServerName(records.Slice(RecordHeaderLength, messageLength), out serverName);
        }

        // the handshake's bytes, from the fragments of the handshake records that hold it
        var handshake = new byte[records.Length];
        var handshakeLength = 0;
        var position = 0;
        while (records.Length >= position + RecordHeaderLength && records[position] == HandshakeRecordType) {
            var recordLength = (records[position + 3] << 8) | records[position + 4];
            var fragmentStart = position + RecordHeaderLength;
            var fragment = records.Slice(fragmentStart, Math.Min(recordLength, records.Length - fragmentStart));
            fragment.CopyTo(handshake.AsSpan(handshakeLength));
            handshakeLength += fragment.Length;
            position = fragmentStart + recordLength;
        }

        return TryGetServerName(handshake.AsSpan(0, handshakeLength), out serverName);
    }

    /// <summary>
    /// Reads the server name from a whole ClientHello: the handshake message, its four-byte header first.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> if the bytes are no well-formed ClientHello, whose name is then not known; otherwise
    /// <see langword="true"/>, with the name, or <see langword="null"/> where the ClientHello names no server.
    /// </returns>
    public static bool TryGetServerName(ReadOnlySpan<byte> handshake, out string? serverName)
    {
        serverName = null;

        // the handshake's header: its type, then its length in three bytes
        if (handshake.Length < HandshakeHeaderLength || handshake[0] != ClientHelloType)
            return false;

        var length = (handshake[1] << 16) | (handshake[2] << 8) | handshake[3];
        if (handshake.Length - HandshakeHeaderLength < length)
            return false;

        // the version (2) and the random (32), then the session ID, the cipher suites and the compression methods
        var body = handshake.Slice(HandshakeHeaderLength, length);
        var position = 34;
        if (body.Length < position ||
            !TryReadVector(body, ref position, lengthSize: 1, out _) ||
            !TryReadVector(body, ref position, lengthSize: 2, out _) ||
            !TryReadVector(body, ref position, lengthSize: 1, out _))
            return false;

        // a ClientHello with no extensions ends here, as an old client's does
        if (position == body.Length)
            return true;

        // the extensions, which end the ClientHello
        if (!TryReadVector(body, ref position, lengthSize: 2, out var extensions) || position != body.Length)
            return false;

        position = 0;
        while (position < extensions.Length) {
            if (extensions.Length - position < 2)
                return false;

            var extensionType = (extensions[position] << 8) | extensions[position + 1];
            position += 2;
            if (!TryReadVector(extensions, ref position, lengthSize: 2, out var extension))
                return false;

            if (extensionType == 0x0000) // server_name
                return TryGetHostName(extension, out serverName);
        }

        return true;
    }

    // the first host name of a server_name extension's list
    private static bool TryGetHostName(ReadOnlySpan<byte> extension, out string? hostName)
    {
        hostName = null;
        var position = 0;
        if (!TryReadVector(extension, ref position, lengthSize: 2, out var names) || position != extension.Length)
            return false;

        position = 0;
        while (position < names.Length) {
            var nameType = names[position++];
            if (!TryReadVector(names, ref position, lengthSize: 2, out var name))
                return false;

            if (nameType != 0) // host_name
                continue;

            if (name.Length > 0)
                hostName = Encoding.ASCII.GetString(name);

            return true;
        }

        return true;
    }

    // a vector of TLS: its length, in one or two bytes, then that many bytes
    private static bool TryReadVector(ReadOnlySpan<byte> data, ref int position, int lengthSize,
        out ReadOnlySpan<byte> vector)
    {
        vector = default;
        if (data.Length - position < lengthSize)
            return false;

        var length = lengthSize == 1 ? data[position] : (data[position] << 8) | data[position + 1];
        position += lengthSize;
        if (data.Length - position < length)
            return false;

        vector = data.Slice(position, length);
        position += length;
        return true;
    }

    // Whether the bytes, as far as they go, are the start of a TLS handshake record that holds a ClientHello: type
    // 0x16, version 3.x, then the handshake's type 0x01. Strict, as a reader waits for the rest of what it finds
    private static bool IsClientHelloStart(ReadOnlySpan<byte> data)
    {
        return data.Length > 0 && data[0] == HandshakeRecordType &&
               (data.Length < 2 || data[1] == 0x03) &&
               (data.Length < 3 || data[2] <= 0x04) &&
               (data.Length < 6 || data[5] == ClientHelloType);
    }
}
