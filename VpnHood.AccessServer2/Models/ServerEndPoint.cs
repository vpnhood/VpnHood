using System;
using System.Collections.Generic;

#nullable disable

namespace VpnHood.AccessServer.Models
{
    public partial class ServerEndPoint
    {
        public ServerEndPoint()
        {
        }

        public bool IsDefault { get; set; }
        public string ServerEndPointId { get; set; }
        public int ServerEndPointGroupId { get; set; }
        public Guid? ServerId { get; set; }
        public byte[] CertificateRawData { get; set; }

        public virtual Server Server { get; set; }
        public virtual ServerEndPointGroup ServerEndPointGroup { get; set; }
    }
}
