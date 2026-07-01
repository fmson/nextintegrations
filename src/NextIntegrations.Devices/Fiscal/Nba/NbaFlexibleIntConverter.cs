using System.Text.Json;
using System.Text.Json.Serialization;

namespace NextIntegrations.Devices.Fiscal.Nba;

/// <summary>
/// Reads integer fields the fiscalbox may emit either as a JSON integer (<c>119</c>) or, like some
/// count/report fields, as a JSON float (<c>119.0</c>) or even a numeric string (<c>"119"</c>).
/// Writes back a plain integer.
/// </summary>
internal sealed class NbaFlexibleIntConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                // Handles both integers and floats (e.g. 2.0) by rounding toward the nearest integer.
                return reader.TryGetInt32(out int i) ? i : (int)Math.Round(reader.GetDouble());
            case JsonTokenType.String:
                string? s = reader.GetString();
                return int.TryParse(s, out int parsed) ? parsed : 0;
            case JsonTokenType.Null:
                return 0;
            default:
                throw new JsonException($"Unexpected token {reader.TokenType} for an integer field.");
        }
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumberValue(value);
    }
}
