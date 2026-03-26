namespace Vigma.TimbradoGateway.ViewsModels.Errores;

/// <summary>
/// ViewModel exclusivo para la vista Estadisticaerrores.
/// Se construye con consultas agregadas sobre timbrado_error_log.
/// No modifica los modelos de entidad existentes.
/// </summary>
public class EstadisticasErroresVM
{
    // ── Totales por periodo ────────────────────────────────────────────────
    public int TotalHoy     { get; set; }   // últimas 24 h
    public int TotalSemana  { get; set; }   // últimos 7 días
    public int TotalMes     { get; set; }   // últimos 30 días

    // ── Categorías (últimas 24 h) ──────────────────────────────────────────
    // Clasificación por código SAT / texto del PAC
    public int CatSat         { get; set; }  // códigos 301-399 o texto SAT/firma/sello
    public int CatCertificado { get; set; }  // códigos 201-299 o texto certificado/cer/key
    public int CatTimeout     { get; set; }  // texto timeout/connection/refused
    public int CatOtros       { get; set; }  // todo lo demás

    // ── Top 10 tenants con más errores ─────────────────────────────────────
    public List<TopErrorItem> TopHoy    { get; set; } = new();
    public List<TopErrorItem> TopSemana { get; set; } = new();
    public List<TopErrorItem> TopMes    { get; set; } = new();
}

public class TopErrorItem
{
    /// <summary>Nombre del tenant (o RFC si no hay nombre)</summary>
    public string Nombre  { get; set; } = "";

    /// <summary>Error más frecuente del tenant en el periodo</summary>
    public string Meta    { get; set; } = "";

    public int    Errores { get; set; }
}
