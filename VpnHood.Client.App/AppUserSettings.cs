using System;

namespace VpnHood.Client.App
{
    public class AppUserSettings
    {
        public bool LogToFile { get; set; } = false;
        public bool LogVerbose { get; set; } = true;
        public string CultureName { get; set; } = "en";
        public Guid? DefaultClientProfileId { get; set; }
        public int MaxReconnectCount { get; set; } = 3;
        public int IsDebugMode { get; set; } = 0;
    }
}
