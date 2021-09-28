using System;
using System.IO;
using VpnHood.Tunneling.Factory;

namespace VpnHood.Client.App
{
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
        public TimeSpan Timeout { get; set; } = new ClientOptions().Timeout;
        public SocketFactory? SocketFactory { get; set; } = null;
    }
}