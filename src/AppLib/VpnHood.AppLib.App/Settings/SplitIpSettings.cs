using VpnHood.AppLib.Utils;

namespace VpnHood.AppLib.Settings;

public class SplitIpSettings(string folderPath)
{
    private string GetFilePath(string fileTitle)
    {
        var filePath = Path.Combine(folderPath, fileTitle + ".txt");
        if (File.Exists(filePath))
            return filePath;

        Directory.CreateDirectory(folderPath);
        File.WriteAllText(filePath, string.Empty);
        return filePath;
    }

    public string AppIncludes {
        get {
            // legacy: deprecated in 785 or upper
            if (File.Exists("app_ip_filter_includes")) {
                Write("app_includes", Read("app_ip_filter_includes"));
                File.Delete("app_ip_filter_includes");
            }

            return Read("app_includes");
        }
        set => Write("app_includes", value);
    }

    public string AppExcludes {
        get {
            // legacy: deprecated in 785 or upper
            if (File.Exists("app_ip_filter_excludes")) {
                Write("app_excludes", Read("app_ip_filter_excludes"));
                File.Delete("app_ip_filter_excludes");
            }

            return Read("app_excludes");
        }
        set => Write("app_excludes", value);
    }

    public string DeviceIncludes {
        get {
            // legacy: deprecated in 785 or upper
            if (File.Exists("adapter_ip_filter_includes")) {
                Write("device_includes", Read("adapter_ip_filter_includes"));
                File.Delete("adapter_ip_filter_includes");
            }

            return Read("device_includes");
        }
        set => Write("device_includes", value);
    }

    public string DeviceExcludes {
        get {
            // legacy: deprecated in 785 or upper
            if (File.Exists("adapter_ip_filter_excludes")) {
                Write("device_excludes", Read("adapter_ip_filter_excludes"));
                File.Delete("adapter_ip_filter_excludes");
            }

            return Read("device_excludes");
        }
        set => Write("device_excludes", value);
    }

    public string AppBlocks {
        get => Read("app_blocks");
        set => Write("app_blocks", value);
    }

    // Stat-only change signature of the app split-ip sources, stored as the db's source_signature meta
    // so SplitIpDbBuilder.EnsureAsync detects stale dbs without parsing the text files. Every setting
    // write rewrites its file, so the signature always changes with the content.
    public string GetSplitIpViaAppSignature() => AppUtils.BuildFileSignature(
        Path.Combine(folderPath, "app_includes.txt"),
        Path.Combine(folderPath, "app_excludes.txt"),
        Path.Combine(folderPath, "app_blocks.txt"));

    private string Read(string fileTitle)
    {
        return File.ReadAllText(GetFilePath(fileTitle));
    }

    private void Write(string fileTitle, string content)
    {
        IpRangeTextFileParser.Parse(content); // validate
        File.WriteAllText(GetFilePath(fileTitle), content);
    }
}