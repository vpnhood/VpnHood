using System.Diagnostics;
using System.Text;

namespace VpnHood.App.StoreScreenshots;

// This tool running itself, one shot or one frame at a time.
//
// A process per picture, deliberately: the UI's state is static - a page that changed a setting
// would otherwise leak into the next shot - and separate processes are also how several pictures
// are drawn at once. What the child says is relayed under the caller's own naming, and a child
// that fails takes the run down with its reason rather than leaving a hole in the set.
internal static class Child
{
    public static async Task<string> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo {
            FileName = Self.FileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in Self.Prefix.Concat(arguments))
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start)
                            ?? throw new InvalidOperationException($"{Self.FileName} did not start.");
        var output = new StringBuilder();
        var error = new StringBuilder();
        var reading = Task.WhenAll(
            ReadAsync(process.StandardOutput, output, cancellationToken),
            ReadAsync(process.StandardError, error, cancellationToken));

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        await reading.ConfigureAwait(false);

        if (process.ExitCode != 0) {
            var reason = error.Length > 0 ? error.ToString() : output.ToString();
            throw new InvalidOperationException(reason.Trim().Replace("FAILED    ", ""));
        }

        return output.ToString();
    }

    private static async Task ReadAsync(StreamReader reader, StringBuilder into, CancellationToken cancellationToken)
    {
        into.Append(await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false));
    }
}
