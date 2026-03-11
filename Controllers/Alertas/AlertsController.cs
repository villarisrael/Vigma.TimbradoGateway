using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Vigma.TimbradoGateway.Infrastructure;
using Vigma.TimbradoGateway.Models.Alertas;
using Vigma.TimbradoGateway.Services.Alertas;
using Microsoft.EntityFrameworkCore;

namespace Vigma.TimbradoGateway.Controllers.Alertas;

// ─────────────────────────────────────────────────────────────────────────────
//  AlertsController
//  Monitor interno — vistas Razor para el equipo Vigma.
//  Autenticación: cookies (igual que TimbradosController)
//  Vistas esperadas en: Views/Alerts/
// ─────────────────────────────────────────────────────────────────────────────

[Authorize]
public class AlertsController : Controller
{
    private readonly IAlertService          _alertService;
    private readonly IAlertTokenService     _tokenService;
    private readonly IAlertDashboardService _dashboardService;
    private readonly TimbradoDbContext      _db;

    public AlertsController(
        IAlertService          alertService,
        IAlertTokenService     tokenService,
        IAlertDashboardService dashboardService,
        TimbradoDbContext      db)
    {
        _alertService     = alertService;
        _tokenService     = tokenService;
        _dashboardService = dashboardService;
        _db               = db;
    }

    // ─── GET /Alerts/Tokens ───────────────────────────────────────────────────
    /// <summary>Índice de tokens FCM registrados con filtros y paginación.</summary>
    [HttpGet]
    public async Task<IActionResult> Tokens(
        int?      tenantId      = null,
        string?   entidadId     = null,
        string?   entidadNombre = null,
        bool?     activo        = null,
        DateTime? fechaDesde    = null,
        DateTime? fechaHasta    = null,
        int       pagina        = 1,
        int       tamanoPagina  = 50,
        CancellationToken ct    = default)
    {
        pagina       = pagina < 1 ? 1 : pagina;
        tamanoPagina = tamanoPagina <= 0 ? 50 : tamanoPagina;
        if (tamanoPagina > 200) tamanoPagina = 200;

        var filtro = new FcmTokenFiltroVM
        {
            TenantId      = tenantId,
            EntidadId     = entidadId,
            EntidadNombre = entidadNombre,
            Activo        = activo,
            FechaDesde    = fechaDesde,
            FechaHasta    = fechaHasta,
            Pagina        = pagina,
            TamanoPagina  = tamanoPagina
        };

        var vm = await _tokenService.GetIndiceAsync(filtro, ct);

        ViewBag.Tenants = await ObtenerTenantsSelectAsync(tenantId, ct);

        return View(vm);  // Views/Alerts/Tokens.cshtml
    }

    // ─── GET /Alerts/Logs ─────────────────────────────────────────────────────
    /// <summary>Índice de logs de alertas con filtros y paginación.</summary>
    [HttpGet]
    public async Task<IActionResult> Logs(
        int?      tenantId      = null,
        string?   entidadId     = null,
        string?   entidadNombre = null,
        string?   origin        = null,
        string?   status        = null,
        DateTime? fechaDesde    = null,
        DateTime? fechaHasta    = null,
        int       pagina        = 1,
        int       tamanoPagina  = 50,
        CancellationToken ct    = default)
    {
        pagina       = pagina < 1 ? 1 : pagina;
        tamanoPagina = tamanoPagina <= 0 ? 50 : tamanoPagina;
        if (tamanoPagina > 200) tamanoPagina = 200;

        var filtro = new AlertLogFiltroVM
        {
            TenantId      = tenantId,
            EntidadId     = entidadId,
            EntidadNombre = entidadNombre,
            Origin        = origin,
            Status        = status,
            FechaDesde    = fechaDesde,
            FechaHasta    = fechaHasta,
            Pagina        = pagina,
            TamanoPagina  = tamanoPagina
        };

        var vm = await _alertService.GetLogsAsync(filtro, ct);

        ViewBag.Tenants = await ObtenerTenantsSelectAsync(tenantId, ct);

        return View(vm);  // Views/Alerts/Logs.cshtml
    }

    // ─── GET /Alerts/Dashboard ────────────────────────────────────────────────
    /// <summary>Dashboard principal con KPIs, gráficas y top entidades.</summary>
    [HttpGet]
    public async Task<IActionResult> Dashboard(
        int?      tenantId   = null,
        DateTime? fechaDesde = null,
        DateTime? fechaHasta = null,
        CancellationToken ct = default)
    {
        var filtros = new AlertFiltrosDashboardVM
        {
            TenantId   = tenantId,
            FechaDesde = fechaDesde,
            FechaHasta = fechaHasta
        };

        var vm = await _dashboardService.GetDashboardAsync(filtros, ct);

        ViewBag.Tenants = await ObtenerTenantsSelectAsync(tenantId, ct);

        return View(vm);  // Views/Alerts/Dashboard.cshtml
    }

    // ─── GET /Alerts/Dashboard/PorHora (AJAX) ────────────────────────────────
    /// <summary>
    /// JSON para gráfica de barras — mensajes por hora del día.
    /// Llamado via fetch/axios desde el Dashboard.
    /// </summary>
    [HttpGet("Alerts/Dashboard/PorHora")]
    public async Task<IActionResult> DashboardPorHora(
        int?      tenantId = null,
        DateTime? fecha    = null,
        CancellationToken ct = default)
    {
        try
        {
            var data = await _dashboardService.GetPorHoraAsync(tenantId, fecha, ct);
            return Json(new { ok = true, data });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    // ─── GET /Alerts/Dashboard/PorDia (AJAX) ─────────────────────────────────
    /// <summary>
    /// JSON para gráfica de línea — tendencia de últimos 30 días.
    /// Llamado via fetch/axios desde el Dashboard.
    /// </summary>
    [HttpGet("Alerts/Dashboard/PorDia")]
    public async Task<IActionResult> DashboardPorDia(
        int? tenantId = null,
        CancellationToken ct = default)
    {
        try
        {
            var data = await _dashboardService.GetPorDiaAsync(tenantId, ct);
            return Json(new { ok = true, data });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    // ─── GET /Alerts/Dashboard/TopEntidades (AJAX) ────────────────────────────
    /// <summary>
    /// JSON para tabla/gráfica de top trabajadores.
    /// Llamado via fetch/axios desde el Dashboard.
    /// </summary>
    [HttpGet("Alerts/Dashboard/TopEntidades")]
    public async Task<IActionResult> DashboardTopEntidades(
        int? tenantId = null,
        int  top      = 10,
        CancellationToken ct = default)
    {
        try
        {
            var data = await _dashboardService.GetTopEntidadesAsync(tenantId, top, ct);
            return Json(new { ok = true, data });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────
    private async Task<List<SelectListItem>> ObtenerTenantsSelectAsync(
        int? seleccionado,
        CancellationToken ct)
    {
        var tenants = await _db.Tenants
                               .Where(t => t.Activo)
                               .OrderBy(t => t.Nombre)
                               .Select(t => new { t.Id, t.Nombre })
                               .ToListAsync(ct);

        var list = new List<SelectListItem>
        {
            new() { Text = "Todos", Value = "", Selected = !seleccionado.HasValue }
        };

        list.AddRange(tenants.Select(t => new SelectListItem
        {
            Text     = t.Nombre,
            Value    = t.Id.ToString(),
            Selected = seleccionado.HasValue && seleccionado.Value == t.Id
        }));

        return list;
    }


    [HttpGet("Alerts/Dashboard/ResumenWidget")]
    public async Task<IActionResult> DashboardResumenWidget(CancellationToken ct = default)
    {
        try
        {
            var resumen = await _dashboardService.GetResumenTenantsAsync(tenantId: null, ct);
            return Json(new { ok = true, resumen });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

}
