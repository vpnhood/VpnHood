using System.Text.Json;

namespace VpnHood.App.StoreScreenshots;

// Where a run's files go, per store.
//
//   raw/      the screens as the UI drew them, above the store's scale - an intermediate
//   final/    the deliverable: each screen in its device, at the size the store takes
//   fixture/  what the app was made to answer for each picture, so any one of them can be drawn
//             again by hand with nothing but this tool
//   device/   each device as the framing pass reads it
internal sealed class RunFolders(string root, string platformKey)
{
    public string Raw { get; } = Path.Combine(root, "raw", platformKey);
    public string Final { get; } = Path.Combine(root, "final", platformKey);
    public string Fixture { get; } = Path.Combine(root, "fixture", platformKey);
    private string Devices { get; } = Path.Combine(root, "device", platformKey);

    private readonly Lock _lock = new();

    public string DeviceFile(DeviceSpec device)
    {
        var path = Path.Combine(Devices, $"{device.Label}.json".Replace('"', '-').Replace('/', '-'));
        lock (_lock) {
            if (File.Exists(path))
                return path;
            Directory.CreateDirectory(Devices);
            File.WriteAllText(path, JsonSerializer.Serialize(device, Options));
        }

        return path;
    }

    private static readonly JsonSerializerOptions Options = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
