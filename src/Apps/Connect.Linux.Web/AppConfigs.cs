using System.Text.Json;
using VpnHood.App.Client;
using VpnHood.AppLib.Utils;
using VpnHood.Core.Client.Abstractions;

// ReSharper disable HeuristicUnreachableCode
// ReSharper disable StringLiteralTypo
// ReSharper disable CommentTypo
namespace VpnHood.App.Connect.Linux.Web;

internal class AppConfigs : AppConfigsBase<AppConfigs>, IRequiredAppConfigs
{
    public const string AppTitle = IsDebugMode ? "VpnHOOD! CONNECT (DEBUG)" : "VpnHood! CONNECT";

    // currently can not have space or more than 20 characters in linux app name as it used for adapter name
    public const string AppName = IsDebugMode ? "VpnHOODConnect_dbg" : "VpnHoodConnect";
    // `.web` marks the direct-download channel, leaving the bare `com.vpnhood.connect.linux` free
    // for a store build (Snap/Flatpak) later. NOTE: the app id is hashed into the client identity
    // (AppUtils.CreateClientId = MD5("appId:deviceId")), so changing it makes every existing
    // install register as a NEW device against the access code's device limit until the stale
    // entries pass deviceLifeSpan. Accepted by the owner on 2026-08-26 for this head and Windows.
    public string AppId { get; set; } =
        IsDebugMode ? "com.vpnhood.connect.linux.web.debug" : "com.vpnhood.connect.linux.web";
    public Uri? UpdateInfoUrl { get; set; } = null;
    public int? WebUiPort { get; set; } = IsDebugMode ? 7701 : 7770;
    public string? DefaultAccessKey { get; set; } = IsDebugMode ? ClientOptions.SampleAccessKey : null;
    // Supplied by the embedded AppSettings (the private .user folder), never in code: a fork has no
    // portal of ours to point at, and a hard-coded host would send its users to a server that is not
    // theirs. Null here means the app ships WITHOUT account features — App.cs builds no account
    // provider at all — which is the right default for anyone building this tree without our config.
    public Uri? PortalBaseUri { get; set; }

    public bool PortalIgnoreSslVerification { get; set; }
    public string? Ga4MeasurementId { get; set; }
    public Uri? RemoteSettingsUrl { get; set; }
    public bool AllowEndPointTracker { get; set; }
    public JsonElement? CustomData { get; set; }

    public static AppConfigs Load()
    {
        var appConfigs = new AppConfigs();
        appConfigs.LoadConfig();

        // The default access key is embedded as its own resource (per configuration) so it can be
        // sourced from a GitHub secret. When present it overrides the in-code/json default.
        var accessKey = appConfigs.ReadResourceText("access_key_default.txt");
        if (!string.IsNullOrWhiteSpace(accessKey))
            appConfigs.DefaultAccessKey = accessKey.Trim();

        return appConfigs;
    }


#if DEBUG
    public const bool IsDebugMode = true;
#else
    public const bool IsDebugMode = false;
#endif
    public static bool IsDebug => IsDebugMode;
}