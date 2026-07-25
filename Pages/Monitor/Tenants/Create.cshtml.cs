using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

using Vigma.TimbradoGateway.Infrastructure;
using Vigma.TimbradoGateway.Models;
using Vigma.TimbradoGateway.Services;
using Vigma.TimbradoGateway.Utils;

namespace Vigma.TimbradoGateway.Pages.Monitor.Tenants
{
    public class CreateModel : PageModel
    {
        private readonly TimbradoDbContext _db;
        private readonly CryptoService _crypto;
        private readonly IWebHostEnvironment _env;

        private static readonly string[] _allowedExt  = [".png", ".jpg", ".jpeg", ".webp", ".gif"];
        private static readonly string[] _allowedMime = ["image/png", "image/jpeg", "image/webp", "image/gif"];
        private const long MaxLogoBytes = 5 * 1024 * 1024;

        public CreateModel(TimbradoDbContext db, CryptoService crypto, IWebHostEnvironment env)
        {
            _db     = db;
            _crypto = crypto;
            _env    = env;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        [BindProperty]
        public IFormFile? LogoFile { get; set; }

        // Para mostrar una sola vez
        public string? PlainApiKey { get; set; }
        public long? CreatedTenantId { get; set; }

        public class InputModel
        {
            [Required, StringLength(150)]
            public string Nombre { get; set; } = "";

            public bool Activo { get; set; } = true;

            [StringLength(80)]
            public string? PacUsuario { get; set; }

            // Solo captura; se guardará cifrado en PacPasswordEnc
            [StringLength(200)]
            public string? PacPassword { get; set; }

            public bool PacProduccion { get; set; } = false;
        }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
                return Page();

            // 1) Genera la ApiKey (solo se muestra una vez)
            PlainApiKey = ApiKeyGenerator.GenerateLiveKey();

            // 2) Guarda hash + last4 en BD (NO guardes la key en claro)
            var apiKeyHash = ApiKeyGenerator.Hash(PlainApiKey);
            var apiKeyLast4 = ApiKeyGenerator.Last4(PlainApiKey);

            // 3) Password PAC cifrado (opcional)
            string? pacPwdEnc = null;
            if (!string.IsNullOrWhiteSpace(Input.PacPassword))
                pacPwdEnc = _crypto.EncryptToBase64(Input.PacPassword);

            var tenant = new Tenant
            {
                Nombre = Input.Nombre.Trim(),
                Activo = Input.Activo,

                ApiKeyHash = apiKeyHash,
                ApiKeyLast4 = apiKeyLast4,
                ApiKeyRotatedUtc = DateTime.UtcNow,
                creado_utc = DateTime.UtcNow,
                PacUsuario = string.IsNullOrWhiteSpace(Input.PacUsuario) ? null : Input.PacUsuario.Trim(),
                PacPasswordEnc = pacPwdEnc,
                PacProduccion = Input.PacProduccion
            };

            _db.Tenants.Add(tenant);
            await _db.SaveChangesAsync();

            CreatedTenantId = tenant.Id;

            // 4) Logo opcional
            if (LogoFile is { Length: > 0 })
            {
                var ext = Path.GetExtension(LogoFile.FileName).ToLowerInvariant();
                if (_allowedExt.Contains(ext)
                    && _allowedMime.Contains(LogoFile.ContentType.ToLowerInvariant())
                    && LogoFile.Length <= MaxLogoBytes)
                {
                    var logosDir = Path.Combine(_env.WebRootPath, "logos");
                    Directory.CreateDirectory(logosDir);

                    var fileName = $"tenant_{tenant.Id}{ext}";
                    var filePath = Path.Combine(logosDir, fileName);

                    await using var stream = new FileStream(filePath, FileMode.Create);
                    await LogoFile.CopyToAsync(stream);

                    tenant.LogoPath       = $"/logos/{fileName}";
                    tenant.actualizado_utc = DateTime.UtcNow;
                    await _db.SaveChangesAsync();
                }
            }

            // Regresa a la misma página para mostrar la ApiKey
            return Page();
        }
    }
}
