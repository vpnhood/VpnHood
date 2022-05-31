using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpnHood.Common.Converters;

public class TimeSpanConverter : JsonConverter<TimeSpan>
{
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType == JsonTokenType.Number
            ? TimeSpan.FromSeconds(reader.GetDouble()) 
            : TimeSpan.Parse(reader.GetString()!);
    }

    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
        //writer.WriteNumberValue(value.TotalSeconds);
    }
}