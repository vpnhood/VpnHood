using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Net.Toolkit.Assets;

// What a zip's contents are called: a stamp in front of the zip - "VHSTAMP:", 16 hex digits, "\n" -
// the first 16 hex digits of the SHA-256 of the whole file, taken while the digits were still zero,
// so the stamp can sit in what it hashes. The tool that packs the zip writes it (StampedZipArchive,
// the UI store's _zip-assets.ps1, the page's _publish.ps1), the zip right after it in the same file:
// a zip is read from its end, by offsets that then count the stamp, so every zip tool still opens it.
// Reading it reads those 25 bytes and closes the stream - an Android asset's forward-only stream
// included, which a ZipArchive would first copy whole into memory. It names a zip's unpacked copy
// (ZipAssetProvider) and the split db built from the IP-location zip.
//
// A zip without one, such as a fork's packed by its own tool, is read whole and hashed instead, with
// a warning: a slower start, rather than a fork that must pack the way we do.
public static class ZipStamp
{
    private const int DigitCount = 16;
    private static readonly byte[] Magic = "VHSTAMP:"u8.ToArray();
    private static readonly SearchValues<byte> HexDigits = SearchValues.Create("0123456789abcdefABCDEF"u8);
    private static int Length => Magic.Length + DigitCount + 1; // the digits, then "\n"

    public static async Task<string> ReadAsync(IAsset zip, CancellationToken cancellationToken)
    {
        await using var stream = await zip.OpenReadAsync(cancellationToken).Vhc();
        return await ReadAsync(stream, zip.ToString(), cancellationToken).Vhc();
    }

    // From the stream's position on; zipName is for the warning alone.
    public static async Task<string> ReadAsync(Stream stream, string? zipName, CancellationToken cancellationToken)
    {
        var head = new byte[Length];
        var read = await stream.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, cancellationToken).Vhc();
        if (read == head.Length && IsStamp(head))
            return Encoding.ASCII.GetString(head, Magic.Length, DigitCount);

        VhLogger.Instance.LogWarning(
            "A zip has no stamp in front of it, so it is read whole to hash it. " +
            "The tool that packs it can write one (ZipStamp). Zip: {Zip}", zipName);
        return await HashAsync(head.AsMemory(0, read), stream, cancellationToken).Vhc();
    }

    // The stamp's space, written before the zip: its digits stay zero until the file is hashed.
    internal static void WriteSpace(Stream stream)
    {
        var space = new byte[Length];
        Magic.CopyTo(space, 0);
        space[^1] = (byte)'\n';
        stream.Write(space);
    }

    // Hashes the whole file with the digits zero and writes them in; zeroed first, so writing it
    // again gives the same digits.
    internal static void Write(Stream stream)
    {
        stream.Position = Magic.Length;
        stream.Write(new byte[DigitCount]);
        stream.Position = 0;
        var hash = SHA256.HashData(stream);
        stream.Position = Magic.Length;
        stream.Write(Encoding.ASCII.GetBytes(ToDigits(hash)));
    }

    internal static async Task WriteAsync(Stream stream, CancellationToken cancellationToken)
    {
        stream.Position = Magic.Length;
        await stream.WriteAsync(new byte[DigitCount], cancellationToken).Vhc();
        stream.Position = 0;
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).Vhc();
        stream.Position = Magic.Length;
        await stream.WriteAsync(Encoding.ASCII.GetBytes(ToDigits(hash)), cancellationToken).Vhc();
    }

    private static bool IsStamp(ReadOnlySpan<byte> head)
    {
        return head.StartsWith(Magic) &&
               !head.Slice(Magic.Length, DigitCount).ContainsAnyExcept(HexDigits) &&
               head[^1] == (byte)'\n';
    }

    // the bytes already read, then the rest of the stream
    private static async Task<string> HashAsync(ReadOnlyMemory<byte> read, Stream rest,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(read.Span);
        var buffer = new byte[81920];
        while (true) {
            var count = await rest.ReadAsync(buffer, cancellationToken).Vhc();
            if (count == 0)
                return ToDigits(hash.GetHashAndReset());

            hash.AppendData(buffer, 0, count);
        }
    }

    private static string ToDigits(ReadOnlySpan<byte> hash)
    {
        return Convert.ToHexStringLower(hash[..(DigitCount / 2)]);
    }
}
