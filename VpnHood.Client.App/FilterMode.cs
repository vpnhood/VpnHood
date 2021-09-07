using System.Text.Json.Serialization;

namespace VpnHood.Client.App
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum FilterMode
    {
        All,
        Exclude,
        Include
    }
}