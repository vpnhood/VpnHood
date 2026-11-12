namespace VpnHood.Core.Client.VpnServices.Abstractions.Requests;

/// <summary>
/// Represents a request that asks the VPN service to disconnect; the service answers at once and disconnects in the
/// background.
/// </summary>
public class ApiDisconnectRequest : IApiRequest;