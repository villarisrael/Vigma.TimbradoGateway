namespace Vigma.TimbradoGateway.Models.Alertas;

public class AlertLogIndiceVM
{
    public List<AlertLogRowVM> Logs   { get; set; } = new();
    public AlertLogFiltroVM    Filtro { get; set; } = new();
    public int                 Total  { get; set; }
}

public class AlertLogRowVM
{
    public long     Id            { get; set; }
    public int      TenantId      { get; set; }
    public string   TenantNombre  { get; set; } = "";
    public string   EntidadId     { get; set; } = "";
    public string?  EntidadNombre { get; set; }
    public string   Origin        { get; set; } = "";
    public string   Title         { get; set; } = "";
    public string   Message       { get; set; } = "";
    public string   Status        { get; set; } = "";
    public string?  ErrorDetail   { get; set; }
    public string?  FirebaseMsgId { get; set; }
    public DateTime SentAt        { get; set; }
    public string   Fecha         { get; set; } = "";
    public string   Hora          { get; set; } = "";
    public bool     EnviadoOk     { get; set; }
}

public class AlertLogFiltroVM
{
    public int?      TenantId      { get; set; }
    public string?   EntidadId     { get; set; }
    public string?   EntidadNombre { get; set; }
    public string?   Origin        { get; set; }
    public string?   Status        { get; set; }  // "sent" | "failed" | null = todos
    public DateTime? FechaDesde    { get; set; }
    public DateTime? FechaHasta    { get; set; }
    public int       Pagina        { get; set; } = 1;
    public int       TamanoPagina  { get; set; } = 50;
}
