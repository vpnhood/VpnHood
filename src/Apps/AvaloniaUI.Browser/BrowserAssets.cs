using System.Net.Http.Json;

namespace VpnHood.App.AvaloniaUI.Browser;

// The assets the UI draws from - images, flags, fonts, words, documents - fetched from the app's
// web server into the runtime's own file system, so the UI reads them as it does on a device:
// from a folder. The server lists them (assets-manifest.json) and serves them by name from the
// SPA's assets folder; there are a few hundred, so they are fetched a few at a time.
internal static class BrowserAssets
{
    private const string FolderPath = "/assets";
    private const int Parallelism = 6;

    public static async Task<string> Download(HttpClient http, CancellationToken cancellationToken)
    {
        var names = await http.GetFromJsonAsync("assets-manifest.json", BrowserJsonContext.Default.StringArray, cancellationToken)
                    ?? throw new InvalidOperationException("The app's web server returned no assets manifest.");

        using var gate = new SemaphoreSlim(Parallelism);
        await Task.WhenAll(names.Select(async name => {
            await gate.WaitAsync(cancellationToken);
            try {
                var bytes = await http.GetByteArrayAsync("assets/" + name, cancellationToken);
                var path = Path.Combine(FolderPath, name);
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? FolderPath);
                await File.WriteAllBytesAsync(path, bytes, cancellationToken);
            }
            finally {
                gate.Release();
            }
        }));

        return FolderPath;
    }
}
