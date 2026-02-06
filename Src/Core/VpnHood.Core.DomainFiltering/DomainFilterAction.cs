using System.Text.Json.Serialization;

namespace VpnHood.Core.DomainFiltering;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DomainFilterAction
{
    Block,
    Exclude,
    Include
}