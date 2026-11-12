
using VpnHood.AppLib.Api.VpnProfiles;
using VpnHood.AppLib.Api.Premium;

namespace VpnHood.AppLib.Api;

/// <summary>Represents the app's API for its VPN profiles: each an access key the app connects with.</summary>
/// <remarks>A failure comes as <see cref="IAppApi"/> describes, in process and over HTTP.</remarks>
public interface IVpnProfilesApi
{
    /// <summary>Adds a VPN profile for an access key, or updates the one the app has for it.</summary>
    /// <param name="accessKey">The access key.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the profile.</returns>
    /// <exception cref="UnauthorizedAccessException">The access key is a built-in VPN profile's.</exception>
    /// <exception cref="OverflowException">A duration in the access key is out of range.</exception>
    /// <exception cref="InvalidDataException"><paramref name="accessKey"/> holds an empty access key.</exception>
    /// <exception cref="System.Text.Json.JsonException"><paramref name="accessKey"/> holds no valid access key.</exception>
    /// <exception cref="NotSupportedException">The access key's version is not one the app reads.</exception>
    /// <exception cref="FormatException">
    /// <paramref name="accessKey"/> is no valid Base64, or an endpoint or a duration in it is not valid.
    /// </exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task<VpnProfileInfo> AddByAccessKey(string accessKey, CancellationToken cancellationToken);

    /// <summary>Gets a VPN profile.</summary>
    /// <param name="vpnProfileId">The VPN profile's ID.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the profile.</returns>
    /// <exception cref="VpnHood.Net.Toolkit.Exceptions.NotExistsException">No VPN profile has <paramref name="vpnProfileId"/>.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task<VpnProfileInfo> Get(Guid vpnProfileId, CancellationToken cancellationToken);

    /// <summary>Gets the premium code a VPN profile connects with.</summary>
    /// <param name="vpnProfileId">The VPN profile's ID.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the premium code, or an empty string where the profile has none.</returns>
    /// <exception cref="VpnHood.Net.Toolkit.Exceptions.NotExistsException">No VPN profile has <paramref name="vpnProfileId"/>.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task<string> GetAccessCode(Guid vpnProfileId, CancellationToken cancellationToken);

    /// <summary>Changes a VPN profile.</summary>
    /// <param name="vpnProfileId">The VPN profile's ID.</param>
    /// <param name="updateParams">What to change; what it leaves out stays.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the profile afterwards.</returns>
    /// <exception cref="FormatException"><paramref name="updateParams"/> sets a server address that is not valid.</exception>
    /// <exception cref="ArgumentException"><paramref name="updateParams"/> sets a premium code that is not valid.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.Exceptions.NotExistsException">No VPN profile has <paramref name="vpnProfileId"/>.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task<VpnProfileInfo> Update(Guid vpnProfileId, VpnProfileUpdateParams updateParams, CancellationToken cancellationToken);

    /// <summary>Deletes a VPN profile, and disconnects first where it is the one connected.</summary>
    /// <param name="vpnProfileId">The VPN profile's ID.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">The VPN profile is built into the app.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.Exceptions.NotExistsException">No VPN profile has <paramref name="vpnProfileId"/>.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    /// <remarks>It disconnects first where the VPN profile is the connected one, even where the delete then fails.</remarks>
    Task Delete(Guid vpnProfileId, CancellationToken cancellationToken);

    /// <summary>Gets where the person can buy a premium plan for a VPN profile.</summary>
    /// <param name="vpnProfileId">The VPN profile's ID.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the purchase options.</returns>
    /// <exception cref="VpnHood.Net.Toolkit.Exceptions.NotExistsException">No VPN profile has <paramref name="vpnProfileId"/>.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task<AppPurchaseOptions> GetPurchaseOptions(Guid vpnProfileId, CancellationToken cancellationToken);
}