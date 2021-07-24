namespace VpnHood.AccessServer.Models
{
    public class ServerEndPoint
    {
        public const string Table_ = nameof(ServerEndPoint);
        public const string serverEndPointId_ = nameof(serverEndPointId);
        public const string serverEndPointGroupId_ = nameof(serverEndPointGroupId);
        public const string serverId_ = nameof(serverId);
        public const string rawData_ = nameof(certificateRawData);

        public string serverEndPointId { get; set; }
        public int serverEndPointGroupId { get; set; }
        public int serverId { get; set; }
        public byte[] certificateRawData { get; set; }
    }
}
