using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Vigma.TimbradoGateway.Infrastructure;
using Vigma.TimbradoGateway.Services;
using Vigma.TimbradoGateway.Utils;

namespace Vigma.TimbradoGateway.Controllers
{
    [Route("tenants")]
    [Authorize]
    public class TenantsController : Controller
    {
        private readonly TimbradoDbContext _db;
        private readonly IMultiFacturasSaldoClient _mfSaldo;
        private readonly CryptoService _crypto;
        private readonly IConfiguration _cfg;

        public TenantsController(
            TimbradoDbContext db,
            IMultiFacturasSaldoClient mfSaldo,
            CryptoService crypto,
            IConfiguration cfg)
        {
            _db = db;
            _mfSaldo = mfSaldo;
            _crypto = crypto;
            _cfg = cfg;
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RotarApiKey(long id, CancellationToken ct)
        {
            var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (tenant == null) return NotFound();

            // Decide prefijo LIVE/TEST:
            // Opción A: live si PacProduccion, test si no
            var newKey = tenant.PacProduccion
                ? ApiKeyGenerator.GenerateLiveKey()
                : ApiKeyGenerator.GenerateTestKey();

            // Guardar hash + last4 + rotated
            tenant.ApiKeyHash = ApiKeyGenerator.Hash(newKey);
            tenant.ApiKeyLast4 = ApiKeyGenerator.Last4(newKey);
            tenant.ApiKeyRotatedUtc = DateTime.UtcNow;

            // Opcional: guardar cifrada (NO se vuelve a mostrar)
            // Si no usas ApiKeyEnc, quita esta línea.
            tenant.ApiKeyEnc = _crypto.EncryptToBase64(newKey);

            await _db.SaveChangesAsync(ct);

            // Mostrar solo una vez
            TempData["NewApiKey"] = newKey;
            TempData["TenantId"] = tenant.Id.ToString();

            return RedirectToAction(nameof(ApiKeyRotada), new { id = tenant.Id });
        }

        /// <summary>
        /// POST /tenants/{id}/rotar-api-key-json
        /// Genera nueva API Key y devuelve JSON (para llamadas AJAX desde el modal)
        /// </summary>
        [HttpPost("{id:long}/rotar-api-key-json")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RotarApiKeyJson(long id, CancellationToken ct)
        {
            try
            {
                var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
                if (tenant == null)
                    return NotFound(new { ok = false, mensaje = "Tenant no encontrado." });

                // Generar nueva API Key
                var newKey = tenant.PacProduccion
                    ? ApiKeyGenerator.GenerateLiveKey()
                    : ApiKeyGenerator.GenerateTestKey();

                // Guardar hash + last4 + rotated
                tenant.ApiKeyHash = ApiKeyGenerator.Hash(newKey);
                tenant.ApiKeyLast4 = ApiKeyGenerator.Last4(newKey);
                tenant.ApiKeyRotatedUtc = DateTime.UtcNow;
                tenant.ApiKeyEnc = _crypto.EncryptToBase64(newKey);

                await _db.SaveChangesAsync(ct);

                return Ok(new
                {
                    ok = true,
                    mensaje = "API Key regenerada correctamente.",
                    newApiKey = newKey,
                    tenantId = tenant.Id,
                    tenantNombre = tenant.Nombre,
                    ambiente = tenant.PacProduccion ? "Producción" : "Pruebas"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { ok = false, mensaje = $"Error al regenerar la API Key: {ex.Message}" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> ApiKeyRotada(long id, CancellationToken ct)
        {
            // Solo para mostrar nombre y que exista
            var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
            if (tenant == null) return NotFound();

            // Si ya se consumió TempData (refresh / link directo), será null (correcto)
            ViewBag.NewApiKey = TempData["NewApiKey"] as string;

            return View(tenant);
        }

        /// <summary>
        /// Devuelve datos de credenciales del tenant (con password descifrado) para el panel lateral del Index.
        /// GET /tenants/{id}/detalles
        /// </summary>
        [HttpGet("{id:long}/detalles")]
        public async Task<IActionResult> Detalles(long id, CancellationToken ct)
        {
            var tenant = await _db.Tenants.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == id, ct);

            if (tenant == null) return NotFound(new { ok = false });

            string? pacPassword = null;
            if (!string.IsNullOrWhiteSpace(tenant.PacPasswordEnc))
            {
                try { pacPassword = _crypto.DecryptFromBase64(tenant.PacPasswordEnc); }
                catch { /* se deja null si no se puede descifrar */ }
            }

            return Ok(new
            {
                id = tenant.Id,
                nombre = tenant.Nombre,
                activo = tenant.Activo,
                logoPath = tenant.LogoPath,
                apiKeyMasked = $"tg_live_************************{tenant.ApiKeyLast4}",
                pacUsuario = tenant.PacUsuario,
                pacPassword,
                pacProduccion = tenant.PacProduccion,
                pacApikeyFacturalo = tenant.PacApikeyFacturalo,
                pacApikeyFacturaloTest = tenant.PacApikeyFacturaloTest,
                pacProveedor = tenant.PacProveedor
            });
        }

        // GET /tenants/saldo-timbres?tenantId=123
        [HttpGet("saldo-timbres")]
        public async Task<IActionResult> SaldoTimbres([FromQuery] long tenantId, CancellationToken ct)
        {
            try
            {
                var tenant = await _db.Tenants
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Id == tenantId && t.Activo, ct);

                if (tenant == null)
                    return NotFound(new { ok = false, mensaje = "Tenant no encontrado o inactivo." });

                if (string.IsNullOrWhiteSpace(tenant.PacUsuario) || string.IsNullOrWhiteSpace(tenant.PacPasswordEnc))
                    return BadRequest(new { ok = false, mensaje = "El tenant no tiene credenciales PAC configuradas." });

                string pacPassword;
                try
                {
                    pacPassword = _crypto.DecryptFromBase64(tenant.PacPasswordEnc);
                }
                catch
                {
                    return StatusCode(500, new { ok = false, mensaje = "No se pudo desencriptar pac_password_enc." });
                }

                // URL segun ambiente
                var urlProd = _cfg["MultiFacturas:UrlWsProd"];
                var urlTest = _cfg["MultiFacturas:UrlWsTest"];
                var urlWs = tenant.PacProduccion ? urlProd : urlTest;

                if (string.IsNullOrWhiteSpace(urlWs))
                    return StatusCode(500, new { ok = false, mensaje = "Falta configurar MultiFacturas:UrlWsProd/UrlWsTest." });

                // OJO: tu MultiFacturasSaldoClient arma envelope con <rfc> y <clave>
                // Si tu 'PacUsuario' es el RFC y 'pacPassword' es la clave MF: OK.
                var resp = await _mfSaldo.ConsultarSaldoAsync(urlWs, tenant.PacUsuario, pacPassword, ct);

                // El PAC puede responder "OK" (codigo 0) pero sin mandar el dato de
                // saldo (xsi:nil="true"). Si pasa eso, no lo mostramos como 0 real.
                var mensaje = resp.Mensaje;
                if (resp.Ok && resp.Saldo == null)
                    mensaje = "MultiFacturas respondió OK pero no envió el dato de saldo. Es un problema de su servicio, contacta a su soporte.";

                return Ok(new
                {
                    ok = resp.Ok,
                    codigo = resp.Codigo,
                    mensaje,
                    saldo = resp.Saldo,
                    tenantId = tenant.Id,
                    tenantNombre = tenant.Nombre
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { ok = false, mensaje = ex.Message });
            }
        }
    }
}