namespace Vigma.TimbradoGateway.Models.Alertas;

public class AlertDashboardVM
{
    public List<AlertResumenTenantVM> ResumenTenants { get; set; } = new();
    public List<AlertaPorHoraVM>      PorHora        { get; set; } = new();
    public List<AlertaPorDiaVM>       PorDia         { get; set; } = new();
    public List<AlertaPorEntidadVM>   TopEntidades   { get; set; } = new();
    public AlertFiltrosDashboardVM    Filtros        { get; set; } = new();
}

public class AlertFiltrosDashboardVM
{
    public int?      TenantId   { get; set; }
    public DateTime? FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }
}

public class AlertResumenTenantVM
{
    public int       TenantId         { get; set; }
    public string    TenantNombre     { get; set; } = "";
    public int       TotalAlertas     { get; set; }
    public int       TotalEnviadas    { get; set; }
    public int       TotalFallidas    { get; set; }
    public decimal   PctError         { get; set; }
    public int       AlertasHoy       { get; set; }
    public int       EnviadasHoy      { get; set; }
    public int       FallidasHoy      { get; set; }
    public int       TokensActivos    { get; set; }
    public DateTime? UltimaAlertaUtc  { get; set; }
}

public class AlertaPorHoraVM
{
    public int      TenantId     { get; set; }
    public string   TenantNombre { get; set; } = "";
    public DateTime Fecha        { get; set; }
    public int      Hora         { get; set; }
    public string   FechaHora    { get; set; } = "";  // "2025-01-15 14:00"
    public int      Total        { get; set; }
    public int      Enviados     { get; set; }
    public int      Fallidos     { get; set; }
    public decimal  PctError     { get; set; }
}

public class AlertaPorDiaVM
{
    public int      TenantId     { get; set; }
    public string   TenantNombre { get; set; } = "";
    public DateTime Fecha        { get; set; }
    public string   FechaCorta   { get; set; } = "";  // "15/01"
    public int      Total        { get; set; }
    public int      Enviados     { get; set; }
    public int      Fallidos     { get; set; }
    public decimal  PctError     { get; set; }
}

public class AlertaPorEntidadVM
{
    public int       TenantId        { get; set; }
    public string    TenantNombre    { get; set; } = "";
    public string    EntidadId       { get; set; } = "";
    public string?   EntidadNombre   { get; set; }
    public int       Total           { get; set; }
    public int       Enviados        { get; set; }
    public int       Fallidos        { get; set; }
    public DateTime? UltimaAlertaUtc { get; set; }
}
