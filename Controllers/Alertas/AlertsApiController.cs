using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Vigma.TimbradoGateway.Models.Alertas;
using Vigma.TimbradoGateway.Services.Alertas;

namespace Vigma.TimbradoGateway.Controllers.Alertas;

// ─────────────────────────────────────────────────────────────────────────────
//  AlertsApiController
//  API pública consumida por los sistemas de los clientes.
//  Autenticación: header X-Api-Key (igual que TimbradoController)
//  Base route: /api/alerts
// ─────────────────────────────────────────────────────────────────────────────

[ApiController]
[Route("api/alerts")]
public class AlertsApiController : ControllerBase
{
    private readonly IAlertService      _alertService;
    private readonly IAlertTokenService _tokenService;

    public AlertsApiController(IAlertService alertService, IAlertTokenService tokenService)
    {
        _alertService = alertService;
        _tokenService = tokenService;
    }

    // ─── Health check (sin ApiKey) ────────────────────────────────────────────
    [HttpGet("health")]
    [AllowAnonymous]
    public IActionResult Health() => Ok(new
    {
        ok         = true,
        service    = "Vigma.TimbradoGateway",
        controller = "AlertsApiController",
        route      = "/api/alerts/health",
        utc        = DateTime.UtcNow
    });

    // ─── POST /api/alerts/tokens/register ─────────────────────────────────────
    /// <summary>
    /// Registra o actualiza el token FCM de un trabajador.
    /// Llamado desde la app SAK Alerts al iniciar sesión o al renovar token.
    /// </summary>
    [HttpPost("tokens/register")]
    public async Task<IActionResult> RegisterToken(
        [FromBody] RegisterTokenRequest? req,
        CancellationToken ct)
    {
        if (!TryGetApiKey(out var apiKey)) return ApiKeyMissing();

        if (req is null)
            return BadRequest(new { ok = false, mensaje = "Body requerido." });

        if (string.IsNullOrWhiteSpace(req.EntidadId))
            return BadRequest(new { ok = false, mensaje = "EntidadId es requerido." });

        if (string.IsNullOrWhiteSpace(req.Token))
            return BadRequest(new { ok = false, mensaje = "Token FCM es requerido." });

        try
        {
            var result = await _tokenService.RegisterAsync(apiKey, req, ct);
            return result.ok ? Ok(result) : Unauthorized(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { ok = false, mensaje = ex.Message });
        }
    }

    // ─── POST /api/alerts/send ────────────────────────────────────────────────
    /// <summary>
    /// Envía una alerta push a un trabajador.
    /// El sistema del cliente solo necesita conocer el entidadId del destinatario.
    /// </summary>
    [HttpPost("send")]
    public async Task<IActionResult> Send(
        [FromBody] SendAlertRequest? req,
        CancellationToken ct)
    {
        if (!TryGetApiKey(out var apiKey)) return ApiKeyMissing();

        if (req is null)
            return BadRequest(new { ok = false, mensaje = "Body requerido." });

        if (string.IsNullOrWhiteSpace(req.EntidadId))
            return BadRequest(new { ok = false, mensaje = "EntidadId es requerido." });

        if (string.IsNullOrWhiteSpace(req.Title))
            return BadRequest(new { ok = false, mensaje = "Title es requerido." });

        if (string.IsNullOrWhiteSpace(req.Message))
            return BadRequest(new { ok = false, mensaje = "Message es requerido." });

        try
        {
            var result = await _alertService.SendAsync(apiKey, req, ct);
            return result.ok ? Ok(result) : BadRequest(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { ok = false, mensaje = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { ok = false, mensaje = ex.Message });
        }
    }

    // ─── GET /api/alerts/logs ─────────────────────────────────────────────────
    /// <summary>
    /// Consulta los logs de alertas enviadas por el tenant autenticado.
    /// Soporta filtros por entidadId, status, fechaDesde, fechaHasta y paginación.
    /// </summary>
    [HttpGet("logs")]
    public async Task<IActionResult> GetLogs(
        [FromQuery] string?   entidadId    = null,
        [FromQuery] string?   status       = null,
        [FromQuery] DateTime? fechaDesde   = null,
        [FromQuery] DateTime? fechaHasta   = null,
        [FromQuery] int       pagina       = 1,
        [FromQuery] int       tamanoPagina = 50,
        CancellationToken ct = default)
    {
        if (!TryGetApiKey(out var apiKey)) return ApiKeyMissing();

        // Resolver tenantId desde la ApiKey para filtrar los logs
        // (reutilizamos el mismo patrón que TimbradoController)
        int? tenantId = null;
        try
        {
            tenantId = await ResolverTenantIdAsync(apiKey, ct);
        }
        catch
        {
            return Unauthorized(new { ok = false, mensaje = "API Key inválida o tenant inactivo." });
        }

        if (tamanoPagina > 200) tamanoPagina = 200;

        var filtro = new AlertLogFiltroVM
        {
            TenantId     = tenantId,
            EntidadId    = entidadId,
            Status       = status,
            FechaDesde   = fechaDesde,
            FechaHasta   = fechaHasta,
            Pagina       = pagina < 1 ? 1 : pagina,
            TamanoPagina = tamanoPagina
        };

        try
        {
            var result = await _alertService.GetLogsAsync(filtro, ct);
            return Ok(new
            {
                ok    = true,
                total = result.Total,
                pagina,
                tamanoPagina,
                logs  = result.Logs
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { ok = false, mensaje = ex.Message });
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────
    private bool TryGetApiKey(out string apiKey)
    {
        apiKey = string.Empty;

        if (Request.Headers.TryGetValue("X-Api-Key", out var v) && !string.IsNullOrWhiteSpace(v))
        {
            apiKey = v.ToString().Trim();
            return true;
        }
        if (Request.Headers.TryGetValue("X-API-KEY", out var v2) && !string.IsNullOrWhiteSpace(v2))
        {
            apiKey = v2.ToString().Trim();
            return true;
        }
        if (Request.Headers.TryGetValue("Authorization", out var auth) && !string.IsNullOrWhiteSpace(auth))
        {
            var s = auth.ToString();
            const string prefix = "Bearer ";
            if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                apiKey = s[prefix.Length..].Trim();
                if (!string.IsNullOrWhiteSpace(apiKey)) return true;
            }
        }

        return false;
    }

    private IActionResult ApiKeyMissing() =>
        Unauthorized(new { ok = false, mensaje = "Falta API Key. Envía header X-Api-Key o Authorization: Bearer {apiKey}." });

    // Resuelve el tenantId a partir del ApiKey usando el DbContext directamente
    private async Task<int> ResolverTenantIdAsync(string apiKey, CancellationToken ct)
    {
        var keyHash = Vigma.TimbradoGateway.Utils.HashHelper.Sha256(apiKey);
        var tenant  = await HttpContext.RequestServices
                                       .GetRequiredService<Vigma.TimbradoGateway.Infrastructure.TimbradoDbContext>()
                                       .Tenants
                                       .Where(t => t.ApiKeyHash == keyHash && t.Activo)
                                       .Select(t => new { t.Id })
                                       .FirstOrDefaultAsync(ct);

        if (tenant is null)
            throw new UnauthorizedAccessException("API Key inválida.");

        return tenant.Id;
    }
}
