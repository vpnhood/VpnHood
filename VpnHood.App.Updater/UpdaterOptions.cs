using System;

namespace VpnHood.App.Updater;

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