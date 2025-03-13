namespace VpnHood.AppLib.Settings;

public class IpFilterSettings(string folderPath)
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

    public string AppIpFilterIncludes {
        get => Read("app_ip_filter_includes");
        set => Write("app_ip_filter_includes", value);
    }

    public string AppIpFilterExcludes {
        get => Read("app_ip_filter_excludes");
        set => Write("app_ip_filter_excludes", value);
    }

    public string AdapterIpFilterIncludes {
        get {
            // legacy: deprecated in 5.1.654 or upper
            if (File.Exists("packetcapture_ip_filter_includes")) {
                Write("adapter_ip_filter_includes", Read("packetcapture_ip_filter_includes"));
                File.Delete("packetcapture_ip_filter_includes");
            }

            return Read("adapter_ip_filter_includes");
        }
        set => Write("adapter_ip_filter_includes", value);
    }

    public string AdapterIpFilterExcludes {
        get {
            // legacy: deprecated in 5.1.654 or upper
            if (File.Exists("packetcapture_ip_filter_excludes")) {
                Write("adapter_ip_filter_excludes", Read("packetcapture_ip_filter_excludes"));
                File.Delete("packetcapture_ip_filter_excludes");
            }

            return Read("adapter_ip_filter_excludes");
        }
        set => Write("adapter_ip_filter_excludes", value);
    }

    private string Read(string fileTitle)
    {
        return File.ReadAllText(GetFilePath(fileTitle));
    }

    private void Write(string fileTitle, string content)
    {
        IpFilterParser.Parse(content); // validate
        File.WriteAllText(GetFilePath(fileTitle), content);
    }
}