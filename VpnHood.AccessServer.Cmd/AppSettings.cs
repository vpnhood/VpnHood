using System;

namespace VpnHood.AccessServer.Cmd
{
    internal class AppSettings
    {
        public Uri ServerUrl { get; set; }
        public string Authorization { get; set; }
        public Guid ProjectId { get; set; }
    }
}
