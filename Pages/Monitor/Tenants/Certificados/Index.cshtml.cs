using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Vigma.TimbradoGateway.Infrastructure;
using Vigma.TimbradoGateway.Models;

namespace Vigma.TimbradoGateway.Pages.Monitor.Tenants.Certificados;

public class IndexModel : PageModel
{
    private readonly TimbradoDbContext _db;

    public int TenantId { get; set; }
    public string TenantNombre { get; set; } = "";
    public List<Certificado> Items { get; set; } = new();

    public IndexModel(TimbradoDbContext db) => _db = db;

    public async Task<IActionResult> OnGetAsync(int tenantId)
    {
        TenantId = tenantId;

        var t = await _db.Tenants.FirstOrDefaultAsync(x => x.Id == tenantId);
        if (t == null) return NotFound();

        TenantNombre = t.Nombre;

        Items = await _db.Certificados
            .Where(c => c.TenantId == tenantId)
            .OrderByDescending(c => c.Activo)
            .ThenBy(c => c.RFC)
            .ToListAsync();

        return Page();
    }
}
