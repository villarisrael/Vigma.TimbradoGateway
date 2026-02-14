using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using TimbradoGateway.Contracts.Mf;
using Vigma.TimbradoGateway.DTOs;
using Vigma.TimbradoGateway.Infrastructure;
using Vigma.TimbradoGateway.Services;

namespace Vigma.TimbradoGateway.Controllers;

[ApiController]
[Route("v1/timbrar")]
public class TimbradoController : ControllerBase
{
    private readonly ITimbradoService _svc;
    private readonly TimbradoDbContext _db;

    private readonly HashSet<string> _optionalHeaderNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Cuenta",
            "Tipo",
            "IDCliente",
            "Referencia",
            "Comunidad",
            "Articulos"
        };
    private List<KeyValuePair<string, string>> _headersAdicionales = new();

    public TimbradoController(TimbradoDbContext db,ITimbradoService svc)
    {
        _svc = svc;
        _db = db;
    }

    // ✅ Health check (sin API Key)
    [HttpGet("health")]
    [AllowAnonymous] // si no usas autenticación global, puedes quitarlo
    public IActionResult Health()
    {
        return Ok(new
        {
            ok = true,
            service = "Vigma.TimbradoGateway",
            controller = "TimbradoController",
            route = "/v1/timbrar/health",
            utc = DateTime.UtcNow,
            uptimeSeconds = (long)TimeSpan.FromMilliseconds(Environment.TickCount64).TotalSeconds
        });
    }

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

    private IActionResult ApiKeyMissing()
        => Unauthorized(new { ok = false, mensaje = "Falta API Key. Envia header X-Api-Key o Authorization: Bearer {apiKey}." });

    [HttpPost("ini")]
    public async Task<IActionResult> TimbrarIni([FromBody] TimbradoIniRequest? req, CancellationToken ct)
    {
        CapturarHeadersAdicionales();
        var adicionales = _headersAdicionales
      .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);  //checa que datos adicionales trae en el header

        if (!TryGetApiKey(out var apiKey)) return ApiKeyMissing();
        if (req is null || string.IsNullOrWhiteSpace(req.ini))
            return BadRequest(new { ok = false, mensaje = "El campo 'ini' es requerido." });

        try
        {
            var resp = await _svc.TimbrarDesdeIniAsync(apiKey, req.ini,adicionales, ct);
            return Ok(resp);
        }
        catch (UnauthorizedAccessException ex) { return Unauthorized(new { ok = false, mensaje = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { ok = false, mensaje = ex.Message }); }
        catch (Exception ex) { return StatusCode(500, new { ok = false, mensaje = ex.Message }); }
    }

    [HttpPost("ini-json")]
    public async Task<IActionResult> TimbrarIniJson([FromBody] TimbradoIniRequest? req, CancellationToken ct)
    {
        CapturarHeadersAdicionales();
        var adicionales = _headersAdicionales
     .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase); //checa que datos adicionales trae en el header de la solcitud 
        if (!TryGetApiKey(out var apiKey)) return ApiKeyMissing(); 
        if (req is null || string.IsNullOrWhiteSpace(req.ini))
            return BadRequest(new { ok = false, mensaje = "El campo 'ini' es requerido." });

        try
        {
            var resp = await _svc.TimbrarDesdeIniJsonAsync(apiKey, req.ini, adicionales, ct );
            return Ok(resp);
        }
        catch (UnauthorizedAccessException ex) { return Unauthorized(new { ok = false, mensaje = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { ok = false, mensaje = ex.Message }); }
        catch (Exception ex) { return StatusCode(500, new { ok = false, mensaje = ex.Message }); }
    }

   

[HttpGet("health/all")]
public async Task<IActionResult> HealthAll(CancellationToken ct)
{

    // 1) Estado de tu API (si este método responde, tu API está viva)
    var mine = new
    {
        online = true,
        status = 200,
        url = "/v1/timbrar/health",
        utc = DateTime.UtcNow
    };

        
        object db;
        try
        {
            // Opcional: confirma conexión (ping)
            var canConnect = await _db.Database.CanConnectAsync(ct);

            // Conteo (tu prueba)
            var tenants = await _db.Tenants.CountAsync(ct);

            db = new
            {
                online = true,
                canConnect,
                tenants
            };
        }
        catch (Exception ex)
        {
            db = new
            {
                online = false,
                error = ex.Message
            };
        }



        // 2) Estado MultiFacturas (externo)
        try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        using var resp = await http.GetAsync("https://ws.multifacturas.com/api/", ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        var mf = new
        {
            online = true,
            status = (int)resp.StatusCode,
            url = "https://ws.multifacturas.com/api/",
            snippet = body.Length > 200 ? body[..200] : body
        };

        return Ok(new { mine, multifacturas = mf , database = db });
    }
    catch (Exception ex)
    {
        var mf = new
        {
            online = false,
            status = 0,
            url = "https://ws.multifacturas.com/api/",
            error = ex.Message
        };

        return Ok(new { mine, multifacturas = mf , database = db });
    }




}

 private void CapturarHeadersAdicionales()  // captura si viene cuenta, idcliente, tipo, referencia
    {
        var list = new List<KeyValuePair<string, string>>();

        foreach (var name in _optionalHeaderNames)
        {
            if (Request.Headers.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                // Si el header trae múltiples valores, los unimos por coma
                list.Add(new KeyValuePair<string, string>(name, value.ToString().Trim()));
            }
        }

        _headersAdicionales = list;
    }

}
