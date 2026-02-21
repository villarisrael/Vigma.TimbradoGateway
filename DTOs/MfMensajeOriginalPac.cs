using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class MfMensajeOriginalPac
{
    [JsonPropertyName("data")]
    public MfMensajeOriginalPacData? Data { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; } // success/error
}

public sealed class MfMensajeOriginalPacData
{
    [JsonPropertyName("uuid")] public string? Uuid { get; set; }
    [JsonPropertyName("fechaTimbrado")] public string? FechaTimbrado { get; set; }
    [JsonPropertyName("noCertificadoSAT")] public string? NoCertificadoSAT { get; set; }
    [JsonPropertyName("noCertificadoCFDI")] public string? NoCertificadoCFDI { get; set; }
    [JsonPropertyName("selloSAT")] public string? SelloSAT { get; set; }
    [JsonPropertyName("selloCFDI")] public string? SelloCFDI { get; set; }
    [JsonPropertyName("cadenaOriginalSAT")] public string? CadenaOriginalSAT { get; set; }
    [JsonPropertyName("qrCode")] public string? QrCodeBase64 { get; set; }
    [JsonPropertyName("cfdi")] public string? Cfdi { get; set; }
}

public static class MfInnerPacParser
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true
    };

    public static MfMensajeOriginalPac? Parse(string? jsonInner)
    {
        if (string.IsNullOrWhiteSpace(jsonInner)) return null;
        return JsonSerializer.Deserialize<MfMensajeOriginalPac>(jsonInner, Opts);
    }
}
