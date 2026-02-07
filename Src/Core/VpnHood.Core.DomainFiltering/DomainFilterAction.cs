using System.Text.Json.Serialization;

namespace VpnHood.Core.SniFiltering;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DomainFilterAction
{
    None, // means domain is unknown or not in any list
    Block,
    Exclude,
    Include
}