using System;
using System.Text.Json;

namespace VpnHood.Client.App
{
    public class ClientProfile
    {
        public string Name { get; set; }
        public Guid ClientProfileId { get; set; }
        public Guid TokenId { get; set; }
    }
}
