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

    public string AppDataPath { get; set; }
    public bool LogToConsole { get; set; }
    public bool LogAnonymous { get; set; } = true;
    public TimeSpan SessionTimeout { get; set; } = new ClientOptions().SessionTimeout;
    public SocketFactory? SocketFactory { get; set; } = null;
}