using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.App;

/// <summary>Specifies where the VPN's connection stands, as the app shows it.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AppConnectionState>))]
public enum AppConnectionState
{
    /// <summary>Idle: no connection, or a disconnect that finishes in the background.</summary>
    None,

    /// <summary>The connect gets ready: the client starts, or the split by country or by app prepares its data.</summary>
    Initializing,

    /// <summary>The system asks the person to allow the VPN, on the device's own screen, as Android does at a first connect.</summary>
    WaitingForVpnPermission,

    /// <summary>The client waits before it goes on.</summary>
    Waiting,

    /// <summary>The connect waits for the ad its plan needs.</summary>
    WaitingForAd,

    /// <summary>The diagnose checks the network and the server.</summary>
    Diagnosing,

    /// <summary>The client checks the proxies.</summary>
    ValidatingProxies,

    /// <summary>The client has looked for a server it can reach for more than a moment.</summary>
    FindingReachableServer,

    /// <summary>The client has looked for the best server for more than a moment.</summary>
    FindingBestServer,

    /// <summary>The VPN is connecting.</summary>
    Connecting,

    /// <summary>The VPN is connected.</summary>
    Connected,

    /// <summary>The VPN is connected, but its connection is not stable.</summary>
    Unstable,

    /// <summary>The VPN is disconnecting.</summary>
    Disconnecting,

    /// <summary>The app installs its update, which stops it in a moment: no connect until it has.</summary>
    Updating
}
