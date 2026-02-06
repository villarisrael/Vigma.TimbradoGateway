using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Vigma.TimbradoGateway.Infrastructure;

namespace Vigma.TimbradoGateway.Pages.Monitor.Tenants.Certificados;

public class DetailsModel : PageModel
{
    private readonly TimbradoDbContext _db;

    public int TenantId { get; set; }
    public int CertId { get; set; }
    public string TenantNombre { get; set; } = "";

    public string RFC { get; set; } = "";
    public bool Activo { get; set; }
    public string? NoCertificado { get; set; }
    public DateTime? VigenciaInicioUtc { get; set; }
    public DateTime? VigenciaFinUtc { get; set; }
    public string CerPath { get; set; } = "";
    public string KeyPath { get; set; } = "";

    public DetailsModel(TimbradoDbContext db) => _db = db;

    public async Task<IActionResult> OnGetAsync(int tenantId, int certId)
    {
        TenantId = tenantId;
        CertId = certId;

        var t = await _db.Tenants.FirstOrDefaultAsync(x => x.Id == tenantId);
        if (t == null) return NotFound();
        TenantNombre = t.Nombre;

        var c = await _db.Certificados.FirstOrDefaultAsync(x => x.Id == certId && x.TenantId == tenantId);
        if (c == null) return NotFound();

        RFC = c.RFC;
        Activo = (bool)c.Activo;
        NoCertificado = c.NoCertificado;
        VigenciaInicioUtc = c.VigenciaInicio;
        VigenciaFinUtc = c.VigenciaFin;
        CerPath = c.CerPath;
        KeyPath = c.KeyPath;

        return Page();
    }
}
