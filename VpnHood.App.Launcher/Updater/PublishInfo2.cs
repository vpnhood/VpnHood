using System;

namespace VpnHood.App.Launcher.Updating;

public class PublishInfo2
{
    public Version Version { get; }

    public string? ExeFile { get; set; }

    public Uri UpdateInfoUrl { get; }

    public Uri UpdateScriptUrl { get; }

    public PublishInfo2(Uri updateScriptUrl, Uri updateInfoUrl, Version version)
    {
        UpdateScriptUrl = updateScriptUrl ?? throw new ArgumentNullException(nameof(updateScriptUrl));
        UpdateInfoUrl = updateInfoUrl ?? throw new ArgumentNullException(nameof(updateInfoUrl));
        Version =  version ?? throw new ArgumentNullException(nameof(version));
    }

}