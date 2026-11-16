using VpnHood.Net.Toolkit.ApiClients;

namespace VpnHood.Core.Client.VpnServices.Abstractions.Requests;

/// <summary>
/// Represents the VPN service's answer to a request.
/// </summary>
/// <typeparam name="T">The type of the request's result.</typeparam>
public class ApiResponse<T>
{
    /// <summary>Gets or sets the connection's info as of the answer.</summary>
    public required ConnectionInfo ConnectionInfo { get; set; }

    /// <summary>Gets or sets the error the request failed with, or <see langword="null"/> where it succeeded.</summary>
    public required ApiError? ApiError { get; set; }

    /// <summary>Gets or sets the request's result, or the default where it has none or failed.</summary>
    public required T? Result { get; set; }
}