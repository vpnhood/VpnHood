using System.Text.Json;

namespace VpnHood.App.Client;

public interface IRequiredAppConfigs
{
    public string AppId { get; set; }
    public int? WebUiPort { get; set; }
    public Uri? UpdateInfoUrl { get; set; }
    public string? DefaultAccessKey { get; set; }
    public string? Ga4MeasurementId { get; set; }
    public Uri? RemoteSettingsUrl { get; set; }
    public bool AllowEndPointTracker { get; set; }
    public JsonElement? CustomData { get; set; }
}