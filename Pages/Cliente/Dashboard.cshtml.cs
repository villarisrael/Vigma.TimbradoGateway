using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Vigma.TimbradoGateway.Infrastructure;
using Vigma.TimbradoGateway.Models;
using Vigma.TimbradoGateway.Services;

namespace Vigma.TimbradoGateway.Pages.Cliente
{
    [Authorize(Roles = "Cliente")]
    public class DashboardModel : PageModel
    {
        private readonly TimbradoDbContext _context;
        private readonly IClienteScopeService _clienteScope;

        public DashboardModel(TimbradoDbContext context, IClienteScopeService clienteScope)
        {
            _context = context;
            _clienteScope = clienteScope;
        }

        public Models.Cliente? Cliente { get; set; }
        public List<(int Id, string? Nombre, bool Activo)> Tenants { get; set; } = new();
        public int TenantCount { get; set; }
        public int TimbradosCount { get; set; }
        public int ErroresCount { get; set; }

        public async Task OnGetAsync(CancellationToken ct)
        {
            try
            {
                var tenantIds = _clienteScope.GetAllowedTenantIds(User);

                if (!tenantIds.Any())
                    return;

                // Obtener cliente info
                var clienteId = _clienteScope.GetClienteId(User);
                Cliente = await _context.Clientes.FindAsync(new object[] { clienteId }, cancellationToken: ct);

                // Obtener tenants del cliente
                var tenants = await _context.Tenants
                    .Where(t => tenantIds.Contains((long)t.Id) && t.Activo)
                    .ToListAsync(ct);

                Tenants = tenants
                    .Select(t => (t.Id, t.Nombre, t.Activo))
                    .ToList();

                TenantCount = tenants.Count;

                // Contar timbrados y errores de hoy
                var hoy = DateTime.UtcNow.Date;

                TimbradosCount = await _context.TimbradoOkLogs
                    .Where(t => tenantIds.Contains(t.TenantId) && t.created_utc.Date == hoy)
                    .CountAsync(ct);

                ErroresCount = await _context.TimbradoErrorLogs
                    .Where(t => tenantIds.Contains(t.TenantId) && t.CreadoUtc.Date == hoy)
                    .CountAsync(ct);
            }
            catch (Exception ex)
            {
                // Log error if needed
            }
        }
    }
}
