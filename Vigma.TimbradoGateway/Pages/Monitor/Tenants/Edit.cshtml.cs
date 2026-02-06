using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Vigma.TimbradoGateway.Infrastructure;
using Vigma.TimbradoGateway.Services;
using Vigma.TimbradoGateway.Utils;

namespace Vigma.TimbradoGateway.Pages.Monitor.Tenants;

public class EditModel : PageModel
{
    private readonly TimbradoDbContext _db;
    private readonly CryptoService _crypto;

    public EditModel(TimbradoDbContext db, CryptoService crypto)
    {
        _db = db;
        _crypto = crypto;
    }

    [BindProperty]
    public TenantInput Input { get; set; } = new();

    public string ApiKeyMasked { get; set; } = "";

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken ct)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant == null) return NotFound();

        Input.Id = tenant.Id;
        Input.Nombre = tenant.Nombre ?? "";
        Input.Activo = tenant.Activo;
        Input.PacUsuario = tenant.PacUsuario ?? "";
        Input.PacProduccion = tenant.PacProduccion;

        // Enmascarado: prefijo + ***** + last4
        var prefix = (tenant.ApiKeyLast4 ?? "").Length > 0 && (tenant.ApiKeyEnc ?? "").Contains("tg_test_")
            ? "tg_test_"
            : "tg_live_";
        // Si no quieres depender de ApiKeyEnc, usa solo tg_live_ fijo:
        prefix = (tenant.ApiKeyLast4 ?? "").Length > 0 && (tenant.ApiKeyHash ?? "").StartsWith("test")
            ? "tg_test_"
            : "tg_live_"; // (fallback)

        ApiKeyMasked = $"{prefix}************************{tenant.ApiKeyLast4}";

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            // Recargar máscara para render correcto
            await LoadMaskedAsync(Input.Id, ct);
            return Page();
        }

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == Input.Id, ct);
        if (tenant == null) return NotFound();

        tenant.Nombre = Input.Nombre?.Trim() ?? "";
        tenant.Activo = Input.Activo;

        tenant.PacUsuario = (Input.PacUsuario ?? "").Trim();
        tenant.PacProduccion = Input.PacProduccion;

        // Si el usuario deja vacío, se conserva
        if (!string.IsNullOrWhiteSpace(Input.PacPassword))
        {
            tenant.PacPasswordEnc = _crypto.EncryptToBase64(Input.PacPassword.Trim());
        }

        tenant.actualizado_utc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        SuccessMessage = "Cambios guardados.";
        return RedirectToPage("./Edit", new { id = tenant.Id });
    }

    public async Task<IActionResult> OnPostRotarAsync(CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == Input.Id, ct);
        if (tenant == null) return NotFound();

        // Generar tg_live o tg_test según PacProduccion
        var newKey = tenant.PacProduccion
            ? ApiKeyGenerator.GenerateLiveKey()
            : ApiKeyGenerator.GenerateTestKey();

        tenant.ApiKeyHash = ApiKeyGenerator.Hash(newKey);
        tenant.ApiKeyLast4 = ApiKeyGenerator.Last4(newKey);
        tenant.ApiKeyRotatedUtc = DateTime.UtcNow;

        // Opcional: guardar cifrada (NO se vuelve a mostrar)
        tenant.ApiKeyEnc = _crypto.EncryptToBase64(newKey);

        tenant.actualizado_utc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        TempData["NewApiKey"] = newKey;
        return RedirectToPage("./ApiKeyRotada", new { id = tenant.Id });
    }

    private async Task LoadMaskedAsync(long id, CancellationToken ct)
    {
        var t = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t == null) { ApiKeyMasked = ""; return; }
        ApiKeyMasked = $"tg_live_************************{t.ApiKeyLast4}";
    }

    public class TenantInput
    {
        public long Id { get; set; }
        public string Nombre { get; set; } = "";
        public bool Activo { get; set; }

        public string PacUsuario { get; set; } = "";
        public string? PacPassword { get; set; }
        public bool PacProduccion { get; set; }
    }
}
