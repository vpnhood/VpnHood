namespace VpnHood.Net.Toolkit.Converters;

// The value JsonStringEnumFallbackConverter reads for a name the enum does not have: the one a reader
// takes when the other side is newer, so it must be safe to act on - an error for an unknown error code,
// the fail-closed mode for an unknown mode.
[AttributeUsage(AttributeTargets.Enum)]
public sealed class JsonEnumFallbackAttribute(object value) : Attribute
{
    public object Value { get; } = value;
}
