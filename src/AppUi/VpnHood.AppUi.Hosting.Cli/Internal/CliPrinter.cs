using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpnHood.AppUi.Hosting.Cli.Internal;

// How the commands write. Two shapes for every answer: lines for a person, and the API's own object
// for a script (--json), which is deliberately not a second format invented here - a script that
// reads it is reading the same fields the UI reads, and gains whatever the app gains.
internal static class CliPrinter
{
    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static Task Json<T>(T value, CancellationToken cancellationToken)
    {
        return Console.Out.WriteLineAsync(
            JsonSerializer.Serialize(value, JsonOptions).AsMemory(), cancellationToken);
    }

    // A label column wide enough for the longest label in a block, so the values line up without
    // anyone counting spaces into a format string.
    public static Task Fields(IReadOnlyList<(string Label, string Value)> fields,
        CancellationToken cancellationToken)
    {
        var width = fields.Count == 0 ? 0 : fields.Max(x => x.Label.Length);
        var text = string.Join(Environment.NewLine,
            fields.Select(x => $"{(x.Label + ":").PadRight(width + 2)}{x.Value}"));

        return Console.Out.WriteLineAsync(text.AsMemory(), cancellationToken);
    }

    public static Task Line(string text, CancellationToken cancellationToken)
    {
        return Console.Out.WriteLineAsync(text.AsMemory(), cancellationToken);
    }

    // Bytes as a person reads them. The app's own UI does this in the browser; a shell has no
    // formatter to borrow.
    public static string Bytes(long value)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = value;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{value} B" : $"{size:0.##} {units[unit]}";
    }
}
