namespace VpnHood.AccessServer.Models
{
    public class Certificate
    {
        public const string Table_ = nameof(Certificate);
        public const string serverEndPoint_ = nameof(serverEndPoint);
        public const string rawData_ = nameof(rawData);

        public string serverEndPoint { get; set; }
        public byte[] rawData { get; set; }
    }
}
