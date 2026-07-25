using BCrypt.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using Vigma.TimbradoGateway.Infrastructure;
using Vigma.TimbradoGateway.Models;
using ClienteModel = Vigma.TimbradoGateway.Models.Cliente;

namespace Vigma.TimbradoGateway.Pages.Monitor.Clientes;

[Authorize(Roles = "Oficina,Admin")]
public class CrearModel : PageModel
{
    private readonly TimbradoDbContext _db;
    private readonly IWebHostEnvironment _env;

    private static readonly string[] _allowedExt  = [".png", ".jpg", ".jpeg", ".gif", ".webp"];
    private static readonly string[] _allowedMime = ["image/png", "image/jpeg", "image/gif", "image/webp"];
    private const long MaxLogoBytes = 5 * 1024 * 1024; // 5 MB

    public CrearModel(TimbradoDbContext db, IWebHostEnvironment env)
    {
        _db  = db;
        _env = env;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty]
    public IFormFile? LogoFile { get; set; }

    [BindProperty]
    public UsuarioInputModel UsuarioInput { get; set; } = new();

    public bool CrearUsuario { get; set; } = false;

    public class InputModel
    {
        [Required(ErrorMessage = "El nombre es obligatorio.")]
        [StringLength(120, ErrorMessage = "Máximo 120 caracteres.")]
        [Display(Name = "Nombre / Razón Social")]
        public string Nombre { get; set; } = "";

        [StringLength(13, ErrorMessage = "El RFC no puede exceder 13 caracteres.")]
        [Display(Name = "RFC")]
        public string? Rfc { get; set; }

        [Display(Name = "Activo")]
        public bool Activo { get; set; } = true;
    }

    public class UsuarioInputModel
    {
        [EmailAddress(ErrorMessage = "Debe ser un email válido.")]
        [StringLength(60, ErrorMessage = "Máximo 60 caracteres.")]
        [Display(Name = "Email del usuario")]
        public string? Email { get; set; }

        [StringLength(100, MinimumLength = 6, ErrorMessage = "La contraseña debe tener entre 6 y 100 caracteres.")]
        [Display(Name = "Contraseña")]
        public string? Password { get; set; }

        [StringLength(100, ErrorMessage = "Máximo 100 caracteres.")]
        [Display(Name = "Nombre completo")]
        public string? Nombre { get; set; }
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        // Validar solo Input (no UsuarioInput que es opcional)
        ModelState.Remove("UsuarioInput.Email");
        ModelState.Remove("UsuarioInput.Password");
        ModelState.Remove("UsuarioInput.Nombre");

        if (!ModelState.IsValid)
            return Page();

        // ── Procesar logo ────────────────────────────────────────────────────
        string? logoPath = null;
        if (LogoFile != null && LogoFile.Length > 0)
        {
            var (ok, path, error) = await GuardarLogoAsync(LogoFile, 0); // ID temporal, se renombrará
            if (!ok)
            {
                ModelState.AddModelError("LogoFile", error!);
                return Page();
            }
            logoPath = path;
        }

        // ── Crear cliente ────────────────────────────────────────────────────
        var cliente = new ClienteModel
        {
            Nombre    = Input.Nombre.Trim(),
            Rfc       = string.IsNullOrWhiteSpace(Input.Rfc)  ? null : Input.Rfc.Trim().ToUpper(),
            LogoPath  = logoPath,
            Activo    = Input.Activo,
            CreadoUtc = DateTime.UtcNow
        };

        _db.Clientes.Add(cliente);
        await _db.SaveChangesAsync(ct);

        // Si el logo se guardó con ID temporal, renombrarlo con el ID real
        if (logoPath != null && logoPath.Contains("_tmp_"))
        {
            var ext          = Path.GetExtension(logoPath);
            var logosDir     = Path.Combine(_env.WebRootPath, "logos");
            var oldPath      = Path.Combine(_env.WebRootPath, logoPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            var newFileName  = $"cliente_{cliente.Id}{ext}";
            var newFilePath  = Path.Combine(logosDir, newFileName);

            if (System.IO.File.Exists(oldPath))
                System.IO.File.Move(oldPath, newFilePath, overwrite: true);

            cliente.LogoPath = $"/logos/{newFileName}";
            await _db.SaveChangesAsync(ct);
        }

        // ── Crear usuario cliente (opcional) ─────────────────────────────────
        var crearUsr = !string.IsNullOrWhiteSpace(UsuarioInput.Email) ||
                       !string.IsNullOrWhiteSpace(UsuarioInput.Password);

        if (crearUsr)
        {
            var email = (UsuarioInput.Email ?? "").Trim().ToLowerInvariant();
            var pwd   = UsuarioInput.Password ?? "";

            if (string.IsNullOrWhiteSpace(email))
            {
                TempData["SuccessMessage"] = $"Cliente '{cliente.Nombre}' creado. Usuario no creado: email vacío.";
                return RedirectToPage("Editar", new { id = cliente.Id });
            }
            if (pwd.Length < 6)
            {
                TempData["SuccessMessage"] = $"Cliente '{cliente.Nombre}' creado. Usuario no creado: contraseña muy corta (mín. 6 caracteres).";
                return RedirectToPage("Editar", new { id = cliente.Id });
            }

            var existe = await _db.UsuariosOficina.AnyAsync(u => u.Usuario == email, ct);
            if (existe)
            {
                TempData["SuccessMessage"] = $"Cliente '{cliente.Nombre}' creado. Usuario no creado: el email '{email}' ya está en uso.";
                return RedirectToPage("Editar", new { id = cliente.Id });
            }

            var usuario = new UsuarioOficina
            {
                Usuario      = email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(pwd),
                Rol          = "Cliente",
                Nombre       = string.IsNullOrWhiteSpace(UsuarioInput.Nombre) ? null : UsuarioInput.Nombre.Trim(),
                Activo       = true,
                Creado       = DateTime.UtcNow,
                ClienteId    = cliente.Id
            };

            _db.UsuariosOficina.Add(usuario);
            await _db.SaveChangesAsync(ct);

            TempData["SuccessMessage"] = $"Cliente '{cliente.Nombre}' creado con usuario '{email}'.";
            return RedirectToPage("Editar", new { id = cliente.Id });
        }

        TempData["SuccessMessage"] = $"Cliente '{cliente.Nombre}' creado correctamente.";
        return RedirectToPage("Index");
    }

    // ── Helper: guardar logo ─────────────────────────────────────────────────
    private async Task<(bool ok, string? path, string? error)> GuardarLogoAsync(IFormFile file, long clienteId)
    {
        if (file.Length > MaxLogoBytes)
            return (false, null, "El archivo supera el límite de 5 MB.");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!_allowedExt.Contains(ext))
            return (false, null, "Tipo de archivo no permitido. Usa PNG, JPG, GIF o WEBP.");

        if (!_allowedMime.Contains(file.ContentType.ToLowerInvariant()))
            return (false, null, "El contenido del archivo no corresponde a una imagen válida.");

        var logosDir = Path.Combine(_env.WebRootPath, "logos");
        Directory.CreateDirectory(logosDir);

        string fileName;
        if (clienteId == 0)
        {
            // ID temporal hasta que tengamos el ID real
            fileName = $"cliente_tmp_{Guid.NewGuid():N}{ext}";
        }
        else
        {
            // Eliminar logos anteriores de este cliente
            foreach (var viejo in Directory.GetFiles(logosDir, $"cliente_{clienteId}.*"))
                System.IO.File.Delete(viejo);
            fileName = $"cliente_{clienteId}{ext}";
        }

        var filePath = Path.Combine(logosDir, fileName);
        await using (var stream = new FileStream(filePath, FileMode.Create))
            await file.CopyToAsync(stream);

        return (true, $"/logos/{fileName}", null);
    }
}
