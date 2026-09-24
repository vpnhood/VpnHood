namespace VpnHood.App.StoreScreenshots;

// The one shape every command here takes: --name value, and nothing else. A bare word or a flag
// without its value is refused by name rather than ignored, because a misspelled option that is
// quietly dropped produces a picture that looks right and is wrong.
internal static class CommandLine
{
    public static Dictionary<string, string> Parse(IReadOnlyList<string> args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Count; i++) {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Count)
                throw new ArgumentException($"Unexpected argument '{args[i]}'. Every option is --name value.");
            values[args[i][2..]] = args[++i];
        }

        return values;
    }

    public static string Required(IReadOnlyDictionary<string, string> values, string name)
    {
        return values.TryGetValue(name, out var value)
            ? value
            : throw new ArgumentException($"--{name} is required.");
    }
}
