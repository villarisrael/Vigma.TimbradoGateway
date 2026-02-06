using System.Text.Json;
using Vigma.TimbradoGateway.Utils;


namespace Vigma.TimbradoGateway.DTOs
{
  
    public sealed class MfApiParsed
    {
        public MfApiMeta Meta { get; set; } = new();
        public string? Uuid { get; set; }
        public string? XmlTimbrado { get; set; }
        public string? RawPac { get; set; }
        public bool? Ok { get; set; }
    }

    public static class MfApiResponseParser
    {
        public static MfApiParsed Parse(string raw)
        {
            var result = new MfApiParsed
            {
                Meta = MfApiMetaParser.ParseMeta(raw)
            };

            // Si no es JSON válido, regresamos solo meta
            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;

                if (root.TryGetProperty("ok", out var okEl) && okEl.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    result.Ok = okEl.GetBoolean();

                if (root.TryGetProperty("uuid", out var uuidEl) && uuidEl.ValueKind == JsonValueKind.String)
                    result.Uuid = uuidEl.GetString();

                // IMPORTANTE: MultiFacturas a veces lo manda como "xmlTimbrado" o "xml"
                if (root.TryGetProperty("xmlTimbrado", out var xmlEl) && xmlEl.ValueKind == JsonValueKind.String)
                    result.XmlTimbrado = xmlEl.GetString();
                else if (root.TryGetProperty("xml", out var xml2El) && xml2El.ValueKind == JsonValueKind.String)
                    result.XmlTimbrado = xml2El.GetString();

                if (root.TryGetProperty("rawPac", out var rawPacEl) && rawPacEl.ValueKind == JsonValueKind.String)
                    result.RawPac = rawPacEl.GetString();

                // Si tu proveedor devuelve cfdi en otro campo, lo agregamos aquí.
                // Ej: "cfdi" : "<cfdi:Comprobante ...>"
                if (string.IsNullOrWhiteSpace(result.XmlTimbrado) &&
                    root.TryGetProperty("cfdi", out var cfdiEl) && cfdiEl.ValueKind == JsonValueKind.String)
                {
                    result.XmlTimbrado = cfdiEl.GetString();
                }
            }
            catch
            {
                // swallow: devolvemos meta como mínimo
            }

            return result;
        }
    }

}
