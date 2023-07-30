using System;
using System.IO;
using VpnHood.Tunneling.Factory;

namespace VpnHood.Client.App;

public class AppOptions
{
    public AppOptions()
    {
        AppDataPath = 
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VpnHood");
    }

    public Uri? UpdateInfoUrl { get; set; }
    public string AppDataPath { get; set; }
    public TimeSpan SessionTimeout { get; set; } = new ClientOptions().SessionTimeout;
    public SocketFactory? SocketFactory { get; set; } = null;
    public TimeSpan UpdateCheckerInterval { get; set; } = TimeSpan.FromHours(1);
    public bool IsLogToConsoleSupported { get; set; }
    public bool LoadCountryIpGroups { get; set; } = true;
}