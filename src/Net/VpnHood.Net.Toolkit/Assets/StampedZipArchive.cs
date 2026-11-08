using System.IO.Compression;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.Toolkit.Assets;

/// <summary>
/// Provides a new zip with its stamp in front (<see cref="ZipStamp"/>): the stamp's space goes first, the zip after it,
/// and closing the archive fills the stamp in, so what writes the zip cannot leave it out.
/// </summary>
public sealed class StampedZipArchive : ZipArchive
{
    private readonly Stream _stream;

    /// <summary>
    /// Initializes a new instance of the <see cref="StampedZipArchive"/> class, which writes a zip into a stream after
    /// its stamp's space.
    /// </summary>
    /// <param name="stream">
    /// The stream, at its start, which can seek, read and write, such as a new file's; it stays open after the archive.
    /// </param>
    /// <exception cref="ArgumentException">The stream cannot seek, read or write, or is not at its start.</exception>
    public StampedZipArchive(Stream stream)
        : base(WithStampSpace(stream), ZipArchiveMode.Create, leaveOpen: true)
    {
        _stream = stream;
    }

    /// <inheritdoc/>
    /// <remarks>After the zip is written, it hashes the stream and writes the stamp.</remarks>
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            ZipStamp.Write(_stream);
    }

    /// <inheritdoc/>
    /// <remarks>After the zip is written, it hashes the stream and writes the stamp.</remarks>
    protected override async ValueTask DisposeAsyncCore()
    {
        await base.DisposeAsyncCore().Vhc();
        await ZipStamp.WriteAsync(_stream, CancellationToken.None).Vhc();
    }

    // before the archive takes the stream: the zip's offsets then count the stamp
    private static Stream WithStampSpace(Stream stream)
    {
        if (!stream.CanSeek || !stream.CanRead || !stream.CanWrite || stream.Position != 0)
            throw new ArgumentException(
                "A stamped zip is written from the start of a stream it can read back, a new file's.",
                nameof(stream));

        ZipStamp.WriteSpace(stream);
        return stream;
    }
}
