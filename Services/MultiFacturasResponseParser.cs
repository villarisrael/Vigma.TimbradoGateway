using System.Xml.Linq;

namespace Vigma.TimbradoGateway.Services;

public static class MultiFacturasResponseParser
{
    public static (bool ok, string? codigo, string? mensaje, string? uuid, string? xmlTimbrado) Parse(string rawXml)
    {
        try
        {
            var xdoc = XDocument.Parse(rawXml);

            var codigo = xdoc.Descendants("codigo_mf_numero").FirstOrDefault()?.Value
                      ?? xdoc.Descendants("codigo").FirstOrDefault()?.Value;

            var mensaje = xdoc.Descendants("codigo_mf_texto").FirstOrDefault()?.Value
                       ?? xdoc.Descendants("mensaje").FirstOrDefault()?.Value;

            var uuid = xdoc.Descendants("uuid").FirstOrDefault()?.Value
                    ?? xdoc.Descendants("UUID").FirstOrDefault()?.Value;

            var xmlTimbrado = xdoc.Descendants("xml").FirstOrDefault()?.Value
                           ?? xdoc.Descendants("cfdi").FirstOrDefault()?.Value
                           ?? xdoc.Descendants("xmlTimbrado").FirstOrDefault()?.Value;

            return (codigo == "0", codigo, mensaje, uuid, xmlTimbrado);
        }
        catch
        {
            return (false, null, "Respuesta PAC no es XML válido.", null, null);
        }
    }
}
