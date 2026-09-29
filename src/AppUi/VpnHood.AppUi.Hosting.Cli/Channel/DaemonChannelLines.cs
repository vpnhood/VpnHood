using System.Text;
using System.Text.Json;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Cli.Channel;

// The channel's framing: one JSON object per line, each way.
internal static class DaemonChannelLines
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static async Task Write<T>(Stream stream, T line, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(line, Options) + "\n");
        await stream.WriteAsync(bytes, cancellationToken).Vhc();
        await stream.FlushAsync(cancellationToken).Vhc();
    }

    // Null at the end of the stream.
    public static async Task<T?> Read<T>(StreamReader reader, CancellationToken cancellationToken) where T : class
    {
        var text = await reader.ReadLineAsync(cancellationToken).Vhc();
        return text == null ? null : JsonSerializer.Deserialize<T>(text, Options);
    }

    // A line from a caller not yet known, so of at most maxLength bytes. Null at the end of the stream.
    public static async Task<T?> ReadBounded<T>(Stream stream, int maxLength, CancellationToken cancellationToken)
        where T : class
    {
        var line = new byte[maxLength];
        for (var length = 0; length < maxLength; length++) {
            if (await stream.ReadAsync(line.AsMemory(length, 1), cancellationToken).Vhc() == 0)
                return null;

            if (line[length] == (byte)'\n')
                return JsonSerializer.Deserialize<T>(line.AsSpan(0, length), Options);
        }

        throw new InvalidDataException($"The line is longer than {maxLength} bytes.");
    }
}
