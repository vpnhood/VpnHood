namespace VpnHood.AccessServer.Dtos;

public class AccessData(Access access, AccessToken.AccessToken accessToken, Device? device)
{
    public Access Access { get; } = access ?? throw new ArgumentNullException(nameof(access));
    public AccessToken.AccessToken AccessToken { get; } = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
    public Device? Device { get; } = device;
}