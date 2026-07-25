using Microsoft.AspNetCore.Mvc;
using BCrypt.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

using System.Security.Claims;
using Vigma.TimbradoGateway.ViewsModels;
using Vigma.TimbradoGateway.Infrastructure.Repositories;
using Vigma.TimbradoGateway.Infrastructure;

namespace Vigma.TimbradoGateway.Controllers
{


    public class AccountController : Controller
    {
        private readonly IRepoUsuariosOficina _repo;
        private readonly TimbradoDbContext _context;
        private readonly ILogger<AccountController> _logger;

        public AccountController(IRepoUsuariosOficina repo, TimbradoDbContext context, ILogger<AccountController> logger)
        {
            _repo = repo;
            _context = context;
            _logger = logger;
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
            => View(new LoginVM { ReturnUrl = returnUrl });

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginVM vm, CancellationToken ct)
        {
            try
            {
                if (!ModelState.IsValid) return View(vm);

                var user = await _repo.GetByUsuarioAsync(vm.Usuario.Trim(), ct);

                if (user == null || !user.Activo)
                {
                    ModelState.AddModelError("", "Usuario o contraseña inválidos.");
                    return View(vm);
                }

                if (string.IsNullOrWhiteSpace(user.PasswordHash))
                {
                    ModelState.AddModelError("", "Usuario sin contraseña configurada (PasswordHash vacío).");
                    return View(vm);
                }

                var ok = BCrypt.Net.BCrypt.Verify(vm.Password, user.PasswordHash);
                if (!ok)
                {
                    ModelState.AddModelError("", "Usuario o contraseña inválidos.");
                    return View(vm);
                }

                // ✅ CREAR CLAIMS PARA EL USUARIO
                var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Usuario),
            new Claim(ClaimTypes.Role, user.Rol ?? "Oficina")
        };

                // ✅ NUEVO: Si el usuario es "Cliente", cargar sus tenants permitidos
                if (user.Rol == "Cliente" && user.ClienteId.HasValue)
                {
                    // Cargar tenants del cliente (convertir a long)
                    var tenantIds = await _context.Tenants
                        .Where(t => t.ClienteId == user.ClienteId && t.Activo)
                        .Select(t => (long)t.Id)
                        .ToListAsync(ct);

                    // Agregar claims
                    claims.Add(new Claim("ClienteId", user.ClienteId.ToString()));
                    claims.Add(new Claim("TenantIds", string.Join(",", tenantIds)));
                }

                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identity);

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    principal,
                    new AuthenticationProperties
                    {
                        IsPersistent = vm.Recordarme,
                        ExpiresUtc = DateTimeOffset.UtcNow.AddHours(vm.Recordarme ? 72 : 12)
                    });

                if (!string.IsNullOrWhiteSpace(vm.ReturnUrl) && Url.IsLocalUrl(vm.ReturnUrl))
                    return LocalRedirect(vm.ReturnUrl);

                // ✅ DEBUG: Log del rol antes de redirigir
                _logger.LogInformation($"✅ LOGIN SUCCESS - Usuario: {user.Usuario}, Rol: {user.Rol}, ClienteId: {user.ClienteId}");

                // ✅ NUEVO: Redirigir a /Cliente/Dashboard si es cliente
                if (user.Rol == "Cliente")
                {
                    _logger.LogInformation($"🔸 REDIRIGIENDO A CLIENTE DASHBOARD");
                    return Redirect("~/Cliente/Dashboard");
                }

                _logger.LogInformation($"🔸 REDIRIGIENDO A HOME");
                return RedirectToAction("Index", "Home");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"ERROR: {ex.GetType().Name} - {ex.Message}");
                return View(vm);
            }
        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login", "Account");
        }

        [HttpGet]
        public IActionResult Denied() => View();
    }

}
