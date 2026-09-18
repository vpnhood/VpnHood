using VpnHood.AppLib.Api;

namespace VpnHood.AppLib.WebHosting;

// What a web host is made with: the API it puts on HTTP, where it may unpack, the port to prefer, and
// how it should behave. Every decision here is the app's - the host obeys rather than infers, so what
// counts as a developer's build is never spelled out on the server side. One of these per host, since
// the local and the remote one are not told the same thing. The app assembles it, so the server half
// names nothing of the engine and compiles against the contract alone - the mirror of
// VpnHood.AppLib.Api.HttpClients on the other side of the same API. Not to be confused with
// WebHostOptions, which is the head's and says what to serve; this is the app's and says what to
// serve it for.
public class WebHostCreateParams
{
    // One instance, every transport, so a paired browser and the device's own UI cannot drift apart.
    public required VpnHoodApi Api { get; init; }

    // The root under which the host keeps its unpacked UI (Temp/WebRoot/<hash>).
    public required string StorageFolderPath { get; init; }

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
