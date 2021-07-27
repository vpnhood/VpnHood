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
        public Guid AccountId { get; set; }
        public Guid ServerEndPointId { get; set; }
        public string PulicEndPoint { get; set; }
        public string LocalEndPoint { get; set; }
        public Guid ServerEndPointGroupId { get; set; }
        public Guid? ServerId { get; set; }
        public byte[] CertificateRawData { get; set; }
        public bool IsDefault { get; set; }

        public virtual Account Account { get; set; }
        public virtual Server Server { get; set; }
        public virtual ServerEndPointGroup ServerEndPointGroup { get; set; }
    }
}
