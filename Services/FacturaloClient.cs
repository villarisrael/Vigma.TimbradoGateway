using System.Net.Http;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using Vigma.TimbradoGateway.Services.Facturalo;

namespace Vigma.TimbradoGateway.Services;

// ── Resultado genérico de cualquier llamada a FacturaLO ──────────────────────
// Status: "success" | "error" — presente en respuestas de cancelación (RespuestaCancelar)
// UrlUsada: la URL REAL que se envió la petición (para debugging)
public sealed record FacturaloRespuesta(string Code, string Message, string Data, string Status = "", string UrlUsada = "");

// ── Resultado específico de consultarEstadoSAT ───────────────────────────────
public sealed record FacturaloEstadoSat(
    string CodigoEstatus,
    string EsCancelable,
    string Estado,
    string EstatusCancelacion);

// ── Interfaz ─────────────────────────────────────────────────────────────────
public interface IFacturaloClient
{
    /// <summary>
    /// Timbra un CFDI XML <b>sin</b> el atributo Sello.
    /// FacturaLO recibe el keyPEM y realiza el sellado + timbrado.
    /// Endpoint SOAP: timbrarConSello
    /// </summary>
    Task<FacturaloRespuesta> TimbrarConSelloAsync(
        string apikey, string xmlSinSello, string keyPem,
        bool produccion = false, CancellationToken ct = default);

    /// <summary>
    /// Cancelación simplificada (sin certificados).
    /// Endpoint SOAP: cancelarSP
    /// </summary>
    Task<FacturaloRespuesta> CancelarAsync(
        string apikey, string rfcEmisor, string uuid,
        bool produccion = false, CancellationToken ct = default);

    /// <summary>
    /// Cancelación completa con CSD en formato PEM y motivo de cancelación.
    /// Soporta facturas emitidas por cualquier sistema (no requiere haber timbrado aquí).
    /// Endpoint SOAP: cancelarPEM
    /// </summary>
    Task<FacturaloRespuesta> CancelarConPemAsync(
        string apikey, string keyPem, string cerPem,
        string uuid, string rfcEmisor, string rfcReceptor, string total,
        string motivo, string folioSustitucion = "",
        bool produccion = false, CancellationToken ct = default);

    /// <summary>
    /// Consulta el estado del comprobante ante el SAT.
    /// Endpoint SOAP: consultarEstadoSAT
    /// </summary>
    Task<FacturaloEstadoSat> ConsultarEstadoSatAsync(
        string apikey, string uuid, string rfcEmisor, string rfcReceptor, string total,
        bool produccion = false, CancellationToken ct = default);
}

// ── Implementación ────────────────────────────────────────────────────────────
public sealed class FacturaloClient : IFacturaloClient
{
    private readonly HttpClient _http;
    private readonly FacturaloOptions _opts;

    // Namespaces SOAP fijos que usa FacturaLO PLUS
    private const string NsSoapEnv = "http://schemas.xmlsoap.org/soap/envelope/";
    private const string NsXsi     = "http://www.w3.org/2001/XMLSchema-instance";
    private const string NsXsd     = "http://www.w3.org/2001/XMLSchema";
    private const string NsUrn     = "urn:ws_api";
    private const string NsEnc     = "http://schemas.xmlsoap.org/soap/encoding/";

    public FacturaloClient(HttpClient http, IOptions<FacturaloOptions> opts)
    {
        _http = http;
        _opts = opts?.Value ?? new FacturaloOptions();
    }

    // ── Timbrar con sello ─────────────────────────────────────────────────────
    public async Task<FacturaloRespuesta> TimbrarConSelloAsync(
        string apikey, string xmlSinSello, string keyPem,
        bool produccion = false, CancellationToken ct = default)
    {
        var body = $"""
            <urn:timbrarConSello soapenv:encodingStyle="{NsEnc}">
                <apikey xsi:type="xsd:string">{SecurityEscape(apikey)}</apikey>
                <xmlCFDI xsi:type="xsd:string"><![CDATA[{xmlSinSello}]]></xmlCFDI>
                <keyPEM xsi:type="xsd:string"><![CDATA[{keyPem}]]></keyPEM>
            </urn:timbrarConSello>
            """;

        var (raw, urlReal) = await EnviarSoapAsync(body, produccion, ct);
        var respuesta = ParseRespuestaTimbrado(raw);
        // Retorna con la URL real incluida
        return respuesta with { UrlUsada = urlReal };
    }

    // ── Cancelar SP ───────────────────────────────────────────────────────────
    public async Task<FacturaloRespuesta> CancelarAsync(
        string apikey, string rfcEmisor, string uuid,
        bool produccion = false, CancellationToken ct = default)
    {
        var body = $"""
            <urn:cancelarSP soapenv:encodingStyle="{NsEnc}">
                <apikey xsi:type="xsd:string">{SecurityEscape(apikey)}</apikey>
                <rfcEmisor xsi:type="xsd:string">{SecurityEscape(rfcEmisor)}</rfcEmisor>
                <uuid xsi:type="xsd:string">{SecurityEscape(uuid)}</uuid>
            </urn:cancelarSP>
            """;

        var (raw, urlReal) = await EnviarSoapAsync(body, produccion, ct);
        var respuesta = ParseRespuestaCancelar(raw);
        return respuesta with { UrlUsada = urlReal };
    }

    // ── Cancelar con PEM ─────────────────────────────────────────────────────
    public async Task<FacturaloRespuesta> CancelarConPemAsync(
        string apikey, string keyPem, string cerPem,
        string uuid, string rfcEmisor, string rfcReceptor, string total,
        string motivo, string folioSustitucion = "",
        bool produccion = false, CancellationToken ct = default)
    {
        var body = $"""
            <urn:cancelarPEM soapenv:encodingStyle="{NsEnc}">
                <apikey xsi:type="xsd:string">{SecurityEscape(apikey)}</apikey>
                <keyPEM xsi:type="xsd:string"><![CDATA[{keyPem}]]></keyPEM>
                <cerPEM xsi:type="xsd:string"><![CDATA[{cerPem}]]></cerPEM>
                <uuid xsi:type="xsd:string">{SecurityEscape(uuid)}</uuid>
                <rfcEmisor xsi:type="xsd:string">{SecurityEscape(rfcEmisor)}</rfcEmisor>
                <rfcReceptor xsi:type="xsd:string">{SecurityEscape(rfcReceptor)}</rfcReceptor>
                <total xsi:type="xsd:double">{SecurityEscape(total)}</total>
                <motivo xsi:type="xsd:string">{SecurityEscape(motivo)}</motivo>
                <folioSustitucion xsi:type="xsd:string">{SecurityEscape(folioSustitucion)}</folioSustitucion>
            </urn:cancelarPEM>
            """;

        var (raw, urlReal) = await EnviarSoapAsync(body, produccion, ct);
        var respuesta = ParseRespuestaCancelar(raw);
        return respuesta with { UrlUsada = urlReal };
    }

    // ── Consultar estado SAT ──────────────────────────────────────────────────
    public async Task<FacturaloEstadoSat> ConsultarEstadoSatAsync(
        string apikey, string uuid, string rfcEmisor, string rfcReceptor, string total,
        bool produccion = false, CancellationToken ct = default)
    {
        var body = $"""
            <urn:consultarEstadoSAT soapenv:encodingStyle="{NsEnc}">
                <apikey xsi:type="xsd:string">{SecurityEscape(apikey)}</apikey>
                <uuid xsi:type="xsd:string">{SecurityEscape(uuid)}</uuid>
                <rfcEmisor xsi:type="xsd:string">{SecurityEscape(rfcEmisor)}</rfcEmisor>
                <rfcReceptor xsi:type="xsd:string">{SecurityEscape(rfcReceptor)}</rfcReceptor>
                <total xsi:type="xsd:string">{SecurityEscape(total)}</total>
            </urn:consultarEstadoSAT>
            """;

        var (raw, urlReal) = await EnviarSoapAsync(body, produccion, ct);
        return ParseEstadoSat(raw);
    }

    // ── HTTP + SOAP ───────────────────────────────────────────────────────────
    // Retorna tupla: (respuestaXml, urlReal)
    private async Task<(string ResponseXml, string UrlReal)> EnviarSoapAsync(string bodyInner, bool produccion, CancellationToken ct)
    {
        var url = produccion ? _opts.UrlProd : _opts.UrlDev;

        var envelope = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <soapenv:Envelope
                xmlns:xsi="{NsXsi}"
                xmlns:xsd="{NsXsd}"
                xmlns:soapenv="{NsSoapEnv}"
                xmlns:urn="{NsUrn}">
                <soapenv:Header/>
                <soapenv:Body>
                    {bodyInner}
                </soapenv:Body>
            </soapenv:Envelope>
            """;

        using var content = new StringContent(envelope, Encoding.UTF8, "text/xml");
        content.Headers.Add("SOAPAction", "\"\"");

        using var resp = await _http.PostAsync(url, content, ct);
        resp.EnsureSuccessStatusCode();
        var responseXml = await resp.Content.ReadAsStringAsync(ct);

        // Retorna AMBAS: el XML de respuesta y la URL real que se usó
        return (responseXml, url);
    }

    // ── Parsers XML ───────────────────────────────────────────────────────────
    private static FacturaloRespuesta ParseRespuestaTimbrado(string soapXml)
    {
        try
        {
            var doc  = XDocument.Parse(soapXml);
            var ret  = BuscarElemento(doc, "return");
            var code = ret?.Element("code")?.Value ?? "";
            var msg  = ret?.Element("message")?.Value ?? "";
            var data = ret?.Element("data")?.Value ?? "";
            return new FacturaloRespuesta(code, msg, data);
        }
        catch (Exception ex)
        {
            return new FacturaloRespuesta("999", $"Error parseando respuesta SOAP: {ex.Message}", "");
        }
    }

    private static FacturaloRespuesta ParseRespuestaCancelar(string soapXml)
    {
        try
        {
            var doc    = XDocument.Parse(soapXml);
            var ret    = BuscarElemento(doc, "return");
            var code   = ret?.Elements().FirstOrDefault(e => e.Name.LocalName == "code")?.Value    ?? "";
            var msg    = ret?.Elements().FirstOrDefault(e => e.Name.LocalName == "message")?.Value ?? "";
            var data   = ret?.Elements().FirstOrDefault(e => e.Name.LocalName == "data")?.Value    ?? "";
            var status = ret?.Elements().FirstOrDefault(e => e.Name.LocalName == "status")?.Value  ?? "";
            return new FacturaloRespuesta(code, msg, data, status);
        }
        catch (Exception ex)
        {
            return new FacturaloRespuesta("999", $"Error parseando respuesta SOAP: {ex.Message}", "", "error");
        }
    }

    private static FacturaloEstadoSat ParseEstadoSat(string soapXml)
    {
        try
        {
            var doc = XDocument.Parse(soapXml);
            var ret = BuscarElemento(doc, "return");
            return new FacturaloEstadoSat(
                CodigoEstatus:       ret?.Element("CodigoEstatus")?.Value ?? "",
                EsCancelable:        ret?.Element("EsCancelable")?.Value ?? "",
                Estado:              ret?.Element("Estado")?.Value ?? "",
                EstatusCancelacion:  ret?.Element("EstatusCancelacion")?.Value ?? "");
        }
        catch
        {
            return new FacturaloEstadoSat("", "", "", "");
        }
    }

    /// <summary>
    /// Busca un elemento por nombre local ignorando namespace,
    /// recorriendo el documento completo.
    /// </summary>
    private static XElement? BuscarElemento(XDocument doc, string localName) =>
        doc.Descendants()
           .FirstOrDefault(e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Escapa caracteres XML peligrosos en valores que van como texto (no CDATA).
    /// </summary>
    private static string SecurityEscape(string s) =>
        s.Replace("&", "&amp;")
         .Replace("<", "&lt;")
         .Replace(">", "&gt;")
         .Replace("\"", "&quot;");
}
