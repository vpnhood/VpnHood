using System.Text.Json.Serialization;

namespace VpnHood.AppLogic.Api.Exceptions;

/// <summary>Specifies why a split tunneling list refuses an entry (<see cref="SplitEntryException"/>).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SplitEntryError>))]
public enum SplitEntryError
{
    /// <summary>A domain has a <c>*</c>: an entry matches its domain and every subdomain already.</summary>
    Asterisk,

    /// <summary>An entry of an IP list is no address, range or network.</summary>
    InvalidIpRange
}
