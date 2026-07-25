using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Vigma.TimbradoGateway.Infrastructure;

namespace Vigma.TimbradoGateway.Pages.Monitor.Tenants;

[Authorize]
public class CaptureFacturaloKeyModel : PageModel
{
    private readonly TimbradoDbContext _db;

    public CaptureFacturaloKeyModel(TimbradoDbContext db)
    {
        _db = db;
    }

    // ── Datos para la vista ───────────────────────────────────────────────────
    public int TenantId { get; private set; }
    public string TenantNombre { get; private set; } = "";
    public string? PacProveedorActual { get; private set; }
    public bool PacProduccion { get; private set; }

    public string? ApiKeyProdMasked { get; private set; }
    public string? ApiKeyTestMasked { get; private set; }
    public bool TieneApiKeyProd { get; private set; }
    public bool TieneApiKeyTest { get; private set; }

    /// <summary>True si el tenant tiene la apikey del ambiente que está activo (pac_produccion).</summary>
    public bool TieneApiKeyDelAmbienteActivo =>
        PacProduccion ? TieneApiKeyProd : TieneApiKeyTest;

    /// <summary>Etiqueta amigable del ambiente activo.</summary>
    public string AmbienteActivoLabel => PacProduccion ? "PRODUCCIÓN" : "PRUEBAS";

    // ── Bindings de formulario ────────────────────────────────────────────────
    [BindProperty] public string? NuevaApiKeyProd { get; set; }
    [BindProperty] public string? NuevaApiKeyTest { get; set; }
    [BindProperty] public bool ActivarFacturalo { get; set; }

    [TempData] public string? MensajeOk { get; set; }
    [TempData] public string? MensajeError { get; set; }

    // ── GET ───────────────────────────────────────────────────────────────────
    public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
    {
        var tenant = await _db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        if (tenant is null) return NotFound();

        TenantId = tenant.Id;
        TenantNombre = tenant.Nombre ?? "";
        PacProveedorActual = tenant.PacProveedor;
        PacProduccion = tenant.PacProduccion;

        TieneApiKeyProd = !string.IsNullOrWhiteSpace(tenant.PacApikeyFacturalo);
        TieneApiKeyTest = !string.IsNullOrWhiteSpace(tenant.PacApikeyFacturaloTest);

        if (TieneApiKeyProd) ApiKeyProdMasked = Enmascarar(tenant.PacApikeyFacturalo!);
        if (TieneApiKeyTest) ApiKeyTestMasked = Enmascarar(tenant.PacApikeyFacturaloTest!);

        ActivarFacturalo = string.Equals(tenant.PacProveedor, "facturalo",
            StringComparison.OrdinalIgnoreCase);

        return Page();
    }

    // ── POST GuardarProd ──────────────────────────────────────────────────────
    public async Task<IActionResult> OnPostGuardarProdAsync(int id, CancellationToken ct)
        => await GuardarApiKeyAsync(id, NuevaApiKeyProd, esProd: true, ct);

    // ── POST GuardarTest ──────────────────────────────────────────────────────
    public async Task<IActionResult> OnPostGuardarTestAsync(int id, CancellationToken ct)
        => await GuardarApiKeyAsync(id, NuevaApiKeyTest, esProd: false, ct);

    private async Task<IActionResult> GuardarApiKeyAsync(int id, string? raw, bool esProd, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return NotFound();

        var key = (raw ?? "").Trim();
        var label = esProd ? "Producción" : "Pruebas";

        if (string.IsNullOrWhiteSpace(key))
        {
            MensajeError = $"Ingresa la API Key de FacturaLO PLUS ({label}).";
            return RedirectToPage(new { id });
        }

        if (key.Length is < 16 or > 64)
        {
            MensajeError = $"La API Key ({label}) debe tener entre 16 y 64 caracteres.";
            return RedirectToPage(new { id });
        }

        if (esProd) tenant.PacApikeyFacturalo = key;
        else        tenant.PacApikeyFacturaloTest = key;

        // Si pidió activar facturalo y ahora ya tiene la apikey del ambiente actual, lo activamos.
        if (ActivarFacturalo)
        {
            var puedeActivar = tenant.PacProduccion
                ? !string.IsNullOrWhiteSpace(tenant.PacApikeyFacturalo)
                : !string.IsNullOrWhiteSpace(tenant.PacApikeyFacturaloTest);

            if (puedeActivar) tenant.PacProveedor = "facturalo";
        }

        tenant.actualizado_utc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        MensajeOk = $"API Key ({label}) guardada correctamente.";
        return RedirectToPage(new { id });
    }

    // ── POST EliminarProd ─────────────────────────────────────────────────────
    public async Task<IActionResult> OnPostEliminarProdAsync(int id, CancellationToken ct)
        => await EliminarApiKeyAsync(id, esProd: true, ct);

    // ── POST EliminarTest ─────────────────────────────────────────────────────
    public async Task<IActionResult> OnPostEliminarTestAsync(int id, CancellationToken ct)
        => await EliminarApiKeyAsync(id, esProd: false, ct);

    private async Task<IActionResult> EliminarApiKeyAsync(int id, bool esProd, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return NotFound();

        var label = esProd ? "Producción" : "Pruebas";

        if (esProd) tenant.PacApikeyFacturalo = null;
        else        tenant.PacApikeyFacturaloTest = null;

        // Si el ambiente activo era el que acabamos de borrar y el proveedor es facturalo,
        // regresamos a multifacturas para evitar dejar el tenant timbrando sin credenciales.
        var apikeyDelAmbienteActivoAhora = tenant.PacProduccion
            ? tenant.PacApikeyFacturalo
            : tenant.PacApikeyFacturaloTest;

        if (string.IsNullOrWhiteSpace(apikeyDelAmbienteActivoAhora) &&
            string.Equals(tenant.PacProveedor, "facturalo", StringComparison.OrdinalIgnoreCase))
        {
            tenant.PacProveedor = "multifacturas";
            MensajeOk = $"API Key ({label}) eliminada. Como era la del ambiente activo, " +
                        $"el proveedor se regresó a MultiFacturas.";
        }
        else
        {
            MensajeOk = $"API Key ({label}) eliminada.";
        }

        tenant.actualizado_utc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return RedirectToPage(new { id });
    }

    // ── POST CambiarProveedor ─────────────────────────────────────────────────
    // proveedor: "multifacturas" | "facturalo"
    public async Task<IActionResult> OnPostCambiarProveedorAsync(int id, string proveedor, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return NotFound();

        var nuevo = (proveedor ?? "").Trim().ToLowerInvariant();
        if (nuevo is not ("multifacturas" or "facturalo"))
        {
            MensajeError = "Proveedor inválido. Valores permitidos: multifacturas, facturalo.";
            return RedirectToPage(new { id });
        }

        if (nuevo == "facturalo")
        {
            var apikeyActiva = tenant.PacProduccion
                ? tenant.PacApikeyFacturalo
                : tenant.PacApikeyFacturaloTest;

            if (string.IsNullOrWhiteSpace(apikeyActiva))
            {
                MensajeError = $"No se puede activar FacturaLO porque el tenant no tiene API Key " +
                               $"para el ambiente {(tenant.PacProduccion ? "PRODUCCIÓN" : "PRUEBAS")}.";
                return RedirectToPage(new { id });
            }
        }

        if (string.Equals(tenant.PacProveedor, nuevo, StringComparison.OrdinalIgnoreCase))
        {
            MensajeOk = $"El proveedor activo ya era {nuevo}.";
            return RedirectToPage(new { id });
        }

        tenant.PacProveedor = nuevo;
        tenant.actualizado_utc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        MensajeOk = nuevo == "facturalo"
            ? "Proveedor activo cambiado a FacturaLO PLUS."
            : "Proveedor activo cambiado a MultiFacturas.";

        return RedirectToPage(new { id });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static string Enmascarar(string key)
    {
        var last4 = key.Length >= 4 ? key[^4..] : key;
        return $"{new string('•', Math.Max(0, key.Length - 4))}{last4}";
    }
}
