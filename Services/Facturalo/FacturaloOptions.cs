namespace Vigma.TimbradoGateway.Services.Facturalo;

/// <summary>
/// Opciones de configuración para el cliente de FacturaLO PLUS.
/// Se lee desde la sección "Facturalo" en appsettings.json.
/// </summary>
public sealed class FacturaloOptions
{
    public const string SectionName = "Facturalo";

    /// <summary>Endpoint SOAP de pruebas / desarrollo.</summary>
    public string UrlDev { get; set; } = "https://dev.facturaloplus.com/ws/servicio.do";

    /// <summary>Endpoint SOAP de producción.</summary>
    public string UrlProd { get; set; } = "https://app.facturaloplus.com/ws/servicio.do";
}
