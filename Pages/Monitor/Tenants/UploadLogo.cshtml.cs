using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Vigma.TimbradoGateway.Infrastructure;

namespace Vigma.TimbradoGateway.Pages.Monitor.Tenants;

[Authorize]
public class UploadLogoModel : PageModel
{
    private readonly TimbradoDbContext _db;
    private readonly IWebHostEnvironment _env;

    private static readonly string[] _allowedExt  = [".png", ".jpg", ".jpeg", ".webp", ".gif"];
    private static readonly string[] _allowedMime = ["image/png", "image/jpeg", "image/webp", "image/gif"];
    private const long MaxBytes = 5 * 1024 * 1024; // 5 MB

    public UploadLogoModel(TimbradoDbContext db, IWebHostEnvironment env)
    {
        _db  = db;
        _env = env;
    }

    // ── Datos para la vista ───────────────────────────────────────────────────
    public int    TenantId   { get; private set; }
    public string TenantNombre { get; private set; } = "";
    public string? LogoActual  { get; private set; }

    [TempData] public string? MensajeOk    { get; set; }
    [TempData] public string? MensajeError { get; set; }

    // ── GET ───────────────────────────────────────────────────────────────────
    public async Task<IActionResult> OnGetAsync(int id)
    {
        var tenant = await _db.Tenants.FindAsync(id);
        if (tenant is null) return NotFound();

        TenantId    = tenant.Id;
        TenantNombre = tenant.Nombre ?? "";
        LogoActual  = tenant.LogoPath;
        return Page();
    }

    // ── POST Upload ───────────────────────────────────────────────────────────
    public async Task<IActionResult> OnPostUploadAsync(int id, IFormFile? logo)
    {
        var tenant = await _db.Tenants.FindAsync(id);
        if (tenant is null) return NotFound();

        // Validaciones
        if (logo is null || logo.Length == 0)
        {
            MensajeError = "Selecciona un archivo de imagen.";
            return RedirectToPage(new { id });
        }

        if (logo.Length > MaxBytes)
        {
            MensajeError = $"El archivo supera el límite de 5 MB.";
            return RedirectToPage(new { id });
        }

        var ext = Path.GetExtension(logo.FileName).ToLowerInvariant();
        if (!_allowedExt.Contains(ext))
        {
            MensajeError = "Tipo de archivo no permitido. Usa PNG, JPG, WEBP o GIF.";
            return RedirectToPage(new { id });
        }

        if (!_allowedMime.Contains(logo.ContentType.ToLowerInvariant()))
        {
            MensajeError = "El contenido del archivo no corresponde a una imagen válida.";
            return RedirectToPage(new { id });
        }

        // Guardar archivo → wwwroot/logos/tenant_{id}{ext}
        var logosDir  = Path.Combine(_env.WebRootPath, "logos");
        Directory.CreateDirectory(logosDir);

        // Borrar logo anterior si existe (cualquier extensión)
        foreach (var viejo in Directory.GetFiles(logosDir, $"tenant_{id}.*"))
            System.IO.File.Delete(viejo);

        var fileName  = $"tenant_{id}{ext}";
        var filePath  = Path.Combine(logosDir, fileName);

        await using (var stream = new FileStream(filePath, FileMode.Create))
            await logo.CopyToAsync(stream);

        // Actualizar BD
        tenant.LogoPath = $"/logos/{fileName}";
        tenant.actualizado_utc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        MensajeOk = "Logo guardado correctamente.";
        return RedirectToPage(new { id });
    }

    // ── POST Eliminar logo ────────────────────────────────────────────────────
    public async Task<IActionResult> OnPostEliminarAsync(int id)
    {
        var tenant = await _db.Tenants.FindAsync(id);
        if (tenant is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(tenant.LogoPath))
        {
            var filePath = Path.Combine(_env.WebRootPath,
                tenant.LogoPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

            if (System.IO.File.Exists(filePath))
                System.IO.File.Delete(filePath);
        }

        tenant.LogoPath       = null;
        tenant.actualizado_utc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        MensajeOk = "Logo eliminado.";
        return RedirectToPage(new { id });
    }
}
