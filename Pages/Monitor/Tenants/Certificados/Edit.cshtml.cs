using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Vigma.TimbradoGateway.Infrastructure;
using Vigma.TimbradoGateway.Services;

namespace Vigma.TimbradoGateway.Pages.Monitor.Tenants.Certificados;

public class EditModel : PageModel
{
    private readonly TimbradoDbContext _db;
    private readonly IConfiguration _cfg;
    private readonly OpenSslService _openssl;

    public int TenantId { get; set; }
    public int CertId { get; set; }
    public string RFC { get; set; } = "";

    [BindProperty] public bool Activo { get; set; }
    [BindProperty] public string? KeyPassword { get; set; }

    [BindProperty] public IFormFile? CerFile { get; set; }
    [BindProperty] public IFormFile? KeyFile { get; set; }
    [BindProperty] public IFormFile? PfxFile { get; set; }

    public bool Ok { get; set; }
    public string? Message { get; set; }

    public EditModel(TimbradoDbContext db, IConfiguration cfg, OpenSslService openssl)
    {
        _db = db;
        _cfg = cfg;
        _openssl = openssl;
    }

    public async Task<IActionResult> OnGetAsync(int tenantId, int certId)
    {
        TenantId = tenantId;
        CertId = certId;

        var c = await _db.Certificados.FirstOrDefaultAsync(x => x.Id == certId && x.TenantId == tenantId);
        if (c == null) return NotFound();

        RFC = c.RFC;
        Activo = (bool)c.Activo;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int tenantId, int certId)
    {
        await OnGetAsync(tenantId, certId);

        try
        {
            var c = await _db.Certificados.FirstOrDefaultAsync(x => x.Id == certId && x.TenantId == tenantId);
            if (c == null) return NotFound();

            c.Activo = Activo;

            var basePath = _cfg["Timbrado:CertBasePath"] ?? "/opt/timbrado/certs";
            var dir = Path.Combine(basePath, tenantId.ToString(), c.RFC);
            Directory.CreateDirectory(dir);

            var cerOriginal = Path.Combine(dir, "original.cer");
            var keyOriginal = Path.Combine(dir, "original.key");
            var pfxOriginal = Path.Combine(dir, "original.pfx");
            var cerPem = Path.Combine(dir, $"{c.RFC}.cer.pem");
            var keyPem = Path.Combine(dir, $"{c.RFC}.key.pem");

            // password nuevo?
            if (!string.IsNullOrWhiteSpace(KeyPassword))
                c.KeyPasswordEnc = KeyPassword;

            var pass = c.KeyPasswordEnc;

            // Reemplazo por PFX tiene prioridad
            if (PfxFile != null)
            {
                await SaveAsync(PfxFile, pfxOriginal);
                await _openssl.RunAsync($"pkcs12 -in \"{pfxOriginal}\" -clcerts -nokeys -out \"{cerPem}\" -passin pass:{Escape(pass)}");
                await _openssl.RunAsync($"pkcs12 -in \"{pfxOriginal}\" -nocerts -nodes -out \"{keyPem}\" -passin pass:{Escape(pass)}");
            }
            else
            {
                if (CerFile != null) await SaveAsync(CerFile, cerOriginal);
                if (KeyFile != null) await SaveAsync(KeyFile, keyOriginal);

                // Regenera PEM si subieron algo
                if (CerFile != null)
                    await _openssl.RunAsync($"x509 -inform DER -in \"{cerOriginal}\" -out \"{cerPem}\"");

                if (KeyFile != null)
                    await _openssl.RunAsync($"pkcs8 -inform DER -in \"{keyOriginal}\" -passin pass:{Escape(pass)} -out \"{keyPem}\"");
            }

            // Si cerPem existe, actualiza vigencia
            if (System.IO.File.Exists(cerPem))
            {
                var (start, end, serial) = await _openssl.ReadCertInfoAsync(cerPem);
                c.VigenciaInicio = start;
                c.VigenciaFin = end;
                c.NoCertificado = serial;
                c.CerPath = cerPem;
            }

            if (System.IO.File.Exists(keyPem))
                c.KeyPath = keyPem;

            await _db.SaveChangesAsync();

            Ok = true;
            Message = "Cambios guardados.";
        }
        catch (Exception ex)
        {
            Ok = false;
            Message = ex.Message;
        }

        return Page();
    }

    private static async Task SaveAsync(IFormFile f, string path)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        await f.CopyToAsync(fs);
    }

    private static string Escape(string pass) => pass.Replace("\"", "\\\"");
}
