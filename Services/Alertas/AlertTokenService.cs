using Microsoft.EntityFrameworkCore;
using Vigma.TimbradoGateway.Infrastructure;
using Vigma.TimbradoGateway.Models.Alertas;
using Vigma.TimbradoGateway.Utils;

namespace Vigma.TimbradoGateway.Services.Alertas;

// ─────────────────────────────────────────────────────────────────────────────
//  AlertTokenService
//  Gestiona el registro, actualización y consulta de tokens FCM por tenant.
//  Cuando un trabajador registra su token:
//    - Si ya existe token activo para ese entidadId → lo actualiza
//    - Si no existe → crea uno nuevo
// ─────────────────────────────────────────────────────────────────────────────

public interface IAlertTokenService
{
    /// <summary>Registra o actualiza el token FCM de un trabajador.</summary>
    Task<RegisterTokenResponse> RegisterAsync(
        string apiKey,
        RegisterTokenRequest req,
        CancellationToken ct = default);

    /// <summary>Desactiva todos los tokens de un trabajador (por entidadId).</summary>
    Task DeactivateAsync(
        int tenantId,
        string entidadId,
        CancellationToken ct = default);

    /// <summary>Obtiene el token FCM activo de un trabajador.</summary>
    Task<FcmToken?> GetActiveTokenAsync(
        int tenantId,
        string entidadId,
        CancellationToken ct = default);

    /// <summary>Datos para el índice de tokens con filtros y paginación.</summary>
    Task<FcmTokenIndiceVM> GetIndiceAsync(
        FcmTokenFiltroVM filtro,
        CancellationToken ct = default);
}

public sealed class AlertTokenService : IAlertTokenService
{
    private readonly TimbradoDbContext _db;

    public AlertTokenService(TimbradoDbContext db) => _db = db;

    // ─── Registrar o actualizar token ────────────────────────────────────────
    public async Task<RegisterTokenResponse> RegisterAsync(
        string apiKey,
        RegisterTokenRequest req,
        CancellationToken ct = default)
    {
        // Validar request
        if (string.IsNullOrWhiteSpace(req.EntidadId))
            return new RegisterTokenResponse { ok = false, mensaje = "EntidadId es requerido." };

        if (string.IsNullOrWhiteSpace(req.Token))
            return new RegisterTokenResponse { ok = false, mensaje = "Token FCM es requerido." };

        // Resolver tenant por ApiKey
        var keyHash = HashHelper.Sha256(apiKey);
        var tenant  = await _db.Tenants
                               .FirstOrDefaultAsync(t => t.ApiKeyHash == keyHash && t.Activo, ct);

        if (tenant is null)
            return new RegisterTokenResponse { ok = false, mensaje = "API Key inválida o tenant inactivo." };

        // Buscar token activo existente para este trabajador
        var existing = await _db.FcmTokens
                                .FirstOrDefaultAsync(
                                    t => t.TenantId  == tenant.Id &&
                                         t.EntidadId == req.EntidadId.Trim() &&
                                         t.Activo,
                                    ct);

        if (existing is not null)
        {
            // Actualizar token existente
            existing.Token          = req.Token.Trim();
            existing.EntidadNombre  = req.EntidadNombre?.Trim() ?? existing.EntidadNombre;
            existing.ActualizadoUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            return new RegisterTokenResponse
            {
                ok      = true,
                tokenId = existing.Id,
                mensaje = "Token FCM actualizado correctamente."
            };
        }

        // Crear nuevo token
        var nuevo = new FcmToken
        {
            TenantId       = tenant.Id,
            EntidadId      = req.EntidadId.Trim(),
            EntidadNombre  = req.EntidadNombre?.Trim(),
            Token          = req.Token.Trim(),
            Activo         = true,
            CreadoUtc      = DateTime.UtcNow,
            ActualizadoUtc = DateTime.UtcNow
        };

        _db.FcmTokens.Add(nuevo);
        await _db.SaveChangesAsync(ct);

        return new RegisterTokenResponse
        {
            ok      = true,
            tokenId = nuevo.Id,
            mensaje = "Token FCM registrado correctamente."
        };
    }

    // ─── Desactivar tokens de un trabajador ──────────────────────────────────
    public async Task DeactivateAsync(int tenantId, string entidadId, CancellationToken ct = default)
    {
        var tokens = await _db.FcmTokens
                              .Where(t => t.TenantId  == tenantId &&
                                          t.EntidadId == entidadId &&
                                          t.Activo)
                              .ToListAsync(ct);

        foreach (var t in tokens)
        {
            t.Activo         = false;
            t.ActualizadoUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }

    // ─── Obtener token activo ─────────────────────────────────────────────────
    public async Task<FcmToken?> GetActiveTokenAsync(
        int tenantId,
        string entidadId,
        CancellationToken ct = default)
    {
        return await _db.FcmTokens
                        .FirstOrDefaultAsync(
                            t => t.TenantId  == tenantId &&
                                 t.EntidadId == entidadId &&
                                 t.Activo,
                            ct);
    }

    // ─── Índice con filtros y paginación ─────────────────────────────────────
    public async Task<FcmTokenIndiceVM> GetIndiceAsync(
        FcmTokenFiltroVM filtro,
        CancellationToken ct = default)
    {
        var query = _db.VwFcmTokens.AsQueryable();

        if (filtro.TenantId.HasValue)
            query = query.Where(x => x.TenantId == filtro.TenantId.Value);

        if (!string.IsNullOrWhiteSpace(filtro.EntidadId))
            query = query.Where(x => x.EntidadId.Contains(filtro.EntidadId.Trim()));

        if (!string.IsNullOrWhiteSpace(filtro.EntidadNombre))
            query = query.Where(x => x.EntidadNombre != null &&
                                     x.EntidadNombre.Contains(filtro.EntidadNombre.Trim()));

        if (filtro.Activo.HasValue)
            query = query.Where(x => x.Activo == filtro.Activo.Value);

        if (filtro.FechaDesde.HasValue)
            query = query.Where(x => x.CreadoUtc >= filtro.FechaDesde.Value);

        if (filtro.FechaHasta.HasValue)
            query = query.Where(x => x.CreadoUtc <= filtro.FechaHasta.Value.AddDays(1));

        var total    = await query.CountAsync(ct);
        var activos  = await query.CountAsync(x => x.Activo, ct);

        var rows = await query
            .OrderByDescending(x => x.ActualizadoUtc)
            .Skip((filtro.Pagina - 1) * filtro.TamanoPagina)
            .Take(filtro.TamanoPagina)
            .Select(x => new FcmTokenRowVM
            {
                Id                = x.Id,
                TenantId          = x.TenantId,
                TenantNombre      = x.TenantNombre,
                EntidadId         = x.EntidadId,
                EntidadNombre     = x.EntidadNombre,
                TokenPreview      = x.TokenPreview,
                Activo            = x.Activo,
                CreadoUtc         = x.CreadoUtc,
                ActualizadoUtc    = x.ActualizadoUtc,
                DiasSinActualizar = x.DiasSinActualizar
            })
            .ToListAsync(ct);

        return new FcmTokenIndiceVM
        {
            Tokens    = rows,
            Filtro    = filtro,
            Total     = total,
            Activos   = activos,
            Inactivos = total - activos
        };
    }
}
