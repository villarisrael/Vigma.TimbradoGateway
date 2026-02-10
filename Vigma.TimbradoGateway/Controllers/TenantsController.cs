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

                return Ok(new
                {
                    ok = resp.Ok,
                    codigo = resp.Codigo,
                    mensaje = resp.Mensaje,
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