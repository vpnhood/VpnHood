using System;

namespace VpnHood.AccessServer.Models
{
    public class Client
    {
        public const string Table_ = nameof(Client);
        public const string clientId_ = nameof(clientId);
        public const string userAgent_ = nameof(userAgent);
        public const string createdTime_ = nameof(createdTime);

        public Guid clientId { get; set; }
        public string userAgent { get; set; }
        public DateTime createdTime { get; set; }
    }
}
