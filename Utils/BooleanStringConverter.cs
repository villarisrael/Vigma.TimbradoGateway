using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vigma.TimbradoGateway.Utils;

/// <summary>
/// Conversor JSON que acepta booleano O cadena ("true"/"false")
/// y serializa como booleano puro.
/// Útil para APIs que devuelven {"ok": "true"} en lugar de {"ok": true}
/// </summary>
public class BooleanStringConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True:
                return true;
            case JsonTokenType.False:
                return false;
            case JsonTokenType.String:
                var stringValue = reader.GetString();
                if (bool.TryParse(stringValue, out var b))
                    return b;
                // Valores comunes de PACs
                if (string.Equals(stringValue, "SI", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (string.Equals(stringValue, "NO", StringComparison.OrdinalIgnoreCase))
                    return false;
                throw new JsonException($"No se puede parsear '{stringValue}' como boolean.");
            default:
                throw new JsonException($"Token inesperado: {reader.TokenType}");
        }
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
    {
        // Siempre serializa como booleano puro (true/false, sin comillas)
        writer.WriteBooleanValue(value);
    }
}
