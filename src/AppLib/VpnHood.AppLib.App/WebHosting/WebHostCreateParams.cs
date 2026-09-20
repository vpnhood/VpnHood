using VpnHood.AppLib.Api;
using VpnHood.Core.Toolkit.Assets;

namespace VpnHood.AppLib.WebHosting;

// Everything a web host is made with: the API it puts on HTTP, the two things it serves - the page
// and the UI's files - the port to prefer, and how it should behave. The head chooses all of it on
// AppOptions; the app hands it over here, one of these per host, since the local and the remote one
// are not told the same thing. The host obeys rather than infers, so what counts as a developer's
// build is never spelled out on the server side, and the server half names nothing of the engine and
// compiles against the contract alone - the mirror of VpnHood.AppLib.Api.HttpClients on the other
// side of the same API.
public class WebHostCreateParams
{
    // One instance, every transport, so a paired browser and the device's own UI cannot drift apart.
    public required VpnHoodApi Api { get; init; }

    // The page this host serves - index.html at its root - to the app's own web view and to a paired
    // device alike: the SPA, or the Avalonia UI's browser build, whichever the head embedded. One
    // instance for both hosts, so the zip behind it is extracted once.
    public required IAssetProvider WebRoot { get; init; }

    // The files the app's UI draws from, which the host serves at /assets/ to a paired phone's page -
    // the same instance the app's own UI reads. Null for a head whose UI brings nothing of its own.
    public IAssetProvider? UiAssetProvider { get; init; }

    // What the local host binds when it is free, and what the remote one is pinned to.
    public required int? WebUiPort { get; init; }

    // Stay up for the life of the process, whether or not anything asked: true for the local host,
    // which the app's own UI loads, and for remote access when a developer opened it. A host that is
    // always on also refuses to stop, since nothing holds it that could let go.
    public required bool IsAlwaysOn { get; init; }

    // Ask a remote caller for the pairing token. False is the developer's open door - a debug build or
    // /remote-access - where there is no screen to read a token from. It says nothing about the local
    // host: a loopback request is never remote, so it is never asked. Read once at launch and never
    // written back to the settings.
    public required bool IsPairingRequired { get; init; }
}
