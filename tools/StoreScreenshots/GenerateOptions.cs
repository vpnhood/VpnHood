namespace VpnHood.App.StoreScreenshots;

// A whole run, as the command line describes it.
//
//   VpnHoodStoreScreenshots generate --config <store/screenshots.json> --assets <store folder or ui.zip>
//       [--out <folder>] [--platform ios,android-phone] [--device ipad-13] [--locale fa] [--only 1,3]
//       [--jobs 8] [--capture-only | --frame-only] [--install] [--install-root <folder>]
internal sealed class GenerateOptions
{
    public required string ConfigPath { get; init; }

    // Where the UI reads its pictures, faces and words. Not needed by a run that only puts
    // finished sets in place, which is what a job gathering other machines' work does.
    public string? AssetsPath { get; init; }
    public required string OutputFolder { get; init; }
    public IReadOnlyList<string> Platforms { get; init; } = [];
    public IReadOnlyList<string> Devices { get; init; } = [];
    public IReadOnlyList<string> Locales { get; init; } = [];
    public IReadOnlyList<string> Only { get; init; } = [];
    public int Jobs { get; init; } = 4;
    public bool Capture { get; init; } = true;
    public bool Frame { get; init; } = true;
    public bool Install { get; init; }
    public string? InstallRoot { get; init; }

    public static GenerateOptions Parse(IReadOnlyList<string> args)
    {
        var flags = args.Where(x => x is "--capture-only" or "--frame-only" or "--install").ToHashSet();
        var values = CommandLine.Parse(args.Where(x => !flags.Contains(x)).ToArray());

        var configPath = Path.GetFullPath(CommandLine.Required(values, "config"));
        var storeRoot = Path.GetDirectoryName(Path.GetDirectoryName(configPath))
                        ?? throw new ArgumentException($"--config must sit in a folder of the store's repo: {configPath}");

        var draws = !flags.Contains("--frame-only") || !flags.Contains("--capture-only");
        return new GenerateOptions {
            ConfigPath = configPath,
            AssetsPath = draws || values.ContainsKey("assets")
                ? Path.GetFullPath(CommandLine.Required(values, "assets"))
                : null,
            // beside the store's repo by default, where a working file is not a deliverable
            OutputFolder = Path.GetFullPath(values.TryGetValue("out", out var output)
                ? output
                : Path.Combine(storeRoot, "test-results", "store-screenshots")),
            Platforms = Split(values, "platform"),
            Devices = Split(values, "device"),
            Locales = Split(values, "locale"),
            Only = Split(values, "only"),
            Jobs = values.TryGetValue("jobs", out var jobs) ? Math.Max(1, int.Parse(jobs)) : 4,
            Capture = !flags.Contains("--frame-only"),
            Frame = !flags.Contains("--capture-only"),
            Install = flags.Contains("--install"),
            InstallRoot = values.TryGetValue("install-root", out var root) ? Path.GetFullPath(root) : null
        };
    }

    private static string[] Split(IReadOnlyDictionary<string, string> values, string name)
    {
        return values.TryGetValue(name, out var value)
            ? value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
    }
}
