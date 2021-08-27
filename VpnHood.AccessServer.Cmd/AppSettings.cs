using System;

namespace VpnHood.AccessServer.Cmd
{
    internal class AppSettings
    {
        public AppSettings(Guid projectId, Uri serverUrl, string authorization)
        {
            ProjectId = projectId;
            ServerUrl = serverUrl;
            Authorization = authorization;
        }

        public Guid ProjectId { get; set; }
        public Uri ServerUrl { get; set; }
        public string Authorization { get; set; }
    }
}