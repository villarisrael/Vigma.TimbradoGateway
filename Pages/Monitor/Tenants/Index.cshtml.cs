using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Runtime.InteropServices;
using Vigma.TimbradoGateway.Infrastructure;

namespace Vigma.TimbradoGateway.Pages.Monitor.Tenants;

[Authorize]
public class IndexModel : PageModel
{
    private readonly TimbradoDbContext _db;

    public string? Q { get; set; }
    public int? Activo { get; set; }

    public List<Row> Items { get; set; } = new();

    public IndexModel(TimbradoDbContext db) => _db = db;

    public class Row
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = "";
        public bool Activo { get; set; }

        public bool pac_produccion { get; set; }
        public string ApiKeyMasked { get; set; } = "";
        public string RfcsResumen { get; set; } = "";
        public DateTime? CertVigenciaFinUtc { get; set; }
    }

    public async Task OnGetAsync(string? q, int? activo)
    {
        Q = q?.Trim();
        Activo = activo;

        var tenantsQ = _db.Tenants.AsQueryable();

        if (activo.HasValue)
            tenantsQ = tenantsQ.Where(t => t.Activo == (activo.Value == 1));

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var qUpper = Q!.ToUpper();
            tenantsQ = tenantsQ.Where(t =>
                t.Nombre.Contains(Q) ||
                _db.Certificados.Any(c => c.TenantId == t.Id && c.RFC.Contains(qUpper)));
        }

        var tenants = await tenantsQ.OrderBy(t => t.Nombre).ToListAsync();

        // Cargar resumen de certs
        var ids = tenants.Select(t => t.Id).ToList();
        var certs = await _db.Certificados
            .Where(c => ids.Contains(c.TenantId) && c.Activo)
            .ToListAsync();

        Items = tenants.Select(t =>
        {
            var tc = certs.Where(c => c.TenantId == t.Id).ToList();
            var rfcs = tc.Select(x => x.RFC).Distinct().ToList();
            var minVigFin = tc.Where(x => x.VigenciaFin != null).Select(x => x.VigenciaFin).Min();

            return new Row
            {
                Id = t.Id,
                Nombre = t.Nombre,
                Activo = t.Activo,
                pac_produccion = t.PacProduccion,
                ApiKeyMasked = $"tg_live_************************{t.ApiKeyLast4}",
                RfcsResumen = rfcs.Count == 0 ? "—" : string.Join(", ", rfcs.Take(3)) + (rfcs.Count > 3 ? "..." : ""),
                CertVigenciaFinUtc = minVigFin
            };
        }).ToList();
    }

    public async Task<IActionResult> OnPostToggleActivoAsync(long id)
    {
        var tenant = await _db.Tenants.FindAsync(id);
        if (tenant == null) return NotFound();

        tenant.Activo = !tenant.Activo;
        tenant.actualizado_utc = HoraMexico(); // si tienes campo
        await _db.SaveChangesAsync();

        return RedirectToPage(); // o RedirectToPage(new { ... filtros ... })
    }

    public async Task<IActionResult> OnPostToggleModoPruebaAsync(long id)
    {
        var tenant = await _db.Tenants.FindAsync(id);
        if (tenant == null) return NotFound();

        tenant.PacProduccion = !tenant.PacProduccion;

        // regla: si está en modo prueba, NO timbra producción
        // (eso lo aplicas en TimbradoService al elegir UrlWsTest/UrlWsProd)
        tenant.actualizado_utc = DateTime.UtcNow; // si aplica
        await _db.SaveChangesAsync();

        return RedirectToPage();
    }

    public static DateTime HoraMexico()
    {
        var tzMexico = TimeZoneInfo.FindSystemTimeZoneById(
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "Central Standard Time"
                : "America/Mexico_City"
        );

        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tzMexico);
    }


}
