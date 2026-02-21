using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Vigma.TimbradoGateway.Infrastructure;

namespace Vigma.TimbradoGateway.Pages.Monitor.Tenants;

public class ApiKeyRotadaModel : PageModel
{
    private readonly TimbradoDbContext _db;

    public ApiKeyRotadaModel(TimbradoDbContext db)
    {
        _db = db;
    }

    public long TenantId { get; set; }
    public string TenantNombre { get; set; } = "";
    public string? NewApiKey { get; set; }

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken ct)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant == null) return NotFound();

        TenantId = tenant.Id;
        TenantNombre = tenant.Nombre ?? "";

        NewApiKey = TempData["NewApiKey"] as string; // si refrescan, ya no sale
        return Page();
    }
}
