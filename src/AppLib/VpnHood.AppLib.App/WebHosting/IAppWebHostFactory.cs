namespace VpnHood.AppLib.App.WebHosting;

/// <summary>
/// Defines how the app gets its two web hosts (<see cref="AppOptions.WebHostFactory"/>): the local one this device
/// loads its UI from, and the remote one a phone pairs with, one implementation bound in two ways.
/// </summary>
/// <remarks>
/// A head hands the factory in, as a host needs the API to exist first while the app says at its making whether
/// pairing is supported. The app calls each method the first time anything asks for that host, and
/// <see cref="CreateRemote"/> again when the developer's open door is turned on or off, as it then replaces the remote
/// host; it owns and disposes what they return. Everything a host is made with is in
/// <see cref="WebHostCreateParams"/>, so an implementation needs nothing of the engine, only the API it puts on HTTP.
/// </remarks>
public interface IAppWebHostFactory
{
    /// <summary>Creates the host this device loads its UI from, on loopback, not yet started.</summary>
    /// <param name="createParams">What the host is made with.</param>
    /// <returns>The host.</returns>
    IAppWebHost CreateLocal(WebHostCreateParams createParams);

    /// <summary>Creates the host a phone pairs with, on the LAN's addresses, not yet started.</summary>
    /// <param name="createParams">What the host is made with.</param>
    /// <returns>The host.</returns>
    IAppWebHost CreateRemote(WebHostCreateParams createParams);
}
