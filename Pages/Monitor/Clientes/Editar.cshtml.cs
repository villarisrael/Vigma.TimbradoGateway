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
public class EditarModel : PageModel
{
    private readonly TimbradoDbContext _db;
    private readonly IWebHostEnvironment _env;

    private static readonly string[] _allowedExt  = [".png", ".jpg", ".jpeg", ".gif", ".webp"];
    private static readonly string[] _allowedMime = ["image/png", "image/jpeg", "image/gif", "image/webp"];
    private const long MaxLogoBytes = 5 * 1024 * 1024; // 5 MB

    public EditarModel(TimbradoDbContext db, IWebHostEnvironment env)
    {
        _db  = db;
        _env = env;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty]
    public IFormFile? LogoFile { get; set; }

    [BindProperty]
    public NuevoUsuarioInputModel NuevoUsuario { get; set; } = new();

    /// <summary>Tenants actualmente asignados a este cliente.</summary>
    public List<TenantRow> TenantsAsignados { get; set; } = new();

    /// <summary>Tenants sin cliente (cliente_id IS NULL) — disponibles para asignar.</summary>
    public List<TenantRow> TenantsDisponibles { get; set; } = new();

    /// <summary>Usuarios con Rol = "Cliente" asociados a este cliente.</summary>
    public List<UsuarioClienteRow> UsuariosCliente { get; set; } = new();

    [TempData] public string? SuccessMessage { get; set; }
    [TempData] public string? ErrorMessage   { get; set; }

    public class InputModel
    {
        public long Id { get; set; }

        [Required(ErrorMessage = "El nombre es obligatorio.")]
        [StringLength(120, ErrorMessage = "Máximo 120 caracteres.")]
        [Display(Name = "Nombre / Razón Social")]
        public string Nombre { get; set; } = "";

        [StringLength(13, ErrorMessage = "El RFC no puede exceder 13 caracteres.")]
        [Display(Name = "RFC")]
        public string? Rfc { get; set; }

        [Display(Name = "Logo actual")]
        public string? LogoPath { get; set; }

        [Display(Name = "Activo")]
        public bool Activo { get; set; }
    }

    public class TenantRow
    {
        public int    Id     { get; set; }
        public string Nombre { get; set; } = "";
        public bool   Activo { get; set; }
    }

    public class UsuarioClienteRow
    {
        public long    Id      { get; set; }
        public string  Usuario { get; set; } = "";
        public string? Nombre  { get; set; }
        public bool    Activo  { get; set; }
        public DateTime Creado { get; set; }
    }

    public class NuevoUsuarioInputModel
    {
        [EmailAddress(ErrorMessage = "Debe ser un email válido.")]
        [StringLength(60, ErrorMessage = "Máximo 60 caracteres.")]
        [Display(Name = "Email")]
        public string? Email { get; set; }

        [StringLength(100, MinimumLength = 6, ErrorMessage = "La contraseña debe tener entre 6 y 100 caracteres.")]
        [Display(Name = "Contraseña")]
        public string? Password { get; set; }

        [StringLength(100, ErrorMessage = "Máximo 100 caracteres.")]
        [Display(Name = "Nombre completo")]
        public string? Nombre { get; set; }
    }

    // ── GET ───────────────────────────────────────────────────────────────────

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken ct)
    {
        var cliente = await _db.Clientes.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (cliente == null) return NotFound();

        Input.Id       = cliente.Id;
        Input.Nombre   = cliente.Nombre;
        Input.Rfc      = cliente.Rfc;
        Input.LogoPath = cliente.LogoPath;
        Input.Activo   = cliente.Activo;

        await CargarDatosAsync(id, ct);
        return Page();
    }

    // ── POST Guardar datos del cliente ────────────────────────────────────────

    public async Task<IActionResult> OnPostGuardarAsync(CancellationToken ct)
    {
        // Ignorar validaciones de NuevoUsuario en este handler
        ModelState.Remove("NuevoUsuario.Email");
        ModelState.Remove("NuevoUsuario.Password");
        ModelState.Remove("NuevoUsuario.Nombre");

        if (!ModelState.IsValid)
        {
            await CargarDatosAsync(Input.Id, ct);
            return Page();
        }

        var cliente = await _db.Clientes.FirstOrDefaultAsync(c => c.Id == Input.Id, ct);
        if (cliente == null) return NotFound();

        // ── Procesar nuevo logo si se subió ──────────────────────────────────
        if (LogoFile != null && LogoFile.Length > 0)
        {
            var (ok, path, error) = await GuardarLogoAsync(LogoFile, Input.Id);
            if (!ok)
            {
                ModelState.AddModelError("LogoFile", error!);
                await CargarDatosAsync(Input.Id, ct);
                return Page();
            }
            cliente.LogoPath = path;
        }

        cliente.Nombre = Input.Nombre.Trim();
        cliente.Rfc    = string.IsNullOrWhiteSpace(Input.Rfc) ? null : Input.Rfc.Trim().ToUpper();
        cliente.Activo = Input.Activo;

        await _db.SaveChangesAsync(ct);

        SuccessMessage = "Cambios guardados correctamente.";
        return RedirectToPage(new { id = Input.Id });
    }

    // ── POST Eliminar logo ────────────────────────────────────────────────────

    public async Task<IActionResult> OnPostEliminarLogoAsync(long clienteId, CancellationToken ct)
    {
        var cliente = await _db.Clientes.FirstOrDefaultAsync(c => c.Id == clienteId, ct);
        if (cliente == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(cliente.LogoPath))
        {
            var filePath = Path.Combine(_env.WebRootPath,
                cliente.LogoPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(filePath))
                System.IO.File.Delete(filePath);
        }

        cliente.LogoPath = null;
        await _db.SaveChangesAsync(ct);

        SuccessMessage = "Logo eliminado.";
        return RedirectToPage(new { id = clienteId });
    }

    // ── POST Crear usuario cliente ────────────────────────────────────────────

    public async Task<IActionResult> OnPostCrearUsuarioAsync(long clienteId, CancellationToken ct)
    {
        var email = (NuevoUsuario.Email ?? "").Trim().ToLowerInvariant();
        var pwd   = NuevoUsuario.Password ?? "";

        if (string.IsNullOrWhiteSpace(email))
        {
            ErrorMessage = "El email es obligatorio para crear un usuario.";
            return RedirectToPage(new { id = clienteId });
        }

        if (pwd.Length < 6)
        {
            ErrorMessage = "La contraseña debe tener al menos 6 caracteres.";
            return RedirectToPage(new { id = clienteId });
        }

        var emailRegex = new System.Text.RegularExpressions.Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");
        if (!emailRegex.IsMatch(email))
        {
            ErrorMessage = "El email no tiene un formato válido.";
            return RedirectToPage(new { id = clienteId });
        }

        var existe = await _db.UsuariosOficina.AnyAsync(u => u.Usuario == email, ct);
        if (existe)
        {
            ErrorMessage = $"El email '{email}' ya está registrado como usuario.";
            return RedirectToPage(new { id = clienteId });
        }

        var cliente = await _db.Clientes.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == clienteId, ct);
        if (cliente == null) return NotFound();

        var usuario = new UsuarioOficina
        {
            Usuario      = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(pwd),
            Rol          = "Cliente",
            Nombre       = string.IsNullOrWhiteSpace(NuevoUsuario.Nombre) ? null : NuevoUsuario.Nombre.Trim(),
            Activo       = true,
            Creado       = DateTime.UtcNow,
            ClienteId    = clienteId
        };

        _db.UsuariosOficina.Add(usuario);
        await _db.SaveChangesAsync(ct);

        SuccessMessage = $"Usuario '{email}' creado para el cliente '{cliente.Nombre}'.";
        return RedirectToPage(new { id = clienteId });
    }

    // ── POST Cambiar contraseña de usuario cliente ────────────────────────────

    public async Task<IActionResult> OnPostCambiarContraseñaAsync(long clienteId, long usuarioId, string nuevaContraseña, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(nuevaContraseña) || nuevaContraseña.Length < 6)
        {
            ErrorMessage = "La contraseña debe tener al menos 6 caracteres.";
            return RedirectToPage(new { id = clienteId });
        }

        var usuario = await _db.UsuariosOficina
            .FirstOrDefaultAsync(u => u.Id == usuarioId && u.ClienteId == clienteId, ct);

        if (usuario == null)
        {
            ErrorMessage = "Usuario no encontrado.";
            return RedirectToPage(new { id = clienteId });
        }

        usuario.PasswordHash = BCrypt.Net.BCrypt.HashPassword(nuevaContraseña);
        await _db.SaveChangesAsync(ct);

        SuccessMessage = $"Contraseña actualizada para usuario {usuario.Usuario}.";
        return RedirectToPage(new { id = clienteId });
    }

    // ── POST Desactivar usuario cliente ───────────────────────────────────────

    public async Task<IActionResult> OnPostDesactivarUsuarioAsync(long clienteId, long usuarioId, CancellationToken ct)
    {
        var usuario = await _db.UsuariosOficina
            .FirstOrDefaultAsync(u => u.Id == usuarioId && u.ClienteId == clienteId, ct);

        if (usuario == null)
        {
            ErrorMessage = "Usuario no encontrado.";
            return RedirectToPage(new { id = clienteId });
        }

        usuario.Activo = false;
        await _db.SaveChangesAsync(ct);

        SuccessMessage = $"Usuario '{usuario.Usuario}' desactivado.";
        return RedirectToPage(new { id = clienteId });
    }

    // ── POST Reactivar usuario cliente ────────────────────────────────────────

    public async Task<IActionResult> OnPostReactivarUsuarioAsync(long clienteId, long usuarioId, CancellationToken ct)
    {
        var usuario = await _db.UsuariosOficina
            .FirstOrDefaultAsync(u => u.Id == usuarioId && u.ClienteId == clienteId, ct);

        if (usuario == null)
        {
            ErrorMessage = "Usuario no encontrado.";
            return RedirectToPage(new { id = clienteId });
        }

        usuario.Activo = true;
        await _db.SaveChangesAsync(ct);

        SuccessMessage = $"Usuario '{usuario.Usuario}' reactivado.";
        return RedirectToPage(new { id = clienteId });
    }

    // ── POST Asignar tenant ───────────────────────────────────────────────────

    public async Task<IActionResult> OnPostAsignarTenantAsync(long clienteId, int tenantId, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant == null) return NotFound();

        // Solo asignar si no tiene cliente ya
        if (tenant.ClienteId != null)
        {
            ErrorMessage = "Este tenant ya está asignado a otro cliente.";
            return RedirectToPage(new { id = clienteId });
        }

        tenant.ClienteId = clienteId;
        await _db.SaveChangesAsync(ct);

        SuccessMessage = $"Tenant '{tenant.Nombre}' asignado correctamente.";
        return RedirectToPage(new { id = clienteId });
    }

    // ── POST Desasignar tenant ────────────────────────────────────────────────

    public async Task<IActionResult> OnPostDesasignarTenantAsync(long clienteId, int tenantId, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId && t.ClienteId == clienteId, ct);
        if (tenant == null) return NotFound();

        tenant.ClienteId = null;
        await _db.SaveChangesAsync(ct);

        SuccessMessage = $"Tenant '{tenant.Nombre}' desasignado (ahora pertenece a la Oficina).";
        return RedirectToPage(new { id = clienteId });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task CargarDatosAsync(long clienteId, CancellationToken ct)
    {
        TenantsAsignados = await _db.Tenants
            .Where(t => t.ClienteId == clienteId)
            .OrderBy(t => t.Nombre)
            .Select(t => new TenantRow { Id = t.Id, Nombre = t.Nombre ?? "", Activo = t.Activo })
            .ToListAsync(ct);

        TenantsDisponibles = await _db.Tenants
            .Where(t => t.ClienteId == null)
            .OrderBy(t => t.Nombre)
            .Select(t => new TenantRow { Id = t.Id, Nombre = t.Nombre ?? "", Activo = t.Activo })
            .ToListAsync(ct);

        UsuariosCliente = await _db.UsuariosOficina
            .Where(u => u.ClienteId == clienteId && u.Rol == "Cliente")
            .OrderByDescending(u => u.Activo)
            .ThenBy(u => u.Usuario)
            .Select(u => new UsuarioClienteRow
            {
                Id      = u.Id,
                Usuario = u.Usuario,
                Nombre  = u.Nombre,
                Activo  = u.Activo,
                Creado  = u.Creado
            })
            .ToListAsync(ct);
    }

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

        // Eliminar logos anteriores de este cliente
        foreach (var viejo in Directory.GetFiles(logosDir, $"cliente_{clienteId}.*"))
            System.IO.File.Delete(viejo);

        var fileName = $"cliente_{clienteId}{ext}";
        var filePath = Path.Combine(logosDir, fileName);

        await using (var stream = new FileStream(filePath, FileMode.Create))
            await file.CopyToAsync(stream);

        return (true, $"/logos/{fileName}", null);
    }
}
