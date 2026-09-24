namespace VpnHood.App.StoreScreenshots;

// One shot, as the command line describes it. The vocabulary is the web UI engine's (e2e/store/
// project.mjs): a route names the page, the size is the device's viewport in logical pixels, and
// the scale is the device's pixel ratio times the engine's supersampling - so a raw PNG from here
// drops into the same raw/ folder and the same frame pass.
internal sealed class ShotOptions
{
    public required string FixturePath { get; init; }
    public required string AssetsPath { get; init; }
    public required string Route { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required double Scale { get; init; }
    public required string OutputPath { get; init; }
    public string? Culture { get; init; }
    // Controls to hide before the capture, each a path of x:Names from the page down ("EnabledItem/Chip").
    public IReadOnlyList<string> Hide { get; init; } = [];

    // Style classes taken off every control before the capture: the ones whose whole purpose is an
    // animation that never ends (Classic.Avalonia/Styles/AppTheme.axaml). A capture would otherwise
    // catch the animation wherever it happened to be - the same shot different every run - and the
    // page would never come to rest for the settle wait. Without the class the control keeps the
    // value the animation starts from. This is the toolkit's reducedMotion: 'reduce'; Avalonia 12
    // keeps its animation clock internal, so there is nothing to pause.
    public IReadOnlyList<string> Freeze { get; init; } = ["flasher"];
    // How long a page may keep changing before the capture is taken anyway.
    public TimeSpan SettleTimeout { get; init; } = TimeSpan.FromSeconds(8);

    public static ShotOptions Parse(string[] args)
    {
        var values = CommandLine.Parse(args);
        string Required(string name) => CommandLine.Required(values, name);

        return new ShotOptions {
            FixturePath = Path.GetFullPath(Required("fixture")),
            AssetsPath = Path.GetFullPath(Required("assets")),
            Route = Required("route"),
            Width = int.Parse(Required("width")),
            Height = int.Parse(Required("height")),
            Scale = double.Parse(Required("scale"), System.Globalization.CultureInfo.InvariantCulture),
            OutputPath = Path.GetFullPath(Required("out")),
            Culture = values.GetValueOrDefault("culture"),
            Hide = values.TryGetValue("hide", out var hide) ? hide.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [],
            Freeze = values.TryGetValue("freeze", out var freeze)
                ? freeze.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : ["flasher"],
            SettleTimeout = values.TryGetValue("settle-timeout", out var settle) ? TimeSpan.FromMilliseconds(int.Parse(settle)) : TimeSpan.FromSeconds(8)
        };
    }
}
