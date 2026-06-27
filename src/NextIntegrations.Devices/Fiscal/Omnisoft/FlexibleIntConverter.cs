using System.Text.Json;
using System.Text.Json.Serialization;

namespace NextIntegrations.Devices.Fiscal.Omnisoft;

/// <summary>
/// Reads an integer field that the Omnisoft device may emit as a JSON float (e.g. <c>"saleCount": 2.0</c>
/// in report payloads, PDF p.29). Accepts both integer and fractional JSON numbers and rounds to the
/// nearest <see cref="int"/>; writes a plain integer.
/// </summary>
internal sealed class FlexibleIntConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            if (reader.TryGetInt32(out int value))
            {
                return value;
            }

            return (int)Math.Round(reader.GetDouble(), MidpointRounding.AwayFromZero);
        }

        throw new JsonException($"Expected a number for an integer field but found {reader.TokenType}.");
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value);
}
