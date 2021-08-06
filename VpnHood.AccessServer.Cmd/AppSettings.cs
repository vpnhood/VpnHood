using System;

namespace VpnHood.AccessServer.Cmd
{
    class AppSettings
    {
        public Uri ServerUrl { get; set; }
        public string AuthHeader { get; set; }
        public Guid ProjectId { get; set; }
    }
}
