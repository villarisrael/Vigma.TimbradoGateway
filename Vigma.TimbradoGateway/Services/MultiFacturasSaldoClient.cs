using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;

using Vigma.TimbradoGateway.DTOs;

namespace Vigma.TimbradoGateway.Services
{



    public interface IMultiFacturasSaldoClient
    {
        Task<SaldoTimbresResponse> ConsultarSaldoAsync(string urlWs, string rfc, string clave, CancellationToken ct = default);
    }

    public sealed class MultiFacturasSaldoClient : IMultiFacturasSaldoClient
    {
        private readonly HttpClient _http;

        public MultiFacturasSaldoClient(HttpClient http)
        {
            _http = http;
        }

        public async Task<SaldoTimbresResponse> ConsultarSaldoAsync(string urlWs, string rfc, string clave, CancellationToken ct = default)
        {
            // SOAP Envelope (igual a tu ejemplo)
            var soapEnvelope = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:ws=""urn:wservicewsdl"">
  <soapenv:Header/>
  <soapenv:Body>
    <ws:saldo>
      <rfc>{System.Security.SecurityElement.Escape(rfc)}</rfc>
      <clave>{System.Security.SecurityElement.Escape(clave)}</clave>
    </ws:saldo>
  </soapenv:Body>
</soapenv:Envelope>";

            using var req = new HttpRequestMessage(HttpMethod.Post, urlWs);
            req.Content = new StringContent(soapEnvelope, Encoding.UTF8, "text/xml");
            req.Headers.TryAddWithoutValidation("SOAPAction", "urn:wservicewsdl#saldo");

            using var resp = await _http.SendAsync(req, ct);
            var xml = await resp.Content.ReadAsStringAsync(ct);

            // Si el server regresa error http, igual te mando xml crudo para depurar
            if (!resp.IsSuccessStatusCode)
            {
                return new SaldoTimbresResponse
                {
                    Ok = false,
                    Codigo = ((int)resp.StatusCode).ToString(),
                    Mensaje = "Error HTTP al consultar saldo en Multifacturas.",
                    Saldo = 0,
                    XmlCrudo = xml
                };
            }

            // Parseo: en tu respuesta vienen tags sin namespace tipo <saldo>...
            var xdoc = XDocument.Parse(xml);

            string? codigo = xdoc.Descendants("codigo_mf_numero").FirstOrDefault()?.Value;
            string? mensaje = xdoc.Descendants("codigo_mf_texto").FirstOrDefault()?.Value;
            string? saldoStr = xdoc.Descendants("saldo").FirstOrDefault()?.Value;

            int saldo = 0;
            _ = int.TryParse(saldoStr, out saldo);

            return new SaldoTimbresResponse
            {
                Ok = codigo == "0",
                Codigo = codigo,
                Mensaje = mensaje,
                Saldo = saldo,
                XmlCrudo = xml
            };
        }
    }
}