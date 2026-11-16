using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Server.ProbeResistance;

// Kestrel's HTTP/1 chunk framing, including when an incomplete invalid delimiter is judged. Work on
// spans rather than awaiting each byte: a large extension or many tiny chunks must fit the drain deadline.
internal sealed class HttpChunkedBody(long maxBodySize)
{
    private Mode _mode;
    private int _size;
    private int _digits;
    private long _bodySize;

    public async Task<bool> Drain(HttpReadStream stream, HttpRequestHead request, CancellationToken cancellationToken)
    {
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            var result = ReadAvailable(stream, cancellationToken);
            switch (result) {
                case Result.Complete:
                    return true;
                case Result.TooLarge:
                    return false;
                case Result.Trailers:
                    // Trailer fields have the header budgets, not the chunk framing budget.
                    await HttpRequestHeadReader.ReadTrailers(stream, request, cancellationToken).Vhc();
                    return true;
                case Result.More:
                    if (!await stream.EnsureBuffered(stream.BufferedSpan.Length + 1, cancellationToken).Vhc())
                        throw new EndOfStreamException();
                    break;
            }
        }
    }

    private Result ReadAvailable(HttpReadStream stream, CancellationToken cancellationToken)
    {
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = stream.BufferedSpan;
            switch (_mode) {
                case Mode.Prefix:
                    // A semicolon already completes the prefix. Other bytes need one byte of lookahead,
                    // including the first invalid digit and a ninth digit; Kestrel waits if it is absent.
                    if (_digits > 0 && bytes.Length > 0 && bytes[0] == ';') {
                        stream.Consume(1);
                        if (!Observe(_digits + 1)) return Result.TooLarge;
                        _mode = Mode.Extension;
                        break;
                    }
                    if (bytes.Length < 2) return Result.More;
                    if (_digits > 0 && bytes[0] == '\r' && bytes[1] == '\n') {
                        stream.Consume(2);
                        if (!Observe(_digits + 2)) return Result.TooLarge;
                        _mode = _size == 0 ? Mode.Trailer : Mode.Data;
                        break;
                    }
                    var digit = HexDigit(bytes[0]);
                    if (digit < 0 || _digits == 8 || _size > (int.MaxValue - digit) / 16)
                        throw new InvalidDataException("Invalid chunk size.");
                    _size = _size * 16 + digit;
                    _digits++;
                    stream.Consume(1);
                    break;

                case Mode.Extension:
                    // There is no separate extension-line limit. Its bytes still count against the body
                    // limit, including extensions that never end, so discard bounded spans as they arrive.
                    var end = bytes.IndexOfAny((byte)'\r', (byte)'\n');
                    var count = end < 0 ? bytes.Length : end;
                    stream.Consume(count);
                    if (!Observe(count)) return Result.TooLarge;
                    bytes = stream.BufferedSpan;
                    if (bytes.Length < 2) return Result.More;
                    if (bytes[0] != '\r' || bytes[1] != '\n')
                        throw new InvalidDataException("A chunk extension must end in CRLF.");
                    stream.Consume(2);
                    if (!Observe(2)) return Result.TooLarge;
                    _mode = _size == 0 ? Mode.Trailer : Mode.Data;
                    break;

                case Mode.Data:
                    if (bytes.IsEmpty) return Result.More;
                    var read = Math.Min(bytes.Length, _size);
                    stream.Consume(read);
                    if (!Observe(read)) return Result.TooLarge;
                    _size -= read;
                    if (_size == 0) _mode = Mode.Suffix;
                    break;

                case Mode.Suffix:
                    if (bytes.Length < 2) return Result.More;
                    if (bytes[0] != '\r' || bytes[1] != '\n')
                        throw new InvalidDataException("Chunk data must end in CRLF.");
                    stream.Consume(2);
                    if (!Observe(2)) return Result.TooLarge;
                    _digits = 0;
                    _mode = Mode.Prefix;
                    break;

                case Mode.Trailer:
                    if (bytes.Length < 2) return Result.More;
                    if (bytes[0] != '\r' || bytes[1] != '\n') return Result.Trailers;
                    stream.Consume(2);
                    return Observe(2) ? Result.Complete : Result.TooLarge;

                default:
                    throw new InvalidOperationException("Unknown chunk parsing state.");
            }
        }
    }

    private bool Observe(int count)
    {
        // Count received framing and data, not declared sizes. A large declaration alone still waits for
        // data; a large extension or many tiny chunks can reach the limit before their payload does.
        _bodySize += count;
        return _bodySize <= maxBodySize;
    }

    private static int HexDigit(byte value) => value switch {
        >= (byte)'0' and <= (byte)'9' => value - '0',
        >= (byte)'a' and <= (byte)'f' => value - 'a' + 10,
        >= (byte)'A' and <= (byte)'F' => value - 'A' + 10,
        _ => -1
    };

    private enum Mode { Prefix, Extension, Data, Suffix, Trailer }
    private enum Result { More, Complete, Trailers, TooLarge }
}
