using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpnHood.Net.Toolkit.Converters;

// An enum written by its name, as JsonStringEnumConverter writes it, read so that a name this side does not
// know - one a newer app, engine or server added - gives the enum's fallback instead of failing the whole
// document: its JsonEnumFallback, or for an enum of another library a subclass's own. A number is read as
// JsonStringEnumConverter reads it, and an undefined one falls back too. A dictionary key is read the same way.
public class JsonStringEnumFallbackConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private static readonly JsonConverter<TEnum> Inner =
        (JsonConverter<TEnum>)new JsonStringEnumConverter<TEnum>().CreateConverter(typeof(TEnum), JsonSerializerOptions.Default);

    public TEnum Fallback { get; }

    public JsonStringEnumFallbackConverter()
        : this(GetFallback())
    {
    }

    protected JsonStringEnumFallbackConverter(TEnum fallback)
    {
        Fallback = fallback;
    }

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

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
        Inner.Write(writer, value, options);

    public override TEnum ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options) =>
        Parse(reader.GetString());

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
