using System;

namespace VpnHood.AccessServer.Models
{
    public class Server
    {
        public const string Table_ = nameof(Server);
        public const string serverId_ = nameof(serverId);
        public const string serverName_ = nameof(serverName);
        public const string createdTime_ = nameof(createdTime);
        public const string lastStatusTime_ = nameof(lastStatusTime);
        public const string lastSessionCount_ = nameof(lastSessionCount);

        public Guid serverId { get; set; }
        public string serverName { get; set; }
        public DateTime createdTime { get; set; }
        public DateTime lastStatusTime { get; set; }
        public int lastSessionCount { get; set; }
    }
}
