using Microsoft.EntityFrameworkCore;
using Vigma.TimbradoGateway.Infrastructure;
using Vigma.TimbradoGateway.Models.Alertas;

namespace Vigma.TimbradoGateway.Services.Alertas;

// ─────────────────────────────────────────────────────────────────────────────
//  AlertDashboardService
//  Abastece el dashboard de alertas consultando las vistas MySQL:
//    - vw_alertas_resumen_tenant  → KPIs / tarjetas
//    - vw_alertas_por_hora        → gráfica por hora
//    - vw_alertas_por_dia         → tendencia 30 días
//    - vw_alertas_por_entidad     → top trabajadores
// ─────────────────────────────────────────────────────────────────────────────

public interface IAlertDashboardService
{
    /// <summary>Carga todo el dashboard de una sola llamada.</summary>
    Task<AlertDashboardVM> GetDashboardAsync(
        AlertFiltrosDashboardVM filtros,
        CancellationToken ct = default);

    /// <summary>Solo KPIs (tarjetas de resumen por tenant).</summary>
    Task<List<AlertResumenTenantVM>> GetResumenTenantsAsync(
        int? tenantId = null,
        CancellationToken ct = default);

    /// <summary>Mensajes agrupados por hora — para gráfica de barras.</summary>
    Task<List<AlertaPorHoraVM>> GetPorHoraAsync(
        int? tenantId,
        DateTime? fecha,
        CancellationToken ct = default);

    /// <summary>Mensajes agrupados por día — tendencia 30 días.</summary>
    Task<List<AlertaPorDiaVM>> GetPorDiaAsync(
        int? tenantId,
        CancellationToken ct = default);

    /// <summary>Top trabajadores que más reciben alertas.</summary>
    Task<List<AlertaPorEntidadVM>> GetTopEntidadesAsync(
        int? tenantId,
        int top = 10,
        CancellationToken ct = default);
}

public sealed class AlertDashboardService : IAlertDashboardService
{
    private readonly TimbradoDbContext _db;

    public AlertDashboardService(TimbradoDbContext db) => _db = db;

    // ─── Dashboard completo ───────────────────────────────────────────────────
    public async Task<AlertDashboardVM> GetDashboardAsync(
        AlertFiltrosDashboardVM filtros,
        CancellationToken ct = default)
    {
        var fecha = filtros.FechaDesde?.Date ?? DateTime.UtcNow.Date;

        var resumen     = await GetResumenTenantsAsync(filtros.TenantId, ct);
        var porHora     = await GetPorHoraAsync(filtros.TenantId, fecha, ct);
        var porDia      = await GetPorDiaAsync(filtros.TenantId, ct);
        var topEntidades = await GetTopEntidadesAsync(filtros.TenantId, top: 10, ct);

        return new AlertDashboardVM
        {
            ResumenTenants = resumen,
            PorHora        = porHora,
            PorDia         = porDia,
            TopEntidades   = topEntidades,
            Filtros        = filtros
        };
    }

    // ─── KPIs por tenant ──────────────────────────────────────────────────────
    public async Task<List<AlertResumenTenantVM>> GetResumenTenantsAsync(
        int? tenantId = null,
        CancellationToken ct = default)
    {
        var query = _db.VwAlertasResumenTenant.AsQueryable();

        if (tenantId.HasValue)
            query = query.Where(x => x.TenantId == tenantId.Value);

        return await query
            .OrderBy(x => x.TenantNombre)
            .Select(x => new AlertResumenTenantVM
            {
                TenantId        = x.TenantId,
                TenantNombre    = x.TenantNombre,
                TotalAlertas    = x.TotalAlertas,
                TotalEnviadas   = x.TotalEnviadas,
                TotalFallidas   = x.TotalFallidas,
                PctError        = x.PctError,
                AlertasHoy      = x.AlertasHoy,
                EnviadasHoy     = x.EnviadasHoy,
                FallidasHoy     = x.FallidasHoy,
                TokensActivos   = x.TokensActivos,
                UltimaAlertaUtc = x.UltimaAlertaUtc
            })
            .ToListAsync(ct);
    }

    // ─── Por hora ────────────────────────────────────────────────────────────
    public async Task<List<AlertaPorHoraVM>> GetPorHoraAsync(
        int? tenantId,
        DateTime? fecha,
        CancellationToken ct = default)
    {
        var fechaFiltro = fecha?.Date ?? DateTime.UtcNow.Date;

        var query = _db.VwAlertasPorHora
                       .Where(x => x.Fecha == fechaFiltro);

        if (tenantId.HasValue)
            query = query.Where(x => x.TenantId == tenantId.Value);

        return await query
            .OrderBy(x => x.Hora)
            .Select(x => new AlertaPorHoraVM
            {
                TenantId     = x.TenantId,
                TenantNombre = x.TenantNombre,
                Fecha        = x.Fecha,
                Hora         = x.Hora,
                FechaHora    = x.FechaHora,
                Total        = x.Total,
                Enviados     = x.Enviados,
                Fallidos     = x.Fallidos,
                PctError     = x.PctError
            })
            .ToListAsync(ct);
    }

    // ─── Por día (30 días) ───────────────────────────────────────────────────
    public async Task<List<AlertaPorDiaVM>> GetPorDiaAsync(
        int? tenantId,
        CancellationToken ct = default)
    {
        var query = _db.VwAlertasPorDia.AsQueryable();

        if (tenantId.HasValue)
            query = query.Where(x => x.TenantId == tenantId.Value);

        return await query
            .OrderBy(x => x.Fecha)
            .Select(x => new AlertaPorDiaVM
            {
                TenantId     = x.TenantId,
                TenantNombre = x.TenantNombre,
                Fecha        = x.Fecha,
                FechaCorta   = x.FechaCorta,
                Total        = x.Total,
                Enviados     = x.Enviados,
                Fallidos     = x.Fallidos,
                PctError     = x.PctError
            })
            .ToListAsync(ct);
    }

    // ─── Top entidades ────────────────────────────────────────────────────────
    public async Task<List<AlertaPorEntidadVM>> GetTopEntidadesAsync(
        int? tenantId,
        int top = 10,
        CancellationToken ct = default)
    {
        var query = _db.VwAlertasPorEntidad.AsQueryable();

        if (tenantId.HasValue)
            query = query.Where(x => x.TenantId == tenantId.Value);

        return await query
            .OrderByDescending(x => x.Total)
            .Take(top)
            .Select(x => new AlertaPorEntidadVM
            {
                TenantId        = x.TenantId,
                TenantNombre    = x.TenantNombre,
                EntidadId       = x.EntidadId,
                EntidadNombre   = x.EntidadNombre,
                Total           = x.Total,
                Enviados        = x.Enviados,
                Fallidos        = x.Fallidos,
                UltimaAlertaUtc = x.UltimaAlertaUtc
            })
            .ToListAsync(ct);
    }
}
