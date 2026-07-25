using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Vigma.TimbradoGateway.Infrastructure;
using Vigma.TimbradoGateway.Services;

namespace Vigma.TimbradoGateway.Controllers
{
    /// <summary>
    /// Portal para clientes (distribuidores).
    /// Solo pueden ver sus propios timbrados y errores de sus tenants.
    /// </summary>
    [Authorize(Roles = "Cliente")]
    public class ClienteController : Controller
    {
        private readonly TimbradoDbContext _context;
        private readonly IClienteScopeService _clienteScope;

        public ClienteController(TimbradoDbContext context, IClienteScopeService clienteScope)
        {
            _context = context;
            _clienteScope = clienteScope;
        }

        /// <summary>
        /// Dashboard del cliente con métricas generales.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Dashboard(CancellationToken ct)
        {
            try
            {
                var tenantIds = _clienteScope.GetAllowedTenantIds(User);

                if (!tenantIds.Any())
                    return View("Dashboard", new { message = "No tienes tenants asignados." });

                // Obtener cliente info
                var clienteId = _clienteScope.GetClienteId(User);
                var cliente = await _context.Clientes.FindAsync(new object[] { clienteId }, cancellationToken: ct);

                // Obtener tenants del cliente
                var tenants = await _context.Tenants
                    .Where(t => tenantIds.Contains((long)t.Id) && t.Activo)
                    .ToListAsync(ct);

                // Contar timbrados y errores
                var timbrados = await _context.TimbradoOkLogs
                    .Where(t => tenantIds.Contains(t.TenantId))
                    .CountAsync(ct);

                var errores = await _context.TimbradoErrorLogs
                    .Where(t => tenantIds.Contains(t.TenantId))
                    .CountAsync(ct);

                var model = new
                {
                    cliente = cliente?.Nombre ?? "Cliente",
                    tenantCount = tenants.Count,
                    timbradosCount = timbrados,
                    erroresCount = errores,
                    tenants = tenants.Select(t => new
                    {
                        t.Id,
                        t.Nombre,
                        t.Activo
                    })
                };

                return View(model);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>
        /// Lista de timbrados del cliente.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Timbrados(CancellationToken ct)
        {
            try
            {
                var tenantIds = _clienteScope.GetAllowedTenantIds(User);

                if (!tenantIds.Any())
                    return View(new List<object>());

                // Obtener últimos 100 timbrados
                var timbrados = await _context.TimbradoOkLogs
                    .Where(t => tenantIds.Contains(t.TenantId))
                    .OrderByDescending(t => t.created_utc)
                    .Take(100)
                    .Select(t => new
                    {
                        t.Id,
                        t.TenantId,
                        t.RfcEmisor,
                        t.Uuid,
                        FechaAlta = t.created_utc,
                        t.xmlTimbrado
                    })
                    .ToListAsync(ct);

                return View(timbrados);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>
        /// Lista de errores del cliente.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Errores(CancellationToken ct)
        {
            try
            {
                var tenantIds = _clienteScope.GetAllowedTenantIds(User);

                if (!tenantIds.Any())
                    return View(new List<object>());

                // Obtener últimos 100 errores
                var errores = await _context.TimbradoErrorLogs
                    .Where(t => tenantIds.Contains(t.TenantId))
                    .OrderByDescending(t => t.CreadoUtc)
                    .Take(100)
                    .Select(t => new
                    {
                        t.Id,
                        t.TenantId,
                        t.RfcEmisor,
                        FechaAlta = t.CreadoUtc,
                        CodigoMf = t.CodigoMfNumero,
                        MensajeMf = t.CodigoMfTexto,
                        t.JsonEnviado
                    })
                    .ToListAsync(ct);

                return View(errores);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}
