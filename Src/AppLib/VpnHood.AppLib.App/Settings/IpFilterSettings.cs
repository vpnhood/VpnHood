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

    public string PacketCaptureIpFilterIncludes {
        get => Read("packetcapture_ip_filter_includes");
        set => Write("packetcapture_ip_filter_includes", value);
    }

    public string PacketCaptureIpFilterExcludes {
        get => Read("packetcapture_ip_filter_excludes");
        set => Write("packetcapture_ip_filter_excludes", value);
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