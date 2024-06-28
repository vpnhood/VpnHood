using System.Text;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;

namespace VpnHood.Tunneling.Utils;

public static class SniExtractor
{
    public class SniData
    {
        public required string? Sni { get; init; }
        public required byte[] ReadData { get; init; }
    }

    public static async Task<SniData> ExtractSni(Stream tcpStream, CancellationToken cancellationToken)
    {
        // extract SNI
        var initBuffer = new byte[1000];
        var bufCount = await tcpStream
            .ReadAsync(initBuffer, 0, initBuffer.Length, cancellationToken)
            .VhConfigureAwait();

        return new SniData
        {
            Sni = ExtractSni(initBuffer[..bufCount]),
            ReadData = initBuffer[..bufCount]
        };
    }

    public static string? ExtractSni(byte[] payloadData)
    {
        try
        {
            return GetSniFromStreamInternal(payloadData);
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(GeneralEventId.Tcp, ex, "Could not extract sni");
            return null;
        }
    }

    public static string? GetSniFromStreamInternal(byte[] payloadData)
    {
        if (payloadData.Length == 0)
            return null;

        // Check if it's a TLS ClientHello (0x16 is Handshake, 0x01 is ClientHello)
        if (payloadData[0] != 0x16 || payloadData[5] != 0x01)
            return null;

        var currentPos = 43; // Position of SessionID length
        if (currentPos >= payloadData.Length)
            return null;

        var sessionIdLength = payloadData[currentPos];
        currentPos += 1 + sessionIdLength; // Move past the SessionID

        if (currentPos + 2 > payloadData.Length)
            return null; // Ensure there's enough data for cipher suites length

        var cipherSuitesLength = (payloadData[currentPos] << 8) | payloadData[currentPos + 1];
        currentPos += 2 + cipherSuitesLength; // Move past the Cipher Suites

        if (currentPos + 1 > payloadData.Length)
            return null; // Ensure there's enough data for compression methods length

        var compressionMethodsLength = payloadData[currentPos];
        currentPos += 1 + compressionMethodsLength; // Move past the Compression Methods

        if (currentPos + 2 > payloadData.Length)
            return null; // Extensions start position is out of bounds

        var extensionsLength = (payloadData[currentPos] << 8) | payloadData[currentPos + 1];
        currentPos += 2; // Move past the extensions length

        // Ensure the extensions length does not exceed the remaining payload length
        if (currentPos + extensionsLength > payloadData.Length)
            extensionsLength = payloadData.Length - currentPos; //Extensions length exceeds payload length. Adjusting to payload boundary

        while (currentPos < payloadData.Length && currentPos < extensionsLength + currentPos)
        {
            if (currentPos + 4 > payloadData.Length)
                return null; // Extension header is out of bounds.

            var extensionType = (payloadData[currentPos] << 8) | payloadData[currentPos + 1];
            var extensionLength = (payloadData[currentPos + 2] << 8) | payloadData[currentPos + 3];
            currentPos += 4; // Move past the extension header

            if (currentPos + extensionLength > payloadData.Length)
                return null; // Extension length is out of bounds.

            if (extensionType == 0x0000) // SNI extension type
            {
                if (currentPos + 2 > payloadData.Length)
                    return null; // Server name list length is out of bounds

                var serverNameListLength = (payloadData[currentPos] << 8) | payloadData[currentPos + 1];
                currentPos += 2; // Move past the server name list length

                if (currentPos + serverNameListLength > payloadData.Length)
                    return null; // Server name list length is out of bounds

                // var serverNameType = payloadData[currentPos];
                var serverNameLength = (payloadData[currentPos + 1] << 8) | payloadData[currentPos + 2];
                currentPos += 3; // Move past the server name type and length

                if (currentPos + serverNameLength > payloadData.Length)
                    return null; // Server name length is out of bounds

                var sni = Encoding.ASCII.GetString(payloadData, currentPos, serverNameLength);
                return sni;
            }

            currentPos += extensionLength; // Move to the next extension
        }

        return null;
    }
}