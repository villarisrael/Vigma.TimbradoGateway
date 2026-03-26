using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Vigma.TimbradoGateway.Services;

/// <summary>
/// Cliente SOAP para cancelación CFDI 4.0 vía Multifacturas.
/// Replica exactamente la llamada que hace el web service WSCancelarFactura40
/// de VB.NET (operación cancelarCfdi con objeto "datos").
/// Endpoint: http://pac1.multifacturas.com/cancelacion2022/index.php
/// </summary>
public interface IMultiFacturasCancelacionSoapClient
{
    Task<string> CancelarCfdiAsync(CancelacionSoapDatos datos, CancellationToken ct = default);
}

/// <summary>
/// Datos que se envían al SOAP cancelarCfdi — misma estructura que
/// WSCancelarFactura40.datos en VB.NET.
/// </summary>
public sealed class CancelacionSoapDatos
{
    public string Accion { get; set; } = "cancelar";
    public string B64Cer { get; set; } = "";
    public string B64Key { get; set; } = "";
    public string Motivo { get; set; } = "02";
    public string Pass { get; set; } = "";       // PAC password
    public string Password { get; set; } = "";    // Key/cert password
    public string Produccion { get; set; } = "SI";
    public string Usuario { get; set; } = "";     // PAC usuario
    public string Uuid { get; set; } = "";
    public string FolioSustitucion { get; set; } = "";
    public string Rfc { get; set; } = "";
}

public sealed class MultiFacturasCancelacionSoapClient : IMultiFacturasCancelacionSoapClient
{
    private readonly HttpClient _http;
    private readonly ILogger<MultiFacturasCancelacionSoapClient> _log;

    // URL del WSDL de cancelación de Multifacturas
    private const string Endpoint = "http://pac1.multifacturas.com/cancelacion2022/index.php";
    // Namespace del WSDL (urn:wservicewsdl es el estándar de Multifacturas)
    private const string WsNamespace = "urn:wservicewsdl";

    public MultiFacturasCancelacionSoapClient(HttpClient http, ILogger<MultiFacturasCancelacionSoapClient> log)
    {
        _http = http;
        _log = log;
    }

    public async Task<string> CancelarCfdiAsync(CancelacionSoapDatos datos, CancellationToken ct = default)
    {
        _log.LogWarning("══════ SOAP CANCELAR INICIO ══════");
        _log.LogWarning("SOAP → Endpoint: {Endpoint}", Endpoint);
        _log.LogWarning("SOAP → UUID: {Uuid}", datos.Uuid);
        _log.LogWarning("SOAP → RFC: {Rfc}", datos.Rfc);
        _log.LogWarning("SOAP → Motivo: {Motivo}", datos.Motivo);
        _log.LogWarning("SOAP → Produccion: {Prod}", datos.Produccion);
        _log.LogWarning("SOAP → Usuario: {Usr}", datos.Usuario);
        _log.LogWarning("SOAP → b64Cer len: {Len}", datos.B64Cer?.Length ?? 0);
        _log.LogWarning("SOAP → b64Key len: {Len}", datos.B64Key?.Length ?? 0);
        _log.LogWarning("SOAP → Password len: {Len}", datos.Password?.Length ?? 0);

        // Construir SOAP envelope
        var soap = BuildSoapEnvelope(datos);
        _log.LogWarning("SOAP → Envelope construido, len={Len}", soap.Length);

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            req.Content = new StringContent(soap, Encoding.UTF8, "text/xml");
            req.Headers.TryAddWithoutValidation("SOAPAction", $"{WsNamespace}#cancelarCfdi");

            _log.LogWarning("SOAP → Enviando HTTP POST...");

            using var resp = await _http.SendAsync(req, ct);

            _log.LogWarning("SOAP → HTTP StatusCode: {Code}", (int)resp.StatusCode);

            var raw = await resp.Content.ReadAsStringAsync(ct);

            _log.LogWarning("SOAP → Respuesta len={Len}", raw?.Length ?? 0);
            // Mostrar los primeros 1000 chars de la respuesta para debug
            var preview = raw != null && raw.Length > 1000 ? raw[..1000] : raw;
            _log.LogWarning("SOAP → Respuesta preview: {Preview}", preview);
            _log.LogWarning("══════ SOAP CANCELAR FIN OK ══════");

            return raw;
        }
        catch (TaskCanceledException ex)
        {
            _log.LogError(ex, "SOAP → TIMEOUT al conectar con {Endpoint}", Endpoint);
            throw;
        }
        catch (HttpRequestException ex)
        {
            _log.LogError(ex, "SOAP → HTTP ERROR: {Message}", ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "SOAP → EXCEPCION INESPERADA: {Type} - {Message}", ex.GetType().Name, ex.Message);
            throw;
        }
    }

    private static string BuildSoapEnvelope(CancelacionSoapDatos d)
    {
        // Escapar valores para XML
        static string Esc(string v) => System.Security.SecurityElement.Escape(v ?? "") ?? "";

        return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:ws=""{WsNamespace}"">
  <soapenv:Header/>
  <soapenv:Body>
    <ws:cancelarCfdi>
      <datos>
        <accion>{Esc(d.Accion)}</accion>
        <b64Cer>{Esc(d.B64Cer)}</b64Cer>
        <b64Key>{Esc(d.B64Key)}</b64Key>
        <motivo>{Esc(d.Motivo)}</motivo>
        <pass>{Esc(d.Pass)}</pass>
        <password>{Esc(d.Password)}</password>
        <produccion>{Esc(d.Produccion)}</produccion>
        <usuario>{Esc(d.Usuario)}</usuario>
        <uuid>{Esc(d.Uuid)}</uuid>
        <folioSustitucion>{Esc(d.FolioSustitucion)}</folioSustitucion>
        <rfc>{Esc(d.Rfc)}</rfc>
      </datos>
    </ws:cancelarCfdi>
  </soapenv:Body>
</soapenv:Envelope>";
    }
}
