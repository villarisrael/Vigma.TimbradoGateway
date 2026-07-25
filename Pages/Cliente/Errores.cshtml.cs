using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Vigma.TimbradoGateway.Infrastructure;
using Vigma.TimbradoGateway.Services;

namespace Vigma.TimbradoGateway.Pages.Cliente
{
    [Authorize(Roles = "Cliente")]
    public class ErroresModel : PageModel
    {
        private readonly TimbradoDbContext _context;
        private readonly IClienteScopeService _clienteScope;

        public ErroresModel(TimbradoDbContext context, IClienteScopeService clienteScope)
        {
            _context = context;
            _clienteScope = clienteScope;
        }

        public List<(int Id, string? Nombre)> Tenants { get; set; } = new();
        public List<(long Id, long TenantId, string? TenantName, string? RfcEmisor, string? CodigoMf, string? MensajeMf, DateTime FechaAlta)> Errores { get; set; } = new();
        public int ErroresCount { get; set; }
        public int ErroresHoy { get; set; }
        public string? ErrorMasFrecuente { get; set; }

        // Propiedades para filtros
        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }
        public string? CodigoMf { get; set; }
        public int? SelectedTenantId { get; set; }

        public async Task OnGetAsync(DateTime? fechaDesde, DateTime? fechaHasta, string? codigoMf, int? tenantId, CancellationToken ct)
        {
            try
            {
                // Asignar valores de filtros
                FechaDesde = fechaDesde;
                FechaHasta = fechaHasta;
                CodigoMf = codigoMf;
                SelectedTenantId = tenantId;

                var tenantIds = _clienteScope.GetAllowedTenantIds(User);

                if (!tenantIds.Any())
                    return;

                // Obtener tenants permitidos (sin cargar navegaciones)
                var tenants = await _context.Tenants
                    .AsNoTracking() // ✅ No cargar navegaciones
                    .Where(t => tenantIds.Contains((long)t.Id) && t.Activo)
                    .Select(t => new { t.Id, t.Nombre })
                    .ToListAsync(ct);

                Tenants = tenants.Select(t => (t.Id, t.Nombre)).ToList();

                // Construir consulta con filtros
                var query = _context.TimbradoErrorLogs
                    .AsQueryable();

                // Filtro por tenant
                if (tenantId.HasValue && tenantId.Value > 0)
                {
                    query = query.Where(t => t.TenantId == tenantId.Value);
                }
                else
                {
                    query = query.Where(t => tenantIds.Contains(t.TenantId));
                }

                // Filtro por fecha desde
                if (fechaDesde.HasValue)
                {
                    var fechaDesdeUtc = DateTime.SpecifyKind(fechaDesde.Value.Date, DateTimeKind.Utc);
                    query = query.Where(t => t.CreadoUtc >= fechaDesdeUtc);
                }

                // Filtro por fecha hasta
                if (fechaHasta.HasValue)
                {
                    var fechaHastaUtc = DateTime.SpecifyKind(fechaHasta.Value.Date.AddDays(1), DateTimeKind.Utc);
                    query = query.Where(t => t.CreadoUtc < fechaHastaUtc);
                }

                // Filtro por código de error
                if (!string.IsNullOrWhiteSpace(codigoMf))
                {
                    if (int.TryParse(codigoMf, out int codigoInt))
                    {
                        query = query.Where(t => t.CodigoMfNumero == codigoInt);
                    }
                }

                // Obtener últimos 100 errores
                var errores = await query
                    .OrderByDescending(t => t.CreadoUtc)
                    .Take(100)
                    .ToListAsync(ct);

                // Mapear con nombre del tenant
                var tenantMap = tenants.ToDictionary(t => (long)t.Id, t => t.Nombre);

                Errores = errores
                    .Select(t => (
                        t.Id,
                        t.TenantId,
                        TenantName: tenantMap.ContainsKey(t.TenantId) ? tenantMap[t.TenantId] : "Desconocido",
                        t.RfcEmisor,
                        CodigoMf: t.CodigoMfNumero?.ToString() ?? "N/A",
                        MensajeMf: t.CodigoMfTexto,
                        FechaAlta: t.CreadoUtc
                    ))
                    .ToList();

                // Estadísticas (con los filtros aplicados)
                ErroresCount = await _context.TimbradoErrorLogs
                    .Where(t => tenantIds.Contains(t.TenantId))
                    .CountAsync(ct);

                var hoy = DateTime.UtcNow.Date;
                ErroresHoy = await _context.TimbradoErrorLogs
                    .Where(t => tenantIds.Contains(t.TenantId) && t.CreadoUtc.Date == hoy)
                    .CountAsync(ct);

                // Error más frecuente
                var errorMasFrecuente = await _context.TimbradoErrorLogs
                    .Where(t => tenantIds.Contains(t.TenantId))
                    .GroupBy(t => t.CodigoMfNumero)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefaultAsync(ct);

                ErrorMasFrecuente = errorMasFrecuente?.Key?.ToString() ?? "N/A";
            }
            catch (Exception ex)
            {
                // Log error if needed
            }
        }
    }
}
