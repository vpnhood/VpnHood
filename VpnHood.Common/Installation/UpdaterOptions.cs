using System;

namespace VpnHood.Common.Installation;

public class UpdaterOptions
{
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromHours(12);
    public string PublishInfoFilePath { get; set; }
    public string UpdateFolderPath { get; set; }

    public UpdaterOptions(string publishInfoFilePath, string updateFolderPath)
    {
        PublishInfoFilePath = publishInfoFilePath;
        UpdateFolderPath = updateFolderPath;
    }
}