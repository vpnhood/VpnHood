using GrayMint.Common.Utils;
using VpnHood.AccessServer.Dtos.FarmTokenRepos;

namespace VpnHood.AccessServer.Dtos.ServerFarms;

public class ServerFarmUpdateParams
{
    public Patch<string>? ServerFarmName { get; set; }
    public Patch<Guid>? ServerProfileId { get; set; }
    public Patch<bool>? UseHostName { get; set; }
    public Patch<byte[]>? Secret { get; set; }
    public Patch<bool>? PushTokenToClient { get; set; }
    public Patch<bool>? AutoValidateCertificate { get; set; }
    public Patch<int>? MaxCertificateCount { get; set; }
    public Patch<FarmTokenRepo[]>? TokenRepos { get; set; }
}