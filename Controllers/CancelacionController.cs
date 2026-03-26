using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vigma.TimbradoGateway.DTOs;
using Vigma.TimbradoGateway.Services;

namespace Vigma.TimbradoGateway.Controllers;

[ApiController]
[Route("v1/cancelar")]
public class CancelacionController : ControllerBase
{
    private readonly ICancelacionService _svc;

    public CancelacionController(ICancelacionService svc)
    {
        _svc = svc;
    }

    // ✅ Health check
    [HttpGet("health")]
    [AllowAnonymous]
    public IActionResult Health()
    {
        return Ok(new
        {
            ok = true,
            service = "Vigma.TimbradoGateway",
            controller = "CancelacionController",
            route = "/v1/cancelar/health",
            utc = DateTime.UtcNow
        });
    }

    /// <summary>
    /// POST /v1/cancelar
    /// Header: X-Api-Key: {apiKey}
    /// Body:
    /// {
    ///   "rfcEmisor": "ABC010101XYZ",
    ///   "uuid": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
    ///   "motivo": "02",
    ///   "uuidSustitucion": null
    /// }
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Cancelar(
        [FromBody] CancelacionRequest? req,
        CancellationToken ct)
    {
        if (!TryGetApiKey(out var apiKey))
            return Unauthorized(new { ok = false, mensaje = "Falta API Key. Envía header X-Api-Key o Authorization: Bearer {apiKey}." });

        if (req is null)
            return BadRequest(new { ok = false, mensaje = "El body de la petición es requerido." });

        try
        {
            var resp = await _svc.CancelarAsync(apiKey, req, ct);
            return Ok(resp);
        }
        catch (UnauthorizedAccessException ex) { return Unauthorized(new { ok = false, mensaje = ex.Message }); }
        catch (ArgumentException ex)           { return BadRequest(new { ok = false, mensaje = ex.Message }); }
        catch (Exception ex)                   { return StatusCode(500, new { ok = false, mensaje = ex.Message }); }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

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
                apiKey = s.Substring(prefix.Length).Trim();
                if (!string.IsNullOrWhiteSpace(apiKey)) return true;
            }
        }

        return false;
    }
}
