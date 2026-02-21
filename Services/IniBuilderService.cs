using System.Text;
using System.Text.RegularExpressions;
using Vigma.TimbradoGateway.DTOs;

namespace Vigma.TimbradoGateway.Services;

public interface IIniBuilderService
{
    string BuildFromJson(TimbradoJsonRequest req);

    // OJO: aquí ya es BASE64, no rutas.
    string UpsertConfBase64(string ini, string cerB64, string keyB64, string pass);

    string ExtractEmisorRfc(string ini);
}

public class IniBuilderService : IIniBuilderService
{
    public string BuildFromJson(TimbradoJsonRequest req)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"version_cfdi={req.version_cfdi}");
        sb.AppendLine($"validacion_local={req.validacion_local}");

        // PAC (aunque luego lo sobreescribas por tenant)
        sb.AppendLine("[PAC]");
        sb.AppendLine($"usuario={req.PAC.usuario}");
        sb.AppendLine($"pass={req.PAC.pass}");
        sb.AppendLine($"produccion={req.PAC.produccion}");

        sb.AppendLine("[factura]");
        if (!string.IsNullOrWhiteSpace(req.factura.condicionesDePago))
        { sb.AppendLine($"condicionesDePago={req.factura.condicionesDePago}"); }


        sb.AppendLine($"fecha_expedicion={req.factura.fecha_expedicion}");
        if (!string.IsNullOrWhiteSpace(req.factura.folio)) sb.AppendLine($"folio={req.factura.folio}");
        if (!string.IsNullOrWhiteSpace(req.factura.forma_pago)) sb.AppendLine($"forma_pago={req.factura.forma_pago}");
        if (!string.IsNullOrWhiteSpace(req.factura.LugarExpedicion)) sb.AppendLine($"LugarExpedicion={req.factura.LugarExpedicion}");
        if (!string.IsNullOrWhiteSpace(req.factura.metodo_pago)) sb.AppendLine($"metodo_pago={req.factura.metodo_pago}");
        if (!string.IsNullOrWhiteSpace(req.factura.moneda)) sb.AppendLine($"moneda={req.factura.moneda}");
        if (!string.IsNullOrWhiteSpace(req.factura.serie)) sb.AppendLine($"serie={req.factura.serie}");

        sb.AppendLine($"subtotal={req.factura.subtotal}");
        sb.AppendLine($"tipocambio={req.factura.tipocambio}");
        if (!string.IsNullOrWhiteSpace(req.factura.tipocomprobante)) sb.AppendLine($"tipocomprobante={req.factura.tipocomprobante}");
        sb.AppendLine($"total={req.factura.total}");

        if (!string.IsNullOrWhiteSpace(req.factura.Exportacion)) sb.AppendLine($"Exportacion={req.factura.Exportacion}");

        sb.AppendLine("[emisor]");
        sb.AppendLine($"rfc={req.emisor.rfc}");
        sb.AppendLine($"nombre={req.emisor.nombre}");
        sb.AppendLine($"RegimenFiscal={req.emisor.RegimenFiscal}");

        sb.AppendLine("[receptor]");
        sb.AppendLine($"rfc={req.receptor.rfc}");
        sb.AppendLine($"nombre={req.receptor.nombre}");
        sb.AppendLine($"UsoCFDI={req.receptor.UsoCFDI}");
        sb.AppendLine($"DomicilioFiscalReceptor={req.receptor.DomicilioFiscalReceptor}");
        sb.AppendLine($"RegimenFiscalReceptor={req.receptor.RegimenFiscalReceptor}");

        sb.AppendLine("[conceptos]");
        for (int i = 0; i < req.conceptos.Count; i++)
        {
            var c = req.conceptos[i];
            sb.AppendLine($"[conceptos.{i}]");
            sb.AppendLine($"cantidad={c.cantidad}");
            if (!string.IsNullOrWhiteSpace(c.unidad)) sb.AppendLine($"unidad={c.unidad}");
            if (!string.IsNullOrWhiteSpace(c.ID)) sb.AppendLine($"ID={c.ID}");
            sb.AppendLine($"descripcion={c.descripcion}");
            sb.AppendLine($"valorunitario={c.valorunitario}");
            sb.AppendLine($"importe={c.importe}");
            if (!string.IsNullOrWhiteSpace(c.ClaveProdServ)) sb.AppendLine($"ClaveProdServ={c.ClaveProdServ}");
            if (!string.IsNullOrWhiteSpace(c.ClaveUnidad)) sb.AppendLine($"ClaveUnidad={c.ClaveUnidad}");
            if (!string.IsNullOrWhiteSpace(c.ObjetoImp)) sb.AppendLine($"ObjetoImp={c.ObjetoImp}");

            if (c.Impuestos?.Traslados?.Count > 0)
            {
                sb.AppendLine($"[conceptos.{i}.Impuestos]");
                sb.AppendLine($"[conceptos.{i}.Impuestos.Traslados]");
                for (int t = 0; t < c.Impuestos.Traslados.Count; t++)
                {
                    var tr = c.Impuestos.Traslados[t];
                    sb.AppendLine($"[conceptos.{i}.Impuestos.Traslados.{t}]");
                    sb.AppendLine($"Base={tr.Base}");
                    sb.AppendLine($"Impuesto={tr.Impuesto}");
                    sb.AppendLine($"TipoFactor={tr.TipoFactor}");
                    sb.AppendLine($"TasaOCuota={tr.TasaOCuota}");
                    sb.AppendLine($"Importe={tr.Importe}");
                }
            }
        }

        if (req.impuestos != null)
        {
            sb.AppendLine("[impuestos]");
            sb.AppendLine($"TotalImpuestosTrasladados={req.impuestos.TotalImpuestosTrasladados}");

            // ✅ OJO: traslados (con a)
            if (req.impuestos.translados?.Count > 0)
            {
                sb.AppendLine("[impuestos.translados]");
                for (int i = 0; i < req.impuestos.translados.Count; i++)
                {
                    var tr = req.impuestos.translados[i];
                    sb.AppendLine($"[impuestos.traslados.{i}]");
                    sb.AppendLine($"Base={tr.Base}");
                    sb.AppendLine($"impuesto={tr.impuesto}");
                    sb.AppendLine($"tasa={tr.tasa}");
                    sb.AppendLine($"importe={tr.Importe}");
                    sb.AppendLine($"TipoFactor={tr.TipoFactor}");
                }
            }
        }

        return sb.ToString();
    }

    public string UpsertConfBase64(string ini, string cerB64, string keyB64, string pass)
    {
        // elimina bloque [conf] existente
        ini = Regex.Replace(
            ini,
            @"(?im)^\[conf\]\s*[\s\S]*?(?=^\[|\z)",
            "",
            RegexOptions.Multiline);

        var sb = new StringBuilder();
        sb.AppendLine(ini.TrimEnd());
        sb.AppendLine();
        sb.AppendLine("[conf]");
        sb.AppendLine($"cer={cerB64}");
        sb.AppendLine($"key={keyB64}");
        sb.AppendLine($"pass={pass}");
        sb.AppendLine();
        return sb.ToString();
    }

    public string ExtractEmisorRfc(string ini)
    {
        // case-insensitive, permite & y minúsculas, y espacios
        var m = Regex.Match(
            ini,
            @"(?is)\[emisor\][\s\S]*?^\s*rfc\s*=\s*(?<rfc>[A-ZÑ&0-9]{12,13})\s*$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

        if (!m.Success)
            throw new Exception("No se pudo detectar el RFC del emisor en el INI.");

        return m.Groups["rfc"].Value.Trim().ToUpperInvariant();
    }
}
