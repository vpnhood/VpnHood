using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace VpnHood.App.StoreScreenshots;

// A whole set, or any slice of one: every language, every device, every screen a store shows.
//
// Two passes over each picture - the screen drawn by the UI, then the device drawn around it - and
// the working files of both kept, because the first is what a wrong pixel is chased in. Each
// picture is a process of its own (Child), several at a time; nothing here depends on the order
// they finish in, so a run of eight at once writes the same bytes as a run of one.
internal static partial class GenerateCommand
{
    public static int Run(IReadOnlyList<string> args)
    {
        GenerateOptions options;
        try {
            options = GenerateOptions.Parse(args);
        }
        catch (ArgumentException ex) {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }

        try {
            RunAsync(options, CancellationToken.None).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception ex) {
            Console.Error.WriteLine($"FAILED    {ex.Message}");
            return 1;
        }
    }

    private static async Task RunAsync(GenerateOptions options, CancellationToken cancellationToken)
    {
        var config = StoreConfig.Load(options.ConfigPath);
        var fixture = JsonNode.Parse(await File.ReadAllTextAsync(config.FixturePath, cancellationToken)) as JsonObject
                      ?? throw new InvalidOperationException($"{config.FixturePath} is not a JSON object.");

        var locales = Select(config.Locales, options.Locales, x => x.Tag, "language");
        var platforms = Select(config.Platforms, options.Platforms, "store");
        var assets = options.AssetsPath;
        var installedApps = options.Capture && assets != null
            ? await AppTiles.DrawAsync(config.InstalledApps, Store.Open(assets), cancellationToken)
            : null;

        if (assets != null)
            Console.WriteLine($"source    {Path.GetFileName(config.FixturePath)}, drawn by the Avalonia UI with {assets}");

        foreach (var (key, platform) in platforms) {
            // a device filter names devices of one platform; the others simply have none selected
            var devices = options.Devices.Count == 0
                ? platform.Devices.ToArray()
                : platform.Devices.Where(x => options.Devices.Contains(x.Key)).ToArray();
            if (devices.Length == 0)
                continue;

            var shots = options.Only.Count == 0
                ? platform.Shots
                : platform.Shots.Where(x => options.Only.Contains(x.Number)).ToArray();
            Console.WriteLine($"\n=== {platform.Label} ({key})");

            var folders = new RunFolders(options.OutputFolder, key);
            var jobs =
                from device in devices
                from locale in locales
                from shot in shots
                select new ShotJob(platform, device.Value, locale, shot, folders);

            await Parallel.ForEachAsync(jobs,
                new ParallelOptions { MaxDegreeOfParallelism = options.Jobs, CancellationToken = cancellationToken },
                async (job, token) => await DrawAsync(job, config, fixture, options, installedApps, token));

            foreach (var device in devices.Select(x => x.Value))
                PruneWorking(folders, config, platform, device, options);

            // installing with neither pass is a run of its own: a job that gathered the finished
            // sets of every language from other machines and only has to put them in place
            if (options.Install)
                Installer.Install(config, platform, devices.Select(x => x.Value).ToArray(), folders.Final,
                    options.InstallRoot ?? Path.GetFullPath(Path.Combine(config.Folder, config.InstallRoot)));
        }

        FixtureDrift.Report(config.FixturePath);
    }

    private static async Task DrawAsync(ShotJob job, StoreConfig config, JsonObject fixture, GenerateOptions options,
        JsonArray? installedApps, CancellationToken cancellationToken)
    {
        var name = Names.File(job.Device, job.Shot, job.Locale);
        var raw = Path.Combine(job.Folders.Raw, name);

        if (options.Capture) {
            if (job.Shot.Source != null) {
                // a picture that is not a screen: the frame pass fits it to each device itself
                Directory.CreateDirectory(job.Folders.Raw);
                File.Copy(Path.Combine(config.Folder, job.Shot.Source), raw, overwrite: true);
            }
            else {
                var composed = FixtureComposer.Compose(fixture, PatchFile(config, job.Platform), job.Platform.Patch,
                    job.Shot.Patch, installedApps);
                var fixturePath = Path.Combine(job.Folders.Fixture, Names.Fixture(job.Device, job.Shot, job.Locale));
                Directory.CreateDirectory(job.Folders.Fixture);
                await File.WriteAllTextAsync(fixturePath, composed.ToJsonString(Compact), cancellationToken);

                var arguments = new List<string> {
                    "--fixture", fixturePath,
                    "--assets", Assets(options),
                    "--route", job.Shot.Route,
                    "--width", job.Device.CssWidth.ToString(),
                    "--height", (job.Device.CssHeight - job.Device.SafeTop - job.Device.SafeBottom).ToString(),
                    "--scale", (job.Device.Scale * CaptureSupersample).ToString("0.###"),
                    "--culture", job.Locale.Culture,
                    "--out", raw
                };
                if (job.Shot.Hide.Count > 0)
                    arguments.AddRange(["--hide", string.Join(',', job.Shot.Hide)]);

                Relay(await Child.RunAsync(arguments, cancellationToken), job, name);
            }

            Console.WriteLine($"captured  {job.Device.Label,-11} {name}  {job.Shot.Label}");
        }

        if (!options.Frame)
            return;

        var devicePath = job.Folders.DeviceFile(job.Device);
        Relay(await Child.RunAsync([
            "frame", "--device", devicePath, "--assets", Assets(options), "--in", raw,
            "--out", Path.Combine(job.Folders.Final, name)
        ], cancellationToken), job, name);
        Console.WriteLine($"framed    {job.Device.Label,-11} {name}  {job.Shot.Label}");
    }

    // The store to draw with, which a drawing run must have been given.
    private static string Assets(GenerateOptions options)
    {
        return options.AssetsPath ?? throw new ArgumentException("--assets is required to draw; a run that only installs does not need it.");
    }

    // A platform's long patch, read once and kept: every picture of that store merges the same one.
    private static JsonObject? PatchFile(StoreConfig config, PlatformSpec platform)
    {
        if (platform.PatchFile == null)
            return null;

        return PatchFiles.GetOrAdd(platform.PatchFile, name => {
            var path = Path.Combine(config.Folder, name);
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                   ?? throw new InvalidOperationException($"{path} is not a JSON object.");
        });
    }

    private static readonly ConcurrentDictionary<string, JsonObject> PatchFiles = new();

    // What a child said about this picture, under this run's own naming: its own "captured" and
    // "framed" lines are the caller's to write, and a fixture behind the contract is said once.
    private static void Relay(string output, ShotJob job, string name)
    {
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries)) {
            var text = line.TrimEnd();
            if (text.StartsWith("captured", StringComparison.Ordinal) || text.StartsWith("framed", StringComparison.Ordinal)
                || text.StartsWith("opened", StringComparison.Ordinal) || text.StartsWith("hidden", StringComparison.Ordinal))
                continue;
            if (text.StartsWith("filled", StringComparison.Ordinal) || text.Contains("behind the API contract"))
                FixtureDrift.Note(text);
            else
                Console.WriteLine($"{text[..Math.Min(9, text.Length)].TrimEnd(),-9} {job.Device.Label,-11} {name}  {text[Math.Min(10, text.Length)..]}");
        }
    }

    // Numbered files a folder should no longer hold, against the FULL set rather than the slice
    // this run drew - a run of one language must not delete the others.
    private static void PruneWorking(RunFolders folders, StoreConfig config, PlatformSpec platform, DeviceSpec device,
        GenerateOptions options)
    {
        var pattern = new Regex($"^{Regex.Escape(device.Prefix)}\\d+_[A-Za-z][\\w-]*\\.png$");
        var expected = (from shot in platform.Shots
            from locale in config.Locales
            select Names.File(device, shot, locale)).ToHashSet();

        if (options.Capture)
            Installer.Prune(folders.Raw, device, expected, pattern);
        if (options.Frame && Directory.Exists(folders.Final))
            Installer.Prune(folders.Final, device, expected, pattern);
    }

    // A name the configuration does not have is refused with the names it does have, rather than
    // quietly drawing nothing: a misspelled language would otherwise look like a finished run.
    private static IReadOnlyList<KeyValuePair<string, T>> Select<T>(IReadOnlyDictionary<string, T> all,
        IReadOnlyList<string> wanted, string what)
    {
        return Select(all.ToArray(), wanted, x => x.Key, what);
    }

    private static IReadOnlyList<T> Select<T>(IReadOnlyList<T> all, IReadOnlyList<string> wanted, Func<T, string> key,
        string what)
    {
        foreach (var name in wanted.Where(name => all.All(x => key(x) != name)))
            throw new ArgumentException($"There is no {what} called \"{name}\". There is: {string.Join(", ", all.Select(key))}.");

        return wanted.Count == 0 ? all : all.Where(x => wanted.Contains(key(x))).ToArray();
    }

    // Above the store's own scale, so the reduction in the frame pass has something to work with.
    private const int CaptureSupersample = 2;

    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };
}
