using System.Text.Json.Serialization;

namespace VpnHood.Core.Client.DomainFiltering;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DomainFilterAction
{
    None,
    Block,
    Exclude,
    Include
}