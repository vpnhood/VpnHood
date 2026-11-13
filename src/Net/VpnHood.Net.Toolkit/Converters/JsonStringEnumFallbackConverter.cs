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
/// and an undefined one falls back too, as does one beyond the enum's own type. Of an enum with
/// <see cref="FlagsAttribute"/>, a combination of its values is read as it is, by its names or by its number, and
/// falls back when it holds a name or a bit the enum does not have. A dictionary key is read the same way.
/// </remarks>
/// <typeparam name="TEnum">The enum.</typeparam>
public class JsonStringEnumFallbackConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private static readonly JsonConverter<TEnum> Inner =
        (JsonConverter<TEnum>)new JsonStringEnumConverter<TEnum>().CreateConverter(typeof(TEnum), JsonSerializerOptions.Default);

    private static readonly bool IsSigned = Type.GetTypeCode(Enum.GetUnderlyingType(typeof(TEnum))) is
        TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64;

    // The bits of a flags enum's values together, as a combination of them is one of the enum's too; null for an
    // enum without flags, whose values are its members alone. After IsSigned, which it reads.
    private static readonly ulong? FlagBits = GetFlagBits();

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
    /// <exception cref="ArgumentException"><paramref name="fallback"/> is not one of the enum's values.</exception>
    protected JsonStringEnumFallbackConverter(TEnum fallback)
    {
        if (!IsKnown(fallback))
            throw new ArgumentException($"{fallback} is not one of {typeof(TEnum).Name}'s values.", nameof(fallback));

        Fallback = fallback;
    }

    /// <inheritdoc/>
    /// <exception cref="JsonException">The value is neither a string nor a number.</exception>
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return TryReadNumber(ref reader, out var numberValue) && IsKnown(numberValue) ? numberValue : Fallback;

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
        Enum.TryParse<TEnum>(name, ignoreCase: true, out var value) && IsKnown(value)
            ? value
            : Fallback;

    // Within the enum's own type only: Enum.ToObject cuts a larger number down to it, which could land on a
    // value the enum has. The way back to the number tells.
    private static bool TryReadNumber(ref Utf8JsonReader reader, out TEnum value)
    {
        value = default;
        if (IsSigned) {
            if (!reader.TryGetInt64(out var number))
                return false;

            value = (TEnum)Enum.ToObject(typeof(TEnum), number);
            return Convert.ToInt64(value) == number;
        }

        if (!reader.TryGetUInt64(out var unsignedNumber))
            return false;

        value = (TEnum)Enum.ToObject(typeof(TEnum), unsignedNumber);
        return Convert.ToUInt64(value) == unsignedNumber;
    }

    private static bool IsKnown(TEnum value) =>
        FlagBits is { } flagBits ? (ToBits(value) & ~flagBits) == 0 : Enum.IsDefined(value);

    private static ulong ToBits(TEnum value) =>
        IsSigned ? unchecked((ulong)Convert.ToInt64(value)) : Convert.ToUInt64(value);

    private static ulong? GetFlagBits()
    {
        if (!typeof(TEnum).IsDefined(typeof(FlagsAttribute), inherit: false))
            return null;

        var flagBits = 0UL;
        foreach (var value in Enum.GetValues<TEnum>())
            flagBits |= ToBits(value);

        return flagBits;
    }

    private static TEnum GetFallback()
    {
        var attribute = (JsonEnumFallbackAttribute?)Attribute.GetCustomAttribute(typeof(TEnum),
                            typeof(JsonEnumFallbackAttribute)) ??
                        throw new InvalidOperationException(
                            $"{typeof(TEnum).Name} has no JsonEnumFallback: a reader must know what a name it does not know means.");

        // one of the enum's, not merely of its type: a cast number is of its type too
        return attribute.Value is TEnum value && IsKnown(value)
            ? value
            : throw new InvalidOperationException($"{typeof(TEnum).Name}'s JsonEnumFallback is not one of its values.");
    }
}
