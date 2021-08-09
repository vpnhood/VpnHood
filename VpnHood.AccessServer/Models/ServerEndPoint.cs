using System;
using System.Collections.Generic;

#nullable disable

namespace VpnHood.AccessServer.Models
{
    public partial class ServerEndPoint
    {
        public Guid ServerEndPointId { get; set; }
        public Guid ProjectId { get; set; }
        public string PulicEndPoint { get; set; }
        public string PrivateEndPoint { get; set; }
        public Guid AccessTokenGroupId { get; set; }
        public Guid? ServerId { get; set; }
        public byte[] CertificateRawData { get; set; }
        public string CertificateCommonName { get; set; }
        public bool IsDefault { get; set; }

        public virtual Project Project { get; set; }
        public virtual Server Server { get; set; }
        public virtual AccessTokenGroup AccessTokenGroup { get; set; }
    }
}
