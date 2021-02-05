using System;
using VpnHood.Common;

namespace VpnHood.Client.App
{
    public class ClientProfileItem
    {
        public Guid Id => ClientProfile.ClientProfileId;
        public ClientProfile ClientProfile { get; set; }
        public Token Token { get; set; }
    }

}
