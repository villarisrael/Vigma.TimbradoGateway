using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Vigma.TimbradoGateway.DTOs;
using Vigma.TimbradoGateway.Infrastructure;
using Vigma.TimbradoGateway.Models.Logs;
using Vigma.TimbradoGateway.Utils;

namespace Vigma.TimbradoGateway.Services;

public interface ICancelacionService
{
    /// <summary>Cancelación vía API Key (endpoint externo POST /v1/cancelar)</summary>
    Task<CancelacionResponse> CancelarAsync(
        string apiKey,
        CancelacionRequest req,
        CancellationToken ct = default);

    /// <summary>Cancelación vía TenantId directo (uso interno desde el monitor web)</summary>
    Task<CancelacionResponse> CancelarPorTenantIdAsync(
        long tenantId,
        CancelacionRequest req,
        CancellationToken ct = default);
}

public sealed class CancelacionService : ICancelacionService
{
    private readonly ITenantConfigService _tenantCfg;
    private readonly CryptoService _crypto;
    private readonly IMultiFacturasCancelacionSoapClient _soapClient;
    private readonly TimbradoDbContext _db;
    private readonly ILogger<CancelacionService> _log;

    public CancelacionService(
        ITenantConfigService tenantCfg,
        CryptoService crypto,
        IMultiFacturasCancelacionSoapClient soapClient,
        TimbradoDbContext db,
        ILogger<CancelacionService> log)
    {
        _tenantCfg = tenantCfg;
        _crypto = crypto;
        _soapClient = soapClient;
        _db = db;
        _log = log;
    }

    public async Task<CancelacionResponse> CancelarPorTenantIdAsync(
        long tenantId,
        CancelacionRequest req,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.RfcEmisor))
            throw new ArgumentException("El campo rfcEmisor es requerido.");
        if (string.IsNullOrWhiteSpace(req.Uuid))
            throw new ArgumentException("El campo uuid es requerido.");
        if (req.Motivo == "01" && string.IsNullOrWhiteSpace(req.UuidSustitucion))
            throw new ArgumentException("Motivo 01 requiere uuidSustitucion.");

        // Resolver tenant + cert por ID (sin necesitar API Key)
        var (tenant, cert) = await _tenantCfg.GetByTenantIdAsync(tenantId, req.RfcEmisor);

        return await EjecutarCancelacionAsync(tenant, cert, req, ct);
    }

    public async Task<CancelacionResponse> CancelarAsync(
        string apiKey,
        CancelacionRequest req,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new UnauthorizedAccessException("Falta API Key (X-Api-Key).");

        if (string.IsNullOrWhiteSpace(req.RfcEmisor))
            throw new ArgumentException("El campo rfcEmisor es requerido.");
        if (string.IsNullOrWhiteSpace(req.Uuid))
            throw new ArgumentException("El campo uuid es requerido.");
        if (string.IsNullOrWhiteSpace(req.Motivo))
            throw new ArgumentException("El campo motivo es requerido.");
        if (req.Motivo == "01" && string.IsNullOrWhiteSpace(req.UuidSustitucion))
            throw new ArgumentException("Motivo 01 requiere uuidSustitucion.");

        var (tenant, cert) = await _tenantCfg.GetByApiKeyAsync(apiKey, req.RfcEmisor);

        return await EjecutarCancelacionAsync(tenant, cert, req, ct);
    }

    // ── Lógica compartida ─────────────────────────────────────────────────────

    private async Task<CancelacionResponse> EjecutarCancelacionAsync(
        Vigma.TimbradoGateway.Models.Tenant tenant,
        Vigma.TimbradoGateway.Models.Certificado cert,
        CancelacionRequest req,
        CancellationToken ct)
    {
        _log.LogWarning("══════ CANCELACION SERVICE INICIO ══════");
        _log.LogWarning("SVC → TenantId={TenantId}, RFC={Rfc}, UUID={Uuid}, Motivo={Motivo}",
            tenant.Id, req.RfcEmisor, req.Uuid, req.Motivo);

        // Credenciales PAC
        _log.LogWarning("SVC → Descifrando pacPass...");
        var pacPass = string.IsNullOrWhiteSpace(tenant.PacPasswordEnc)
            ? ""
            : _crypto.DecryptFromBase64(tenant.PacPasswordEnc);
        _log.LogWarning("SVC → pacPass len={Len}", pacPass?.Length ?? 0);

        // Certificado
        if (string.IsNullOrWhiteSpace(cert.CerPath) || string.IsNullOrWhiteSpace(cert.KeyPath))
            throw new ArgumentException("El certificado del tenant no tiene cer_path / key_path configurado.");

        _log.LogWarning("SVC → Leyendo CER: {Path}", cert.CerPath);
        var cerBytes = await File.ReadAllBytesAsync(cert.CerPath, ct);
        _log.LogWarning("SVC → Leyendo KEY: {Path}", cert.KeyPath);
        var keyBytes = await File.ReadAllBytesAsync(cert.KeyPath, ct);
        var cerB64 = Convert.ToBase64String(cerBytes);
        var keyB64 = Convert.ToBase64String(keyBytes);
        _log.LogWarning("SVC → CER b64 len={CerLen}, KEY b64 len={KeyLen}", cerB64.Length, keyB64.Length);

        string keyPass = "";
        if (!string.IsNullOrWhiteSpace(cert.KeyPasswordEnc))
        {
            var s = cert.KeyPasswordEnc.Trim();
            try { keyPass = _crypto.DecryptFromBase64(s); }
            catch { keyPass = s; }
        }
        _log.LogWarning("SVC → keyPass len={Len}", keyPass.Length);

        // Construir datos SOAP — misma estructura que WSCancelarFactura40.datos en VB.NET
        _log.LogWarning("SVC → Construyendo CancelacionSoapDatos...");
        var datos = new CancelacionSoapDatos
        {
            Accion = "cancelar",
            B64Cer = cerB64,
            B64Key = keyB64,
            Motivo = req.Motivo,
            Pass = pacPass,
            Password = keyPass,
            Produccion = tenant.PacProduccion ? "SI" : "NO",
            Usuario = tenant.PacUsuario ?? "",
            Uuid = req.Uuid,
            FolioSustitucion = req.UuidSustitucion ?? "",
            Rfc = req.RfcEmisor
        };

        // Llamar al PAC vía SOAP (igual que VB.NET)
        _log.LogWarning("SVC → Llamando _soapClient.CancelarCfdiAsync...");
        var sw = Stopwatch.StartNew();
        string raw;
        try
        {
            raw = await _soapClient.CancelarCfdiAsync(datos, ct);
            _log.LogWarning("SVC → SOAP respondió en {Ms}ms, raw len={Len}", sw.ElapsedMilliseconds, raw?.Length ?? 0);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "SVC → EXCEPCION en SOAP: {Type} - {Msg}", ex.GetType().Name, ex.Message);
            await GuardarLogAsync(tenant.Id, req, "ERROR", null, ex.Message, null, null,
                tenant.PacProduccion ? "SI" : "NO", (int)sw.ElapsedMilliseconds, ct);
            throw;
        }
        sw.Stop();

        // Parsear respuesta SOAP XML
        _log.LogWarning("SVC → Parseando respuesta SOAP...");
        var (ok, codigo, mensaje) = ParseSoapResponse(raw);
        _log.LogWarning("SVC → Resultado: ok={Ok}, codigo={Codigo}, mensaje={Msg}", ok, codigo, mensaje);
        _log.LogWarning("══════ CANCELACION SERVICE FIN ══════");
        var resultado = ok ? "CANCELADO" : "RECHAZADO";

        if (ok)
            await MarcarCanceladaEnOkLogAsync(req.Uuid, ct);

        var logId = await GuardarLogAsync(
            tenantId: tenant.Id,
            req: req,
            resultado: resultado,
            codigoMf: codigo,
            mensajeMf: mensaje,
            jsonEnviado: null,
            rawPac: raw,
            mfProduccion: tenant.PacProduccion ? "SI" : "NO",
            duracionMs: (int)sw.ElapsedMilliseconds,
            ct: ct);

        return new CancelacionResponse
        {
            Ok = ok,
            Codigo = codigo,
            Mensaje = mensaje,
            Uuid = req.Uuid,
            RawPac = ok ? null : raw,
            LogId = logId
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Parsea la respuesta SOAP XML de Multifacturas cancelación.
    /// La respuesta tiene elementos hijos dentro de "return":
    ///   codigo_mf_numero, codigo_mf_texto, mensaje_original, etc.
    /// </summary>
    private static (bool ok, string codigo, string mensaje) ParseSoapResponse(string rawXml)
    {
        try
        {
            var doc = XDocument.Parse(rawXml);

            // Buscar el elemento "return" dentro del SOAP Body
            var returnEl = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "return");

            if (returnEl != null)
            {
                // Leer hijos directos por nombre local
                var codigoMfNumero = returnEl.Elements()
                    .FirstOrDefault(e => e.Name.LocalName == "codigo_mf_numero")?.Value?.Trim() ?? "";
                var codigoMfTexto = returnEl.Elements()
                    .FirstOrDefault(e => e.Name.LocalName == "codigo_mf_texto")?.Value?.Trim() ?? "";
                var mensajeOriginal = returnEl.Elements()
                    .FirstOrDefault(e => e.Name.LocalName == "mensaje_original")?.Value?.Trim() ?? "";

                // codigo_mf_numero == "0" significa éxito
                var ok = codigoMfNumero == "0";

                // Para el mensaje, usar codigo_mf_texto (corto) — el mensaje_original es muy largo
                var mensaje = !string.IsNullOrWhiteSpace(codigoMfTexto) ? codigoMfTexto : "Sin mensaje";

                return (ok, codigoMfNumero, mensaje);
            }

            // Fallback: buscar Fault
            var faultString = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "faultstring")?.Value;
            if (!string.IsNullOrWhiteSpace(faultString))
                return (false, "FAULT", faultString);

            return (false, "UNKNOWN", rawXml.Length > 500 ? rawXml[..500] : rawXml);
        }
        catch
        {
            return (false, "PARSE_ERROR", rawXml.Length > 500 ? rawXml[..500] : rawXml);
        }
    }

    private async Task MarcarCanceladaEnOkLogAsync(string uuid, CancellationToken ct)
    {
        try
        {
            var row = await _db.TimbradoOkLogs
                .FirstOrDefaultAsync(x => x.Uuid == uuid, ct);
            if (row != null)
            {
                row.Cancelada = true;
                await _db.SaveChangesAsync(ct);
            }
        }
        catch
        {
            // No tumbamos la respuesta si el log falla
        }
    }

    private async Task<long> GuardarLogAsync(
        long tenantId, CancelacionRequest req,
        string resultado, string? codigoMf, string? mensajeMf,
        string? jsonEnviado, string? rawPac,
        string? mfProduccion, int duracionMs, CancellationToken ct)
    {
        try
        {
            // Truncar campos para evitar "Data too long" en MySQL
            static string? Truncate(string? s, int max) =>
                s != null && s.Length > max ? s[..max] : s;

            var log = new CancelacionLog
            {
                TenantId = tenantId,
                RfcEmisor = req.RfcEmisor,
                Uuid = req.Uuid,
                Motivo = req.Motivo,
                UuidSustitucion = req.UuidSustitucion,
                Resultado = resultado,
                CodigoMf = Truncate(codigoMf, 50),
                MensajeMf = Truncate(mensajeMf, 500),
                JsonEnviado = jsonEnviado,
                RawPac = rawPac,
                MfProduccion = mfProduccion,
                DuracionMs = duracionMs,
                CreadoUtc = TimbradoLogService.MexicoNow()
            };

            _db.CancelacionLogs.Add(log);
            await _db.SaveChangesAsync(ct);
            return log.Id;
        }
        catch
        {
            return 0;
        }
    }
}
