namespace Vigma.TimbradoGateway.Models.Alertas;

// ─────────────────────────────────────────────────────────────────────────────
//  Modelos EF para las vistas MySQL.
//  Registrar en TimbradoDbContext con HasNoKey().ToView("nombre_vista")
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>vw_fcm_tokens</summary>
public class VwFcmToken
{
    public int      Id                  { get; set; }
    public int      TenantId            { get; set; }
    public string   TenantNombre        { get; set; } = "";
    public string   EntidadId           { get; set; } = "";
    public string?  EntidadNombre       { get; set; }
    public string   TokenPreview        { get; set; } = "";
    public bool     Activo              { get; set; }
    public DateTime CreadoUtc           { get; set; }
    public DateTime ActualizadoUtc      { get; set; }
    public int      DiasSinActualizar   { get; set; }
}

/// <summary>vw_alert_logs</summary>
public class VwAlertLog
{
    public long     Id              { get; set; }
    public int      TenantId        { get; set; }
    public string   TenantNombre    { get; set; } = "";
    public string   EntidadId       { get; set; } = "";
    public string?  EntidadNombre   { get; set; }
    public string   Origin          { get; set; } = "";
    public string   Title           { get; set; } = "";
    public string   Message         { get; set; } = "";
    public string   Status          { get; set; } = "";
    public string?  FirebaseMsgId   { get; set; }
    public string?  ErrorDetail     { get; set; }
    public DateTime SentAt          { get; set; }
    public string   Fecha           { get; set; } = "";
    public string   Hora            { get; set; } = "";
    public bool     EnviadoOk       { get; set; }
}

/// <summary>vw_alertas_por_hora</summary>
public class VwAlertasPorHora
{
    public int      TenantId        { get; set; }
    public string   TenantNombre    { get; set; } = "";
    public DateTime Fecha           { get; set; }
    public int      Hora            { get; set; }
    public string   FechaHora       { get; set; } = "";
    public int      Total           { get; set; }
    public int      Enviados        { get; set; }
    public int      Fallidos        { get; set; }
    public decimal  PctError        { get; set; }
}

/// <summary>vw_alertas_por_dia</summary>
public class VwAlertasPorDia
{
    public int      TenantId        { get; set; }
    public string   TenantNombre    { get; set; } = "";
    public DateTime Fecha           { get; set; }
    public string   FechaCorta      { get; set; } = "";
    public int      Total           { get; set; }
    public int      Enviados        { get; set; }
    public int      Fallidos        { get; set; }
    public decimal  PctError        { get; set; }
}

/// <summary>vw_alertas_resumen_tenant</summary>
public class VwAlertasResumenTenant
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

/// <summary>vw_alertas_por_entidad</summary>
public class VwAlertasPorEntidad
{
    public int       TenantId         { get; set; }
    public string    TenantNombre     { get; set; } = "";
    public string    EntidadId        { get; set; } = "";
    public string?   EntidadNombre    { get; set; }
    public int       Total            { get; set; }
    public int       Enviados         { get; set; }
    public int       Fallidos         { get; set; }
    public DateTime? UltimaAlertaUtc  { get; set; }
}
