using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpnHood.Net.Toolkit.Converters;

/// <summary>
/// Converts an enum by its name, as <see cref="JsonStringEnumConverter"/> does, but reads a name this side does not
/// know, one a newer app, engine or server added, as the enum's fallback instead of failing the whole document.
/// </summary>
/// <remarks>
/// The fallback is the enum's <see cref="JsonEnumFallbackAttribute"/>, or, for an enum of another library, a
/// subclass's own. A name is matched ignoring case. A number is read as <see cref="JsonStringEnumConverter"/> reads it,
/// and an undefined one falls back too. A dictionary key is read the same way.
/// </remarks>
/// <typeparam name="TEnum">The enum.</typeparam>
public class JsonStringEnumFallbackConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private static readonly JsonConverter<TEnum> Inner =
        (JsonConverter<TEnum>)new JsonStringEnumConverter<TEnum>().CreateConverter(typeof(TEnum), JsonSerializerOptions.Default);

    /// <summary>Gets the value read for a name or a number the enum does not have.</summary>
    public TEnum Fallback { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonStringEnumFallbackConverter{TEnum}"/> class with the enum's
    /// <see cref="JsonEnumFallbackAttribute"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The enum has no <see cref="JsonEnumFallbackAttribute"/>, or its value is not one of the enum's.
    /// </exception>
    public JsonStringEnumFallbackConverter()
        : this(GetFallback())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonStringEnumFallbackConverter{TEnum}"/> class with a fallback, for
    /// an enum of another library, which a subclass names.
    /// </summary>
    /// <param name="fallback">The value read for a name or a number the enum does not have.</param>
    protected JsonStringEnumFallbackConverter(TEnum fallback)
    {
        Fallback = fallback;
    }

    /// <inheritdoc/>
    /// <exception cref="JsonException">The value is neither a string nor a number.</exception>
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return reader.TryGetInt64(out var number) && Enum.IsDefined(typeof(TEnum), ToEnum(number))
                ? ToEnum(number)
                : Fallback;

        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException($"{typeof(TEnum).Name} must be a string. TokenType: {reader.TokenType}");

        return Parse(reader.GetString());
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
        Inner.Write(writer, value, options);

    /// <inheritdoc/>
    public override TEnum ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options) =>
        Parse(reader.GetString());

    /// <inheritdoc/>
    public override void WriteAsPropertyName(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
        Inner.WriteAsPropertyName(writer, value, options);

    private TEnum Parse(string? name) =>
        Enum.TryParse<TEnum>(name, ignoreCase: true, out var value) && Enum.IsDefined(value)
            ? value
            : Fallback;

    private static TEnum ToEnum(long number) => (TEnum)Enum.ToObject(typeof(TEnum), number);

    private static TEnum GetFallback()
    {
        var attribute = (JsonEnumFallbackAttribute?)Attribute.GetCustomAttribute(typeof(TEnum),
                            typeof(JsonEnumFallbackAttribute)) ??
                        throw new InvalidOperationException(
                            $"{typeof(TEnum).Name} has no JsonEnumFallback: a reader must know what a name it does not know means.");

        return attribute.Value is TEnum value
            ? value
            : throw new InvalidOperationException($"{typeof(TEnum).Name}'s JsonEnumFallback is not one of its values.");
    }
}
