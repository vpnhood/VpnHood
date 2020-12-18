using System;
using System.IO;
using System.Reflection;

namespace VpnHood.Common
{
    public class AppUpdaterOptions
    {
        public string LauncherFilePath { get; set; }
        public string UpdatesFolder { get; set; }
        public Uri UpdateUri { get; set; }
        public int CheckIntervalMinutes { get; set; } = 1 * (24 * 60);
    }
}
