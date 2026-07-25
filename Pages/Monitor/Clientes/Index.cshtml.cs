using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Vigma.TimbradoGateway.Infrastructure;

namespace Vigma.TimbradoGateway.Pages.Monitor.Clientes;

[Authorize(Roles = "Oficina,Admin")]
public class IndexModel : PageModel
{
    private readonly TimbradoDbContext _db;

    public List<Row> Items { get; set; } = new();

    [TempData] public string? SuccessMessage { get; set; }

    public IndexModel(TimbradoDbContext db) => _db = db;

    public class Row
    {
        public long   Id          { get; set; }
        public string Nombre      { get; set; } = "";
        public string? Rfc        { get; set; }
        public string? LogoPath   { get; set; }
        public bool   Activo      { get; set; }
        public DateTime CreadoUtc { get; set; }
        public int    NumTenants  { get; set; }
    }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var clientes = await _db.Clientes
            .OrderBy(c => c.Nombre)
            .ToListAsync(ct);

        // Contar tenants por cliente en una sola consulta
        var conteos = await _db.Tenants
            .Where(t => t.ClienteId != null)
            .GroupBy(t => t.ClienteId!.Value)
            .Select(g => new { ClienteId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ClienteId, x => x.Count, ct);

        Items = clientes.Select(c => new Row
        {
            Id        = c.Id,
            Nombre    = c.Nombre,
            Rfc       = c.Rfc,
            LogoPath  = c.LogoPath,
            Activo    = c.Activo,
            CreadoUtc = c.CreadoUtc,
            NumTenants = conteos.TryGetValue(c.Id, out var cnt) ? cnt : 0
        }).ToList();
    }

    public async Task<IActionResult> OnPostToggleActivoAsync(long id, CancellationToken ct)
    {
        var cliente = await _db.Clientes.FindAsync(new object[] { id }, ct);
        if (cliente == null) return NotFound();

        cliente.Activo = !cliente.Activo;
        await _db.SaveChangesAsync(ct);

        SuccessMessage = cliente.Activo
            ? $"Cliente '{cliente.Nombre}' activado."
            : $"Cliente '{cliente.Nombre}' suspendido.";

        return RedirectToPage();
    }
}
