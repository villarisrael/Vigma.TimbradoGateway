using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Vigma.TimbradoGateway.Infrastructure;
using Vigma.TimbradoGateway.Services;

namespace Vigma.TimbradoGateway.Pages.Monitor.Tenants;

[Authorize]
public class DetalleModel : PageModel
{
    private readonly TimbradoDbContext _db;
    private readonly CryptoService _crypto;

    public DetalleModel(TimbradoDbContext db, CryptoService crypto)
    {
        _db = db;
        _crypto = crypto;
    }

    // ── Propiedades expuestas a la vista ──────────────────────────────────────
    public long   Id              { get; set; }
    public string Nombre          { get; set; } = "";
    public bool   Activo          { get; set; }
    public bool   PacProduccion   { get; set; }
    public string? LogoPath       { get; set; }
    public string ApiKeyMasked    { get; set; } = "";

    public string? PacUsuario          { get; set; }
    public string? PacPassword         { get; set; }   // descifrado
    public string? PacApikeyFacturalo      { get; set; }
    public string? PacApikeyFacturaloTest  { get; set; }
    public string  PacProveedor        { get; set; } = "multifacturas";

    /// <summary>API Key de FacturaLO según el ambiente activo.</summary>
    public string? FlKeyActiva => PacProduccion ? PacApikeyFacturalo : PacApikeyFacturaloTest;
    public string  FlKeyLabel  => PacProduccion ? "Producción" : "Pruebas / Sandbox";

    [TempData] public string? SuccessMessage { get; set; }
    [TempData] public string? ErrorMessage   { get; set; }

    // ── POST CambiarProveedor ─────────────────────────────────────────────────
    /// <summary>
    /// Alterna el proveedor PAC activo entre MultiFacturas y FacturaLO PLUS.
    /// Botón de emergencia — un clic, sin salir de la página de detalle.
    /// </summary>
    public async Task<IActionResult> OnPostCambiarProveedorAsync(long id, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return NotFound();

        var esFacturalo = string.Equals(tenant.PacProveedor, "facturalo",
            StringComparison.OrdinalIgnoreCase);

        var nuevoProveedor = esFacturalo ? "multifacturas" : "facturalo";

        // Si se intenta activar FacturaLO, verificar que exista la API Key del ambiente activo
        if (nuevoProveedor == "facturalo")
        {
            var apikeyActiva = tenant.PacProduccion
                ? tenant.PacApikeyFacturalo
                : tenant.PacApikeyFacturaloTest;

            if (string.IsNullOrWhiteSpace(apikeyActiva))
            {
                ErrorMessage = $"No se puede activar FacturaLO: falta la API Key para " +
                               $"{(tenant.PacProduccion ? "PRODUCCIÓN" : "PRUEBAS")}. " +
                               $"Configúrala primero en «API Key FacturaLO».";
                return RedirectToPage(new { id });
            }
        }

        tenant.PacProveedor   = nuevoProveedor;
        tenant.actualizado_utc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        SuccessMessage = nuevoProveedor == "facturalo"
            ? "⚡ Proveedor cambiado a FacturaLO PLUS."
            : "✅ Proveedor regresado a MultiFacturas.";

        return RedirectToPage(new { id });
    }

    // ── POST CambiarAmbiente ──────────────────────────────────────────────────
    /// <summary>
    /// Alterna pac_produccion entre Producción y Pruebas/Sandbox.
    /// Si el tenant usa FacturaLO y no tiene API Key para el nuevo ambiente,
    /// regresa automáticamente a MultiFacturas para evitar dejar al tenant sin timbrar.
    /// </summary>
    public async Task<IActionResult> OnPostCambiarAmbienteAsync(long id, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return NotFound();

        var nuevoAmbienteProd = !tenant.PacProduccion;   // toggle
        var nuevoLabel = nuevoAmbienteProd ? "PRODUCCIÓN" : "PRUEBAS";

        // Si usa FacturaLO, verificar que tenga API Key para el ambiente destino
        if (string.Equals(tenant.PacProveedor, "facturalo", StringComparison.OrdinalIgnoreCase))
        {
            var apikeyDestino = nuevoAmbienteProd
                ? tenant.PacApikeyFacturalo
                : tenant.PacApikeyFacturaloTest;

            if (string.IsNullOrWhiteSpace(apikeyDestino))
            {
                // Cambiamos el ambiente pero regresamos a MF para no dejar al tenant sin PAC
                tenant.PacProduccion  = nuevoAmbienteProd;
                tenant.PacProveedor   = "multifacturas";
                tenant.actualizado_utc = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);

                SuccessMessage = $"Ambiente cambiado a {nuevoLabel}. " +
                                 $"FacturaLO no tiene API Key para este ambiente, " +
                                 $"el PAC activo se regresó a MultiFacturas automáticamente.";
                return RedirectToPage(new { id });
            }
        }

        tenant.PacProduccion  = nuevoAmbienteProd;
        tenant.actualizado_utc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        SuccessMessage = nuevoAmbienteProd
            ? "🚀 Ambiente cambiado a PRODUCCIÓN — timbrado real ante el SAT."
            : "🧪 Ambiente cambiado a PRUEBAS — Sandbox, sin efectos fiscales.";

        return RedirectToPage(new { id });
    }

    // ── GET /Monitor/Tenants/Detalle/{id} ─────────────────────────────────────
    public async Task<IActionResult> OnGetAsync(long id, CancellationToken ct)
    {
        var tenant = await _db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        if (tenant == null) return NotFound();

        Id            = tenant.Id;
        Nombre        = tenant.Nombre ?? "";
        Activo        = tenant.Activo;
        PacProduccion = tenant.PacProduccion;
        LogoPath      = tenant.LogoPath;
        PacUsuario    = tenant.PacUsuario;
        PacProveedor  = tenant.PacProveedor ?? "multifacturas";
        PacApikeyFacturalo     = tenant.PacApikeyFacturalo;
        PacApikeyFacturaloTest = tenant.PacApikeyFacturaloTest;
        ApiKeyMasked  = $"tg_live_************************{tenant.ApiKeyLast4}";

        if (!string.IsNullOrWhiteSpace(tenant.PacPasswordEnc))
        {
            try   { PacPassword = _crypto.DecryptFromBase64(tenant.PacPasswordEnc); }
            catch { PacPassword = null; }
        }

        return Page();
    }
}
