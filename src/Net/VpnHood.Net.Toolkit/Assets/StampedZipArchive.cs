using System.IO.Compression;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.Toolkit.Assets;

// A new zip with its stamp in front (ZipStamp): the stamp's space goes first, the zip after it, and
// closing the archive fills the stamp in, so what writes the zip cannot leave it out. The stream is a
// new file's, from its start, read back to hash it; it stays open.
public sealed class StampedZipArchive : ZipArchive
{
    private readonly Stream _stream;

    public StampedZipArchive(Stream stream)
        : base(WithStampSpace(stream), ZipArchiveMode.Create, leaveOpen: true)
    {
        _stream = stream;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            ZipStamp.Write(_stream);
    }

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
