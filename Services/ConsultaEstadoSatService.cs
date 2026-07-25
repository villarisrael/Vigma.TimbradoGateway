using System;
using System.Threading;
using System.Threading.Tasks;
using Vigma.TimbradoGateway.DTOs;
using Vigma.TimbradoGateway.Models.Facturalo;

namespace Vigma.TimbradoGateway.Services;

public interface IConsultaEstadoSatService
{
    /// <summary>
    /// Consulta el estado del CFDI ante el SAT vía FacturaLO PLUS.
    /// Valida que la API Key del tenant sea correcta y que el tenant tenga
    /// configurada la API Key de FacturaLO (independientemente del PAC activo).
    /// </summary>
    Task<consultarEstadoSATResponse> ConsultarAsync(
        string apiKey,
        ConsultaEstadoSatRequest req,
        CancellationToken ct = default);
}

public sealed class ConsultaEstadoSatService : IConsultaEstadoSatService
{
    private readonly ITenantConfigService _tenantCfg;
    private readonly IFacturaloClient _facturalo;

    public ConsultaEstadoSatService(
        ITenantConfigService tenantCfg,
        IFacturaloClient facturalo)
    {
        _tenantCfg = tenantCfg;
        _facturalo = facturalo;
    }

    public async Task<consultarEstadoSATResponse> ConsultarAsync(
        string apiKey,
        ConsultaEstadoSatRequest req,
        CancellationToken ct = default)
    {
        // 1) Validaciones de entrada
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new UnauthorizedAccessException("Falta API Key (X-Api-Key).");

        if (req is null)
            throw new ArgumentException("El body de la petición es requerido.");

        if (string.IsNullOrWhiteSpace(req.Uuid))
            throw new ArgumentException("El campo 'uuid' es requerido.");
        if (string.IsNullOrWhiteSpace(req.RfcEmisor))
            throw new ArgumentException("El campo 'rfcEmisor' es requerido.");
        if (string.IsNullOrWhiteSpace(req.RfcReceptor))
            throw new ArgumentException("El campo 'rfcReceptor' es requerido.");
        if (string.IsNullOrWhiteSpace(req.Total))
            throw new ArgumentException("El campo 'total' es requerido.");

        // 2) Resolver tenant por API Key + RFC emisor (también valida que el RFC
        //    pertenezca al tenant: si no, falla con "No hay certificado registrado").
        var (tenant, _) = await _tenantCfg.GetByApiKeyAsync(apiKey, req.RfcEmisor!.Trim());

        // 3) Verificar que el tenant tenga API Key de FacturaLO para el ambiente activo
        var apikeyFl = tenant.PacApikeyFacturaloActiva;
        if (string.IsNullOrWhiteSpace(apikeyFl))
            throw new InvalidOperationException(
                $"Este tenant no tiene configurada la API Key de FacturaLO PLUS para el ambiente " +
                $"{(tenant.PacProduccion ? "PRODUCCIÓN" : "PRUEBAS")}. " +
                "La consulta de estado SAT solo está disponible para tenants con FacturaLO.");

        // 4) Llamar a FacturaLO — consultarEstadoSAT
        var resp = await _facturalo.ConsultarEstadoSatAsync(
            apikey:      apikeyFl!,
            uuid:        req.Uuid!.Trim(),
            rfcEmisor:   req.RfcEmisor!.Trim(),
            rfcReceptor: req.RfcReceptor!.Trim(),
            total:       req.Total!.Trim(),
            produccion:  tenant.PacProduccion,
            ct:          ct);

        // 5) Mapear al modelo público
        return new consultarEstadoSATResponse
        {
            CodigoEstatus      = resp.CodigoEstatus,
            EsCancelable       = resp.EsCancelable,
            Estado             = resp.Estado,
            EstatusCancelacion = resp.EstatusCancelacion
        };
    }
}
