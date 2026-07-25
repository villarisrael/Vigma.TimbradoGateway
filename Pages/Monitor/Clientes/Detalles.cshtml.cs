using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Vigma.TimbradoGateway.Infrastructure;

namespace Vigma.TimbradoGateway.Pages.Monitor.Clientes;

[Authorize(Roles = "Oficina,Admin")]
public class DetallesModel : PageModel
{
    private readonly TimbradoDbContext _db;

    public DetallesModel(TimbradoDbContext db) => _db = db;

    // ── Datos del cliente ─────────────────────────────────────────────────────
    public long    ClienteId  { get; set; }
    public string  Nombre     { get; set; } = "";
    public string? Rfc        { get; set; }
    public string? LogoPath   { get; set; }
    public bool    Activo     { get; set; }
    public DateTime CreadoUtc { get; set; }

    // ── Estadísticas ──────────────────────────────────────────────────────────
    public int  TotalTenants       { get; set; }
    public int  TenantsActivos     { get; set; }
    public long TotalTimbrados     { get; set; }
    public long TotalErrores       { get; set; }

    // ── Tenants con stats ─────────────────────────────────────────────────────
    public List<TenantStats> Tenants { get; set; } = new();

    // ── Usuarios cliente ──────────────────────────────────────────────────────
    public List<UsuarioClienteInfo> UsuariosCliente { get; set; } = new();

    public class TenantStats
    {
        public int    Id        { get; set; }
        public string Nombre    { get; set; } = "";
        public bool   Activo    { get; set; }
        public string? LogoPath { get; set; }
        public long   Timbrados { get; set; }
        public long   Errores   { get; set; }
    }

    public class UsuarioClienteInfo
    {
        public long    Id      { get; set; }
        public string  Usuario { get; set; } = "";
        public string? Nombre  { get; set; }
        public bool    Activo  { get; set; }
        public DateTime Creado { get; set; }
    }

    // ── GET ───────────────────────────────────────────────────────────────────

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken ct)
    {
        var cliente = await _db.Clientes.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (cliente == null) return NotFound();

        ClienteId  = cliente.Id;
        Nombre     = cliente.Nombre;
        Rfc        = cliente.Rfc;
        LogoPath   = cliente.LogoPath;
        Activo     = cliente.Activo;
        CreadoUtc  = cliente.CreadoUtc;

        // Tenants del cliente
        var tenants = await _db.Tenants
            .Where(t => t.ClienteId == id)
            .OrderBy(t => t.Nombre)
            .ToListAsync(ct);

        TotalTenants   = tenants.Count;
        TenantsActivos = tenants.Count(t => t.Activo);

        var tenantIds = tenants.Select(t => (long)t.Id).ToList();

        // Conteo de timbrados por tenant
        var timbradosPorTenant = await _db.TimbradoOkLogs
            .Where(l => tenantIds.Contains(l.TenantId))
            .GroupBy(l => l.TenantId)
            .Select(g => new { TenantId = g.Key, Count = (long)g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, ct);

        // Conteo de errores por tenant
        var erroresPorTenant = await _db.TimbradoErrorLogs
            .Where(l => tenantIds.Contains(l.TenantId))
            .GroupBy(l => l.TenantId)
            .Select(g => new { TenantId = g.Key, Count = (long)g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, ct);

        TotalTimbrados = timbradosPorTenant.Values.Sum();
        TotalErrores   = erroresPorTenant.Values.Sum();

        Tenants = tenants.Select(t => new TenantStats
        {
            Id        = t.Id,
            Nombre    = t.Nombre ?? "",
            Activo    = t.Activo,
            LogoPath  = t.LogoPath,
            Timbrados = timbradosPorTenant.TryGetValue((long)t.Id, out var tb) ? tb : 0,
            Errores   = erroresPorTenant.TryGetValue((long)t.Id, out var er)   ? er : 0
        }).ToList();

        // Usuarios del cliente con Rol = "Cliente"
        UsuariosCliente = await _db.UsuariosOficina
            .Where(u => u.ClienteId == id && u.Rol == "Cliente")
            .OrderByDescending(u => u.Activo)
            .ThenBy(u => u.Usuario)
            .Select(u => new UsuarioClienteInfo
            {
                Id      = u.Id,
                Usuario = u.Usuario,
                Nombre  = u.Nombre,
                Activo  = u.Activo,
                Creado  = u.Creado
            })
            .ToListAsync(ct);

        return Page();
    }
}
