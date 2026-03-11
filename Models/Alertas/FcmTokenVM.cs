namespace Vigma.TimbradoGateway.Models.Alertas;

public class FcmTokenIndiceVM
{
    public List<FcmTokenRowVM> Tokens   { get; set; } = new();
    public FcmTokenFiltroVM    Filtro   { get; set; } = new();
    public int                 Total    { get; set; }
    public int                 Activos  { get; set; }
    public int                 Inactivos { get; set; }
}

public class FcmTokenRowVM
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

public class FcmTokenFiltroVM
{
    public int?      TenantId      { get; set; }
    public string?   EntidadId     { get; set; }
    public string?   EntidadNombre { get; set; }
    public bool?     Activo        { get; set; }
    public DateTime? FechaDesde    { get; set; }
    public DateTime? FechaHasta    { get; set; }
    public int       Pagina        { get; set; } = 1;
    public int       TamanoPagina  { get; set; } = 50;
}
