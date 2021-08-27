using System;

namespace VpnHood.AccessServer.Models
{
    public class ServerEndPoint
    {
        public Guid ServerEndPointId { get; set; }
        public Guid ProjectId { get; set; }
        public string PublicEndPoint { get; set; } = null!;
        public string? PrivateEndPoint { get; set; }
        public Guid AccessTokenGroupId { get; set; }
        public Guid? ServerId { get; set; }
        public byte[] CertificateRawData { get; set; } = null!;
        public string CertificateCommonName { get; set; } = null!;
        public bool IsDefault { get; set; }

        public virtual Project? Project { get; set; }
        public virtual Server? Server { get; set; }
        public virtual AccessTokenGroup? AccessTokenGroup { get; set; }
    }
}