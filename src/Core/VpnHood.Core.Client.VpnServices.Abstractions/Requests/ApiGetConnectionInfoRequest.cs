namespace VpnHood.Core.Client.VpnServices.Abstractions.Requests;

/// <summary>
/// Represents a request for the connection's info, which comes back as every answer's
/// (<see cref="ApiResponse{T}.ConnectionInfo"/>).
/// </summary>
public class ApiGetConnectionInfoRequest : IApiRequest;