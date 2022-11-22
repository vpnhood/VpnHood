using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace VpnHood.App.Launcher.Updating;

public class UpdaterOptions2
{
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromHours(12);
    public string PublishInfoFilePath { get; set; }
    public string UpdateFolderPath { get; set; }

    public UpdaterOptions2(string publishInfoFilePath, string updateFolderPath)
    {
        PublishInfoFilePath = publishInfoFilePath;
        UpdateFolderPath = updateFolderPath;
    }
}