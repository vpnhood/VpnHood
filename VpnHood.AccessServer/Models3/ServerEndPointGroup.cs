namespace VpnHood.AccessServer.Models
{
    public class ServerEndPointGroup
    {
        public const string Table_ = nameof(ServerEndPointGroup);
        public const string serverEndPointGroupId_ = nameof(serverEndPointGroupId);
        public const string defaultServerEndPoint_ = nameof(defaultServerEndPointId);
        public string serverEndPointGroupId { get; set; }
        public string defaultServerEndPointId { get; set; }
    }
}
