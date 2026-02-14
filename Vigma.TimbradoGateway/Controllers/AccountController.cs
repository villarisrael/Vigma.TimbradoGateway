using Microsoft.AspNetCore.Mvc;
using BCrypt.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

using System.Security.Claims;
using Vigma.TimbradoGateway.ViewsModels;
using Vigma.TimbradoGateway.Infrastructure.Repositories;

namespace Vigma.TimbradoGateway.Controllers
{
    

    public class AccountController : Controller
    {
        private readonly IRepoUsuariosOficina _repo;

        public AccountController(IRepoUsuariosOficina repo)
        {
            _repo = repo;
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

                // ✅ AQUÍ FALTABA EL LOGIN (cookie)
                var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Usuario),
            new Claim(ClaimTypes.Role, user.Rol ?? "Oficina")
        };

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
