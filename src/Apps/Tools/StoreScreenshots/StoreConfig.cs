using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpnHood.App.StoreScreenshots;

// Everything an app's store sets are made of, as one file beside its fixture.
//
// THIS is the file a fork edits - the tool itself is never edited to change what is generated. It
// says which languages to draw, which stores to draw for, what each OS build of the app can do,
// which devices show it, which screens, and where a finished set is installed.
internal sealed class StoreConfig
{
    // The checkout every InstallDir resolves inside - the repo that owns the store assets - as a
    // path relative to this configuration's own folder. A caller may name another with --install-root.
    public string InstallRoot { get; init; } = ".";

    public required IReadOnlyList<StoreLocale> Locales { get; init; }
    public required Dictionary<string, PlatformSpec> Platforms { get; init; }

    // The apps the Split Apps screen lists, drawn as tiles (DemoApp).
    public IReadOnlyList<DemoApp> InstalledApps { get; init; } = [];

    // Where this file and the fixture beside it live: every path in it is relative to that.
    public string Folder { get; private set; } = "";

    public string FixturePath => Path.Combine(Folder, "fixture.json");

    public static StoreConfig Load(string path)
    {
        var config = JsonSerializer.Deserialize<StoreConfig>(File.ReadAllText(path), Options)
                     ?? throw new InvalidOperationException($"{path} holds no configuration.");
        config.Folder = Path.GetDirectoryName(Path.GetFullPath(path))
                        ?? throw new InvalidOperationException($"There is no folder in the path {path}.");

        // the number in a file name is where the shot sits in its platform's list, and a shot
        // shared between platforms numbers independently in each
        foreach (var platform in config.Platforms.Values)
            for (var i = 0; i < platform.Shots.Count; i++)
                platform.Shots[i].Number = (i + 1).ToString();

        return config;
    }

    private static JsonSerializerOptions Options { get; } = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
