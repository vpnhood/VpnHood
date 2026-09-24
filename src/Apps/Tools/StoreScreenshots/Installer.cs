using System.Text.RegularExpressions;

namespace VpnHood.App.StoreScreenshots;

// Finished sets into the store trees, and stale files out of them.
//
// A store folder is a deliverable, never a partial: a missing file fails the install rather than
// leaving a hole for the uploader to ship, and a numbered file the set no longer produces is
// deleted - a set that shrank would otherwise keep shipping the screen it dropped.
internal static partial class Installer
{
    public static void Install(StoreConfig config, PlatformSpec platform, IReadOnlyList<DeviceSpec> devices,
        string finalDir, string installRoot)
    {
        if (!platform.InstallDir.Contains("<locale>", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"{platform.Label}: installDir \"{platform.InstallDir}\" has no <locale> - every store keeps one folder per language.");

        // the leading slice where a store caps a set, and a word about what is being left out
        var shots = platform.InstallMax is { } max && platform.Shots.Count > max
            ? platform.Shots.Take(max).ToArray()
            : platform.Shots;
        var capped = platform.Shots.Skip(shots.Count).ToArray();
        if (capped.Length > 0)
            Console.WriteLine($"capped    {platform.Label}: installing 1-{shots.Count}; left out " +
                              string.Join(", ", capped.Select(x => $"{x.Number} {x.Label}")));

        foreach (var locale in config.Locales) {
            var folder = locale.FolderFor(platform.Store);
            if (folder == null) {
                Console.WriteLine($"skipped   {platform.Label} {locale.Tag}: this store has no such language");
                continue;
            }

            var destination = Path.GetFullPath(Path.Combine(installRoot, platform.InstallDir.Replace("<locale>", folder)));
            Directory.CreateDirectory(destination);
            var copied = 0;

            foreach (var device in devices) {
                foreach (var shot in shots) {
                    var from = Path.Combine(finalDir, Names.File(device, shot, locale));
                    var to = Path.Combine(destination, Names.Installed(device, shot));
                    if (!File.Exists(from))
                        throw new InvalidOperationException(
                            $"{platform.Label}: {Names.File(device, shot, locale)} is not in {finalDir} - " +
                            "generate the whole set (no --only, no --locale) before installing.");
                    File.Copy(from, to, overwrite: true);
                    copied++;
                }

                // the pattern matches the retired locale-suffixed names too, so a folder written by
                // an older tool cleans itself up here
                Prune(destination, device, shots.Select(shot => Names.Installed(device, shot)).ToHashSet(),
                    new Regex($"^{Regex.Escape(device.Prefix)}\\d+(_[A-Za-z][\\w-]*)?\\.png$"));
            }

            Console.WriteLine($"installed {copied} file(s) -> {Path.GetRelativePath(installRoot, destination)}");
        }
    }

    public static void Prune(string folder, DeviceSpec device, IReadOnlySet<string> expected, Regex pattern)
    {
        foreach (var path in Directory.GetFiles(folder)) {
            var name = Path.GetFileName(path);
            if (!pattern.IsMatch(name) || expected.Contains(name))
                continue;
            File.Delete(path);
            Console.WriteLine($"pruned    {device.Label,-11} {name}  (no longer in the set)");
        }
    }
}
