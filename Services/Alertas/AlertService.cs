using Microsoft.EntityFrameworkCore;
using Vigma.TimbradoGateway.Infrastructure;
using Vigma.TimbradoGateway.Models.Alertas;
using Vigma.TimbradoGateway.Utils;

namespace Vigma.TimbradoGateway.Services.Alertas;

// ─────────────────────────────────────────────────────────────────────────────
//  AlertService
//  Orquesta el envío de alertas:
//    1. Valida ApiKey → obtiene Tenant
//    2. Busca token FCM activo del trabajador
//    3. Envía via FcmService
//    4. Graba log (siempre, sea éxito o fallo)
// ─────────────────────────────────────────────────────────────────────────────

public interface IAlertService
{
    Task<SendAlertResponse> SendAsync(
        string apiKey,
        SendAlertRequest req,
        CancellationToken ct = default);

    /// <summary>Datos para el índice de logs con filtros y paginación.</summary>
    Task<AlertLogIndiceVM> GetLogsAsync(
        AlertLogFiltroVM filtro,
        CancellationToken ct = default);
}

public sealed class AlertService : IAlertService
{
    private readonly TimbradoDbContext  _db;
    private readonly IFcmService        _fcm;

    public AlertService(TimbradoDbContext db, IFcmService fcm)
    {
        _db  = db;
        _fcm = fcm;
    }

    // ─── Enviar alerta ────────────────────────────────────────────────────────
    public async Task<SendAlertResponse> SendAsync(
        string apiKey,
        SendAlertRequest req,
        CancellationToken ct = default)
    {
        // 1. Validaciones básicas
        if (string.IsNullOrWhiteSpace(req.EntidadId))
            return Fail("EntidadId es requerido.");

        if (string.IsNullOrWhiteSpace(req.Title))
            return Fail("Title es requerido.");

        if (string.IsNullOrWhiteSpace(req.Message))
            return Fail("Message es requerido.");

        // 2. Resolver tenant
        var keyHash = HashHelper.Sha256(apiKey);
        var tenant  = await _db.Tenants
                               .FirstOrDefaultAsync(t => t.ApiKeyHash == keyHash && t.Activo, ct);

        if (tenant is null)
            return Fail("API Key inválida o tenant inactivo.");

        // 3. Buscar token FCM activo del trabajador
        var fcmToken = await _db.FcmTokens
                                .FirstOrDefaultAsync(
                                    t => t.TenantId  == tenant.Id &&
                                         t.EntidadId == req.EntidadId.Trim() &&
                                         t.Activo,
                                    ct);

        if (fcmToken is null)
            return await LogAndReturn(tenant.Id, req, tokenSnapshot: null,
                status: "failed",
                error: $"No se encontró token FCM activo para entidadId='{req.EntidadId}'.");

        // 4. Enviar via Firebase
        string?  firebaseMsgId = null;
        string?  errorDetail   = null;
        string   status        = "sent";


        var fcmData = new Dictionary<string, string>
        {
            ["origin"] = req.Origin?.Trim() ?? ""
        };

        // Mezclar con el data extra que mande el cliente
        if (req.Data is not null)
            foreach (var kv in req.Data)
                fcmData[kv.Key] = kv.Value;


        try
        {
            firebaseMsgId = await _fcm.SendAsync(
                fcmToken:  fcmToken.Token,
                title:     req.Title,
                message:   req.Message,
                priority:  req.Priority,
                data:      req.Data,
                ct:        ct);
        }
        catch (Exception ex)
        {
            status      = "failed";
            errorDetail = ex.Message;
        }

        // 5. Grabar log (siempre)
        var log = new AlertLog
        {
            TenantId      = tenant.Id,
            EntidadId     = req.EntidadId.Trim(),
            EntidadNombre = fcmToken.EntidadNombre,
            Origin        = req.Origin?.Trim() ?? "",
            Title         = req.Title.Trim(),
            Message       = req.Message.Trim(),
            FcmToken      = fcmToken.Token,          // snapshot del token usado
            Status        = status,
            FirebaseMsgId = firebaseMsgId,
            ErrorDetail   = errorDetail,
            SentAt        = DateTime.UtcNow
        };

        _db.AlertLogs.Add(log);
        await _db.SaveChangesAsync(ct);

        if (status == "failed")
            return new SendAlertResponse
            {
                ok      = false,
                logId   = log.Id,
                error   = errorDetail,
                mensaje = "Fallo al enviar la alerta."
            };

        return new SendAlertResponse
        {
            ok            = true,
            logId         = log.Id,
            firebaseMsgId = firebaseMsgId,
            mensaje       = "Alerta enviada correctamente."
        };
    }

    // ─── Índice de logs con filtros ───────────────────────────────────────────
    public async Task<AlertLogIndiceVM> GetLogsAsync(
        AlertLogFiltroVM filtro,
        CancellationToken ct = default)
    {
        var query = _db.VwAlertLogs.AsQueryable();

        if (filtro.TenantId.HasValue)
            query = query.Where(x => x.TenantId == filtro.TenantId.Value);

        if (!string.IsNullOrWhiteSpace(filtro.EntidadId))
            query = query.Where(x => x.EntidadId.Contains(filtro.EntidadId.Trim()));

        if (!string.IsNullOrWhiteSpace(filtro.EntidadNombre))
            query = query.Where(x => x.EntidadNombre != null &&
                                     x.EntidadNombre.Contains(filtro.EntidadNombre.Trim()));

        if (!string.IsNullOrWhiteSpace(filtro.Origin))
            query = query.Where(x => x.Origin.Contains(filtro.Origin.Trim()));

        if (!string.IsNullOrWhiteSpace(filtro.Status))
            query = query.Where(x => x.Status == filtro.Status.Trim());

        if (filtro.FechaDesde.HasValue)
            query = query.Where(x => x.SentAt >= filtro.FechaDesde.Value);

        if (filtro.FechaHasta.HasValue)
            query = query.Where(x => x.SentAt <= filtro.FechaHasta.Value.AddDays(1));

        var total = await query.CountAsync(ct);

        var rows = await query
            .OrderByDescending(x => x.SentAt)
            .Skip((filtro.Pagina - 1) * filtro.TamanoPagina)
            .Take(filtro.TamanoPagina)
            .Select(x => new AlertLogRowVM
            {
                Id            = x.Id,
                TenantId      = x.TenantId,
                TenantNombre  = x.TenantNombre,
                EntidadId     = x.EntidadId,
                EntidadNombre = x.EntidadNombre,
                Origin        = x.Origin,
                Title         = x.Title,
                Message       = x.Message,
                Status        = x.Status,
                ErrorDetail   = x.ErrorDetail,
                FirebaseMsgId = x.FirebaseMsgId,
                SentAt        = x.SentAt,
                Fecha         = x.Fecha,
                Hora          = x.Hora,
                EnviadoOk     = x.EnviadoOk
            })
            .ToListAsync(ct);

        return new AlertLogIndiceVM
        {
            Logs   = rows,
            Filtro = filtro,
            Total  = total
        };
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────
    private static SendAlertResponse Fail(string error) =>
        new() { ok = false, error = error, mensaje = error };

    private async Task<SendAlertResponse> LogAndReturn(
        int tenantId,
        SendAlertRequest req,
        string? tokenSnapshot,
        string status,
        string error)
    {
        var log = new AlertLog
        {
            TenantId      = tenantId,
            EntidadId     = req.EntidadId.Trim(),
            Origin        = req.Origin?.Trim() ?? "",
            Title         = req.Title?.Trim()  ?? "",
            Message       = req.Message?.Trim() ?? "",
            FcmToken      = tokenSnapshot,
            Status        = status,
            ErrorDetail   = error,
            SentAt        = DateTime.UtcNow
        };

        try
        {
            _db.AlertLogs.Add(log);
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            // Temporal para debug
            var inner = ex.InnerException?.Message ?? ex.Message;
            return new SendAlertResponse
            {
                ok = false,
                mensaje = inner  // ver el error real
            };
        }

        return new SendAlertResponse
        {
            ok      = false,
            logId   = log.Id,
            error   = error,
            mensaje = error
        };
    }
}
