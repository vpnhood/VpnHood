
namespace VpnHood.AccessServer.Models
{
    public class AccessInfo
    {
        public const string AccessInfo_ = nameof(AccessInfo);
        public const string accessUsage_ = nameof(accessUsage);
        public const string token_ = nameof(token);
        
        public AccessUsage accessUsage { get; set; }
        public Token token { get; set; }
    }
}
