using System.Text;
using Microsoft.Extensions.Configuration;

namespace Vigma.TimbradoGateway.Services;

public interface IMultiFacturasClient
{
    Task<string> TimbrarIniAsync(string iniFinal, string rfcEmisor, CancellationToken ct = default);
}

public class MultiFacturasClient : IMultiFacturasClient
{
    private readonly HttpClient _http;
    private readonly IConfiguration _cfg;

    public MultiFacturasClient(HttpClient http, IConfiguration cfg)
    {
        _http = http;
        _cfg = cfg;
    }

    public async Task<string> TimbrarIniAsync(string iniFinal, string rfcEmisor, CancellationToken ct = default)
    {
        // Endpoint (NO WSDL)
        // Ej: https://ini.multifacturas.com/timbrarini.php
        var url = _cfg["MultiFacturas:TimbrarIniUrl"];
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("Falta MultiFacturas:TimbrarIniUrl en configuración.");

        // Convertir INI final a Base64 (UTF8)
        var inib64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(iniFinal));

        // SOAP timbrarini1
        var soap = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
        <soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:ws=""urn:wservicewsdl"">
          <soapenv:Header/>
          <soapenv:Body>
            <ws:timbrarini1>
              <rfc>{System.Security.SecurityElement.Escape(rfcEmisor)}</rfc>
              <inib64>{System.Security.SecurityElement.Escape(inib64)}</inib64>
            </ws:timbrarini1>
          </soapenv:Body>
        </soapenv:Envelope>";

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = new StringContent(soap, Encoding.UTF8, "text/xml");
        req.Headers.TryAddWithoutValidation("SOAPAction", "urn:wservicewsdl#timbrarini1");

        using var resp = await _http.SendAsync(req, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);

        return raw;
    }
}
