namespace VpnHood.Net.Toolkit.Converters;

/// <summary>
/// Specifies the value <see cref="JsonStringEnumFallbackConverter{TEnum}"/> reads for a name the enum does not have:
/// the one a reader takes when the other side is newer, so it must be safe to act on, such as an error for an unknown
/// error code, or the fail-closed mode for an unknown mode.
/// </summary>
/// <param name="value">The fallback, one of the enum's values.</param>
[AttributeUsage(AttributeTargets.Enum)]
public sealed class JsonEnumFallbackAttribute(object value) : Attribute
{
    /// <summary>Gets the fallback.</summary>
    public object Value { get; } = value;
}
