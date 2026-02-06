using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Vigma.TimbradoGateway.Infrastructure;
using Vigma.TimbradoGateway.Models;
using Vigma.TimbradoGateway.Services;
using System.Text.RegularExpressions;

namespace Vigma.TimbradoGateway.Pages.Monitor.Tenants.Certificados;

public class CreateModel : PageModel
{
    private readonly TimbradoDbContext _db;
    private readonly IConfiguration _cfg;
    private readonly OpenSslService _openssl;

    public int TenantId { get; set; }
    public string TenantNombre { get; set; } = "";

    [BindProperty] public string RFC { get; set; } = "";
    [BindProperty] public string KeyPassword { get; set; } = "";
    [BindProperty] public string Modo { get; set; } = "CERKEY";

    [BindProperty] public IFormFile? CerFile { get; set; }
    [BindProperty] public IFormFile? KeyFile { get; set; }
    [BindProperty] public IFormFile? PfxFile { get; set; }

    public bool Ok { get; set; }
    public string? Message { get; set; }

    public CreateModel(TimbradoDbContext db, IConfiguration cfg, OpenSslService openssl)
    {
        _db = db;
        _cfg = cfg;
        _openssl = openssl;
    }

    public async Task<IActionResult> OnGetAsync(int tenantId)
    {
        TenantId = tenantId;
        var t = await _db.Tenants.FirstOrDefaultAsync(x => x.Id == tenantId);
        if (t == null) return NotFound();
        TenantNombre = t.Nombre;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int tenantId)
    {
        await OnGetAsync(tenantId);

        try
        {
            RFC = (RFC ?? "").Trim().ToUpperInvariant();
            if (!Regex.IsMatch(RFC, @"^[A-ZÑ&0-9]{12,13}$"))
                throw new Exception("RFC inválido.");

            if (string.IsNullOrWhiteSpace(KeyPassword))
                throw new Exception("Password requerido.");

            var basePath = _cfg["Timbrado:CertBasePath"] ?? "/opt/timbrado/certs";
            var dir = Path.Combine(basePath, tenantId.ToString(), RFC);
            Directory.CreateDirectory(dir);

            var cerOriginal = Path.Combine(dir, $"{RFC}.cer");
            var keyOriginal = Path.Combine(dir, $"{RFC}.key");
            var pfxOriginal = Path.Combine(dir, $"{RFC}.pfx");

            var cerPem = Path.Combine(dir, $"{RFC}.cer");
            var keyPem = Path.Combine(dir, $"{RFC}.key");

            if (Modo == "CERKEY")
            {
                if (CerFile == null || KeyFile == null)
                    throw new Exception("Sube .cer y .key.");

                await SaveAsync(CerFile, cerOriginal);
                await SaveAsync(KeyFile, keyOriginal);

                await _openssl.RunAsync($"x509 -inform DER -in \"{cerOriginal}\" -out \"{cerPem}\"");
                await _openssl.RunAsync($"pkcs8 -inform DER -in \"{keyOriginal}\" -passin pass:{Escape(KeyPassword)} -out \"{keyPem}\"");
            }
            else if (Modo == "PFX")
            {
                if (PfxFile == null)
                    throw new Exception("Sube .pfx.");

                await SaveAsync(PfxFile, pfxOriginal);

                await _openssl.RunAsync($"pkcs12 -in \"{pfxOriginal}\" -clcerts -nokeys -out \"{cerPem}\" -passin pass:{Escape(KeyPassword)}");
                await _openssl.RunAsync($"pkcs12 -in \"{pfxOriginal}\" -nocerts -nodes -out \"{keyPem}\" -passin pass:{Escape(KeyPassword)}");
            }
            else throw new Exception("Modo inválido.");

            // Leer vigencia y serial
            var (start, end, serial) = await _openssl.ReadCertInfoAsync(cerPem);

            // Upsert en DB
            var c = await _db.Certificados.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.RFC == RFC);
            if (c == null)
            {
                c = new Certificado { TenantId = tenantId, RFC = RFC };
                _db.Certificados.Add(c);
            }

            c.CerPath = cerPem;
            c.KeyPath = keyPem;
            c.KeyPasswordEnc = KeyPassword; // MVP: luego ciframos
            c.Activo = true;
            c.VigenciaInicio = start;
            c.VigenciaFin = end;
            c.NoCertificado = serial;

            await _db.SaveChangesAsync();

            Ok = true;
            Message = $"OK: guardado y PEM generado para {RFC}.";
        }
        catch (Exception ex)
        {
            Ok = false;
            Message = ex.ToString();

        }

        return Page();
    }

    private static async Task SaveAsync(IFormFile f, string path)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        await f.CopyToAsync(fs);
    }

    private static string Escape(string pass) => pass.Replace("\"", "\\\"");

    // mantiene el contexto OnGet
    private async Task<IActionResult> OnGetAsync(int tenantId, bool _ = true) => await OnGetAsync(tenantId);
}
