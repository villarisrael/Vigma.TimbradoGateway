using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using Vigma.TimbradoGateway.Infrastructure;

namespace Vigma.TimbradoGateway.Pages.Cliente;

[Authorize(Roles = "Cliente")]
public class CambiarContraseñaModel : PageModel
{
    private readonly TimbradoDbContext _db;

    public CambiarContraseñaModel(TimbradoDbContext db)
    {
        _db = db;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? UsuarioEmail { get; set; }

    [TempData] public string? SuccessMessage { get; set; }

    public class InputModel
    {
        [Required(ErrorMessage = "La contraseña actual es obligatoria.")]
        [Display(Name = "Contraseña Actual")]
        public string ContraseñaActual { get; set; } = "";

        [Required(ErrorMessage = "La nueva contraseña es obligatoria.")]
        [MinLength(6, ErrorMessage = "La nueva contraseña debe tener al menos 6 caracteres.")]
        [Display(Name = "Contraseña Nueva")]
        public string ContraseñaNueva { get; set; } = "";

        [Required(ErrorMessage = "La confirmación es obligatoria.")]
        [Display(Name = "Confirmar Contraseña")]
        public string Confirmar { get; set; } = "";
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var email = User.Identity?.Name;
        var usuario = await _db.UsuariosOficina
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Usuario == email, ct);

        if (usuario == null) return NotFound();

        UsuarioEmail = usuario.Usuario;
        return Page();
    }

    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return Page();

        var email = User.Identity?.Name;
        var usuario = await _db.UsuariosOficina
            .FirstOrDefaultAsync(u => u.Usuario == email, ct);

        if (usuario == null) return NotFound();

        UsuarioEmail = usuario.Usuario;

        // Validar contraseña actual
        if (!BCrypt.Net.BCrypt.Verify(Input.ContraseñaActual, usuario.PasswordHash))
        {
            ModelState.AddModelError("Input.ContraseñaActual", "La contraseña actual es incorrecta.");
            return Page();
        }

        // Validar que la nueva sea diferente
        if (BCrypt.Net.BCrypt.Verify(Input.ContraseñaNueva, usuario.PasswordHash))
        {
            ModelState.AddModelError("Input.ContraseñaNueva", "La nueva contraseña debe ser diferente a la actual.");
            return Page();
        }

        // Validar que confirmación coincida
        if (Input.ContraseñaNueva != Input.Confirmar)
        {
            ModelState.AddModelError("Input.Confirmar", "La confirmación no coincide con la nueva contraseña.");
            return Page();
        }

        usuario.PasswordHash = BCrypt.Net.BCrypt.HashPassword(Input.ContraseñaNueva);
        await _db.SaveChangesAsync(ct);

        SuccessMessage = "Tu contraseña ha sido actualizada correctamente.";
        return RedirectToPage();
    }
}
