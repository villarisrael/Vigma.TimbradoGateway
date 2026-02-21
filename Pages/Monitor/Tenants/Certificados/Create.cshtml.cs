using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using Vigma.TimbradoGateway.Infrastructure;
using Vigma.TimbradoGateway.Models;
using Vigma.TimbradoGateway.Services;
using Vigma.TimbradoGateway.Util;

namespace Vigma.TimbradoGateway.Pages.Monitor.Tenants.Certificados;

public class CreateModel : PageModel
{
    private readonly TimbradoDbContext _db;
    private readonly IConfiguration _cfg;
    private readonly OpenSslService _openssl;
    private readonly ILogger<CreateModel> _logger;

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

    // ✅ Nuevo: UI amigable tipo checklist
    public UiStatus Ui { get; set; } = new();

    public class UiStatus
    {
        public bool Show { get; set; }
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";

        public bool CerOk { get; set; }
        public bool KeyOk { get; set; }
        public bool PassOk { get; set; }

        public string CerText { get; set; } = "";
        public string KeyText { get; set; } = "";
        public string PassText { get; set; } = "";
    }

    public CreateModel(TimbradoDbContext db, IConfiguration cfg, OpenSslService openssl, ILogger<CreateModel> logger)
    {
        _db = db;
        _cfg = cfg;
        _openssl = openssl;
        _logger = logger;
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

        // reset visual
        Ui = new UiStatus { Show = false };
        Ok = false;
        Message = null;

        try
        {
            RFC = (RFC ?? "").Trim().ToUpperInvariant();

            // ===== Validaciones amigables =====
            if (!Regex.IsMatch(RFC, @"^[A-ZÑ&0-9]{12,13}$"))
                throw new Exception("El RFC no tiene un formato válido.");

            if (string.IsNullOrWhiteSpace(KeyPassword))
                throw new Exception("La contraseña de la llave es requerida.");

            if (Modo != "CERKEY" && Modo != "PFX")
                throw new Exception("Selecciona un modo válido.");

            if (Modo == "CERKEY")
            {
                if (CerFile == null || CerFile.Length == 0)
                    throw new Exception("Selecciona el archivo .cer.");

                if (KeyFile == null || KeyFile.Length == 0)
                    throw new Exception("Selecciona el archivo .key.");
            }
            else // PFX
            {
                if (PfxFile == null || PfxFile.Length == 0)
                    throw new Exception("Selecciona el archivo .pfx.");
            }

            // ===== Prepara checklist inicial =====
            Ui.Show = true;

            if (Modo == "CERKEY")
            {
                Ui.CerOk = true;
                Ui.CerText = "Certificado (.cer): archivo seleccionado.";

                Ui.KeyOk = true;
                Ui.KeyText = "Llave privada (.key): archivo seleccionado.";

                Ui.PassOk = true;
                Ui.PassText = "Contraseña: capturada.";
            }
            else
            {
                Ui.CerOk = true;
                Ui.CerText = "Certificado: incluido en el archivo .pfx.";

                Ui.KeyOk = true;
                Ui.KeyText = "Archivo (.pfx): seleccionado.";

                Ui.PassOk = true;
                Ui.PassText = "Contraseña: capturada.";
            }

            // ===== Rutas =====
            var basePath = _cfg["Timbrado:CertBasePath"] ?? "/opt/timbrado/certs";
            var dir = Path.Combine(basePath, tenantId.ToString(), RFC);
            Directory.CreateDirectory(dir);

            var cerDer = Path.Combine(dir, $"{RFC}.cer");          // original DER
            var keyDer = Path.Combine(dir, $"{RFC}.key");          // original DER

            var cerPem = Path.Combine(dir, $"{RFC}.cer.pem");      // convertido PEM
            var keyPem = Path.Combine(dir, $"{RFC}.key.pem");      // convertido PEM

            var pfxOriginal = Path.Combine(dir, $"{RFC}.pfx");

            // ===== OpenSSL =====
            if (Modo == "CERKEY")
            {
                await SaveAsync(CerFile!, cerDer);
                await SaveAsync(KeyFile!, keyDer);

                await _openssl.RunAsync(
                    $"x509 -inform DER -in \"{cerDer}\" -out \"{cerPem}\"");

                await _openssl.RunAsync(
                    $"pkcs8 -inform DER -in \"{keyDer}\" -passin pass:\"{Escape(KeyPassword)}\" -out \"{keyPem}\"");
            }
            else // PFX
            {
                await SaveAsync(PfxFile!, pfxOriginal);

                await _openssl.RunAsync(
                    $"pkcs12 -in \"{pfxOriginal}\" -clcerts -nokeys -out \"{cerPem}\" -passin pass:\"{Escape(KeyPassword)}\"");

                await _openssl.RunAsync(
                    $"pkcs12 -in \"{pfxOriginal}\" -nocerts -out \"{keyPem}\" -passin pass:\"{Escape(KeyPassword)}\" -passout pass:\"{Escape(KeyPassword)}\"");
            }

            // ===== Cert info =====
            // var (start, end, serial) = await _openssl.ReadCertInfoAsync(cerPem);
            var certInfo = CertificadoReader.LeerCertificado(cerDer);

            // ===== Upsert en DB =====
            var c = await _db.Certificados.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.RFC == RFC);
            if (c == null)
            {
                c = new Certificado { TenantId = tenantId, RFC = RFC };
                _db.Certificados.Add(c);
            }

            c.CerPath = cerDer;
            c.KeyPath = keyDer;
            c.KeyPasswordEnc = KeyPassword; // MVP: luego ciframos
            c.Activo = true;
            c.VigenciaInicio = certInfo.VigenciaInicio;
            c.VigenciaFin = certInfo.VigenciaFin;
            c.NoCertificado = certInfo.NoCertificado;


            await _db.SaveChangesAsync();

            Ok = true;
            Message = $"OK: guardado y PEM generado para {RFC}.";

            Ui.Title = "Certificado registrado";
            Ui.Message = "Se generaron los PEM correctamente.";
            Ui.CerOk = true;
            Ui.KeyOk = true;
            Ui.PassOk = true;

            if (Modo == "CERKEY")
            {
                Ui.CerText = "Certificado (.cer): válido.";
                Ui.KeyText = "Llave privada (.key): convertida a PEM.";
                Ui.PassText = "Contraseña: correcta.";
            }
            else
            {
                Ui.CerText = "Certificado: extraído del .pfx.";
                Ui.KeyText = "Llave privada: extraída del .pfx.";
                Ui.PassText = "Contraseña: correcta.";
            }
        }
        catch (Exception ex)
        {
            // ✅ log completo (interno)
            _logger.LogError(ex, "Error registrando certificado. TenantId={TenantId}, RFC={RFC}, Modo={Modo}",
                tenantId, RFC, Modo);

            Ok = false;

            var friendly = GetFriendlyMessage(ex);
            Message = friendly;

            Ui.Show = true;
            Ui.Title = "Error al transformar la llave";
            Ui.Message = friendly;

            // checklist tipo tu imagen
            if (Modo == "CERKEY")
            {
                Ui.CerOk = true;
                Ui.CerText = "Certificado (.cer): archivo con formato válido.";

                Ui.KeyOk = false;
                Ui.KeyText = "Llave privada (.key): la llave o el archivo no son válidos.";

                Ui.PassOk = false;
                Ui.PassText = "Contraseña de la llave: no se pudo descifrar. Verifica la contraseña.";
            }
            else
            {
                Ui.CerOk = true;
                Ui.CerText = "Certificado: incluido en el .pfx.";

                Ui.KeyOk = false;
                Ui.KeyText = "Archivo (.pfx): no se pudo abrir o descifrar.";

                Ui.PassOk = false;
                Ui.PassText = "Contraseña del .pfx: verifica que sea la correcta.";
            }
        }

        return Page();
    }

    private static async Task SaveAsync(IFormFile f, string path)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        await f.CopyToAsync(fs);
    }

    private static string Escape(string pass) => pass.Replace("\"", "\\\"");

    private static string GetFriendlyMessage(Exception ex)
    {
        var txt = (ex.Message ?? "").ToLowerInvariant();

        // OpenSSL “wrong password / bad decrypt”
        if (txt.Contains("bad decrypt")
            || txt.Contains("maybe wrong password")
            || txt.Contains("cipherfinal error")
            || txt.Contains("pkcs12 cipherfinal error")
            || txt.Contains("mac verify error"))
        {
            return "No se pudo descifrar la llave privada. Verifica que la contraseña sea correcta.";
        }

        // Validaciones comunes
        if (txt.Contains("rfc"))
            return ex.Message;

        if (txt.Contains("contraseña") || txt.Contains("password"))
            return ex.Message;

        if (txt.Contains(".cer") || txt.Contains(".key") || txt.Contains(".pfx"))
            return ex.Message;

        // Fallback amigable
        return "Ocurrió un problema al procesar los archivos. Verifica que hayas seleccionado los correctos.";
    }

    // mantiene el contexto OnGet
    private async Task<IActionResult> OnGetAsync(int tenantId, bool _ = true) => await OnGetAsync(tenantId);
}
