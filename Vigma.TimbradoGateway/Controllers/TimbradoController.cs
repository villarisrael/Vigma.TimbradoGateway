using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using Vigma.TimbradoGateway.DTOs;
using Vigma.TimbradoGateway.Services;
using Vigma.TimbradoGateway.ViewModels.Timbrados;

namespace Vigma.TimbradoGateway.Controllers;

[ApiController]
[Route("v1/timbrar")]
public class TimbradoController : ControllerBase
{
    private readonly ITimbradoService _svc;
    private readonly string _cs;
    public TimbradoController(ITimbradoService svc, IConfiguration cfg) 
        {
        _cs = cfg.GetConnectionString("MySql")!;
        _svc = svc; }

    private bool TryGetApiKey(out string apiKey)
    {
        apiKey = string.Empty;

        // Acepta X-Api-Key y X-API-KEY (case-insensitive en ASP.NET Core, pero por claridad)
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

        // Opcional: también permitir Authorization: Bearer {key}
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

    private IActionResult ApiKeyMissing()
        => Unauthorized(new { ok = false, mensaje = "Falta API Key. Envia header X-Api-Key o Authorization: Bearer {apiKey}." });

    [HttpPost("ini")]
    public async Task<IActionResult> TimbrarIni([FromBody] TimbradoIniRequest? req, CancellationToken ct)
    {
        if (!TryGetApiKey(out var apiKey)) return ApiKeyMissing();
        if (req is null || string.IsNullOrWhiteSpace(req.ini))
            return BadRequest(new { ok = false, mensaje = "El campo 'ini' es requerido." });

        try
        {
            var resp = await _svc.TimbrarDesdeIniAsync(apiKey, req.ini, ct);
            return Ok(resp);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { ok = false, mensaje = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { ok = false, mensaje = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { ok = false, mensaje = ex.Message });
        }
    }

    [HttpPost("ini-json")]
    public async Task<IActionResult> TimbrarIniJson([FromBody] TimbradoIniRequest? req, CancellationToken ct)
    {
        if (!TryGetApiKey(out var apiKey)) return ApiKeyMissing();
        if (req is null || string.IsNullOrWhiteSpace(req.ini))
            return BadRequest(new { ok = false, mensaje = "El campo 'ini' es requerido." });

        try
        {
            var resp = await _svc.TimbrarDesdeIniJsonAsync(apiKey, req.ini, ct);
            return Ok(resp);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { ok = false, mensaje = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { ok = false, mensaje = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { ok = false, mensaje = ex.Message });
        }
    }

    
}
