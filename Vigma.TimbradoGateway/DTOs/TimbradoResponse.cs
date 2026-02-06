using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vigma.TimbradoGateway.DTOs;

public class TimbradoResponse
{
    // === Formato simple (tu WS interno / gateway) ===
    public bool ok { get; set; }
    public string? uuid { get; set; }

    // En el formato simple viene aquí; en el formato real lo normalizamos desde "cfdi"
    public string? xmlTimbrado { get; set; }

    public string? codigo { get; set; }
    public string? mensaje { get; set; }
    public string? error { get; set; }
    public string? rawPac { get; set; }
    public long logId { get; set; }

    // === Formato “real” (respuesta MF expandida) ===
    public string? cfdi { get; set; }               // XML CFDI timbrado
    public string? png { get; set; }                // Base64 PNG (QR/representación)
    public string? archivo_xml { get; set; }        // ruta en MF
    public string? archivo_png { get; set; }        // ruta en MF

    public string? produccion { get; set; }
    public int? codigo_mf_numero { get; set; }
    public string? codigo_mf_texto { get; set; }

    public string? mensaje_original_pac_json { get; set; } // JSON interno (string)
    public string? cancelada { get; set; }
    public int? saldo { get; set; }
    public string? servidor { get; set; }
    public string? ejecucion { get; set; }
    public bool? abortar { get; set; }
    public string? error_debug_log_xml_pac { get; set; }
    public string? version_kit { get; set; }

    // Estos vienen como {"0":"valor"} en tu ejemplo
    public Dictionary<string, string>? representacion_impresa_fecha_timbrado { get; set; }
    public Dictionary<string, string>? representacion_impresa_sello { get; set; }
    public Dictionary<string, string>? representacion_impresa_selloSAT { get; set; }
    public Dictionary<string, string>? representacion_impresa_certificadoSAT { get; set; }
    public string? representacion_impresa_cadena { get; set; }
    public string? representacion_impresa_certificado_no { get; set; }

    // Captura llaves nuevas sin romper
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? extra { get; set; }

    // === Helpers de normalización ===

    /// <summary>Devuelve el XML timbrado venga en xmlTimbrado o en cfdi.</summary>
    public string? GetXml() => !string.IsNullOrWhiteSpace(xmlTimbrado) ? xmlTimbrado : cfdi;

    /// <summary>Devuelve el código MF número como string (prioriza el simple).</summary>
    public string? GetCodigo()
        => !string.IsNullOrWhiteSpace(codigo) ? codigo : (codigo_mf_numero?.ToString());

    /// <summary>Devuelve el texto MF (prioriza el simple).</summary>
    public string? GetMensaje()
        => !string.IsNullOrWhiteSpace(mensaje) ? mensaje : codigo_mf_texto;

    public static string? Get0(Dictionary<string, string>? d)
        => d != null && d.TryGetValue("0", out var v) ? v : null;
}
