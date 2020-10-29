
namespace VpnHood.AccessServer.Models
{
    public class ClientInfo
    {
        public const string ClientInfo_ = nameof(ClientInfo);
        public const string clientUsage_ = nameof(clientUsage);
        public const string token_ = nameof(token);
        
        public ClientUsage clientUsage { get; set; }
        public Token token { get; set; }
    }
}
