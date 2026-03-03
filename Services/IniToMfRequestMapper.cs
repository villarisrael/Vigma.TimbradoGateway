using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using TimbradoGateway.Contracts.Mf;
using TimbradoGateway.Infrastructure.Ini;
using Vigma.TimbradoGateway.Models;
using Vigma.TimbradoGateway.Services;

namespace TimbradoGateway.Services;

public sealed class IniToMfRequestMapper
{

    private readonly CryptoService _crypto;

    public IniToMfRequestMapper(CryptoService crypto)
    {
        _crypto = crypto;
    }

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = null, // respetar nombres exactos que MF espera (camel/snake mix)
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    /// <summary>
    /// Convierte IniCfdiDocument (tipado) al JSON que consume https://ws.multifacturas.com/api/
    /// REGLA: conf SIEMPRE va como tenant_id (no cer/key/pass).
    /// </summary>
    public async Task<string> MapToJsonAsync(IniCfdiDocument doc, Tenant tenant, Certificado cert)
    {




        if (doc == null) throw new ArgumentNullException(nameof(doc));

        var isPago = string.Equals(doc.Factura?.TipoComprobante, "P", StringComparison.OrdinalIgnoreCase)
                     || doc.Complementos?.Pagos20 != null;

        
        
        
        
        
        var root = new Dictionary<string, object?>();

        // Root
        root["version_cfdi"] = doc.VersionCfdi ?? "4.0";
        root["validacion_local"] = doc.ValidacionLocal ?? "NO";

        // Si es pago, MF suele requerir "complemento":"pagos20"
        if (isPago) root["complemento"] = "pagos20";


        var pacPass = string.IsNullOrWhiteSpace(tenant.PacPasswordEnc)
           ? ""
           : _crypto.DecryptFromBase64(tenant.PacPasswordEnc);

        var prod = tenant.PacProduccion ? "SI" : "NO";


        // PAC
        root["PAC"] = new Dictionary<string, object?>
        {
            ["usuario"] = tenant.PacUsuario,
            ["pass"] = pacPass,
            ["produccion"] = prod
        };
        CancellationToken ct = default;

        var cerBytes = await File.ReadAllBytesAsync(cert.CerPath, ct);
        var keyBytes = await File.ReadAllBytesAsync(cert.KeyPath, ct);

        var cerB64 =  Convert.ToBase64String(cerBytes);
        var keyB64 = Convert.ToBase64String(keyBytes);
        string keyPass = "";

        if (!string.IsNullOrWhiteSpace(cert.KeyPasswordEnc))
        {
            var s = cert.KeyPasswordEnc.Trim();

            try
            {
                // Si estaba cifrado en base64 por tu CryptoService
                keyPass = _crypto.DecryptFromBase64(s);
            }
            catch
            {
                // Si estaba en texto plano (como ZH20051998), úsalo tal cual
                keyPass = s;
            }
        }



        // conf => tenant_id (SIEMPRE)
        root["conf"] = new Dictionary<string, object?>
        {
            ["cer"] = cerB64,
            ["key"] = keyB64,
            ["pass"] = keyPass
        };

        // factura
        root["factura"] = MapFactura(doc);

        // emisor / receptor
        root["emisor"] = MapEmisor(doc);
        root["receptor"] = MapReceptor(doc);

        // InformacionGlobal (si viene en Raw como InformacionGlobal.Periodicidad etc.)
        var infoGlobal = ExtractObjectFromRaw(doc.Raw, "InformacionGlobal.");
        if (infoGlobal.Count > 0)
            root["InformacionGlobal"] = infoGlobal;

        // CfdisRelacionados:
        // Tu modelo trae uno (IniCfdiRelacionados) pero MF puede recibir arreglo.
        var cfdisRel = MapCfdisRelacionados(doc);
        if (cfdisRel != null)
            root["CfdisRelacionados"] = cfdisRel;

        // conceptos
        root["conceptos"] = MapConceptos(doc);

        // impuestos globales
        var impuestos = MapImpuestos(doc);
        if (impuestos != null)
            root["impuestos"] = impuestos;

        // pagos20 (si aplica)
        if (doc.Complementos?.Pagos20 != null)
            root["pagos20"] = MapPagos20(doc.Complementos.Pagos20);

        // (Opcional) Cualquier otra “bolsa” extra del INI podría salir de doc.Raw si quieres.
        // Por ahora lo dejamos tipado a lo que MF espera.

        return JsonSerializer.Serialize(root, _jsonOpts);
    }

    // -----------------------
    // Mapeos
    // -----------------------
    private static Dictionary<string, object?> MapFactura(IniCfdiDocument doc)
    {
        var f = doc.Factura ?? new IniFactura();
        var factura = new Dictionary<string, object?>();
        if (f.TipoComprobante == "I" || f.TipoComprobante == "E")
        {
             factura = new Dictionary<string, object?>
            {
                ["condicionesDePago"] = f.CondicionesDePago,
                ["fecha_expedicion"] = f.FechaExpedicion ?? "AUTO",
                ["folio"] = f.Folio,
                ["forma_pago"] = f.FormaPago,
                ["LugarExpedicion"] = f.LugarExpedicion,
                ["metodo_pago"] = f.MetodoPago,
                ["moneda"] = f.Moneda ?? "MXN",
                ["serie"] = f.Serie,
                ["subtotal"] = f.SubTotal,
                ["descuento"] = RedondearDosDecimales(f.Descuento),
                ["tipocambio"] = f.TipoCambio,
                ["tipocomprobante"] = f.TipoComprobante,
                ["total"] = f.Total,
                ["Exportacion"] = f.Exportacion ?? "01"
            }
              .Where(kv => kv.Value != null)
              .ToDictionary(kv => kv.Key, kv => kv.Value);
        }
        if (f.TipoComprobante == "P")
        {
            factura = new Dictionary<string, object?>
            {
     
                ["fecha_expedicion"] = f.FechaExpedicion ?? "AUTO",
                ["folio"] = f.Folio,
                ["forma_pago"] = f.FormaPago,
                ["LugarExpedicion"] = f.LugarExpedicion,
                ["metodo_pago"] = f.MetodoPago,
                ["moneda"] = f.Moneda ?? "MXN",
                ["serie"] = f.Serie,
                ["subtotal"] = f.SubTotal,
               
                ["tipocomprobante"] = f.TipoComprobante,
                ["total"] = f.Total,
                ["Exportacion"] = f.Exportacion ?? "01"
            }
             .Where(kv => kv.Value != null)
             .ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        // Permite “extras” de [factura] si vienen (por ejemplo Confirmacion, Observaciones, etc.)
        if (!string.IsNullOrWhiteSpace(f.Observaciones))
            factura["observaciones"] = f.Observaciones;

        if (!string.IsNullOrWhiteSpace(f.Confirmacion))
            factura["Confirmacion"] = f.Confirmacion;

        // Por si en Extra venían claves con exactitud MF:
        foreach (var kv in f.Extra)
        {
            if (!factura.ContainsKey(kv.Key))
                factura[kv.Key] = kv.Value;
        }

        return factura;
    }

    private static Dictionary<string, object?> MapEmisor(IniCfdiDocument doc)
    {
        var e = doc.Emisor ?? new IniEmisor();
        var emisor = new Dictionary<string, object?>
        {
            ["rfc"] = e.Rfc,
            ["nombre"] = e.Nombre,
            ["RegimenFiscal"] = e.RegimenFiscal
        };

        foreach (var kv in e.Extra)
        {
            if (!emisor.ContainsKey(kv.Key))
                emisor[kv.Key] = kv.Value;
        }

        return emisor;
    }

    private static Dictionary<string, object?> MapReceptor(IniCfdiDocument doc)
    {
        var r = doc.Receptor ?? new IniReceptor();
        var receptor = new Dictionary<string, object?>
        {
            ["rfc"] = r.Rfc,
            ["nombre"] = r.Nombre,
            ["UsoCFDI"] = r.UsoCfdi,
            ["DomicilioFiscalReceptor"] = r.DomicilioFiscalReceptor,
            ["RegimenFiscalReceptor"] = r.RegimenFiscalReceptor,

            // extranjeros (opcionales)
            //["ResidenciaFiscal"] = r.ResidenciaFiscal,
            //["NumRegIdTrib"] = r.NumRegIdTrib
        };

        foreach (var kv in r.Extra)
        {
            if (!receptor.ContainsKey(kv.Key))
                receptor[kv.Key] = kv.Value;
        }

        return receptor;
    }

    /// <summary>
    /// MF espera: CfdisRelacionados: [ { TipoRelacion:"01", UUID:[...]} ]
    /// Tu modelo trae uno, entonces lo envolvemos en lista.
    /// </summary>
    private static List<Dictionary<string, object?>>? MapCfdisRelacionados(IniCfdiDocument doc)
    {
        if (doc.CfdiRelacionados == null) return null;

        var tr = doc.CfdiRelacionados.TipoRelacion;
        var uuids = doc.CfdiRelacionados.Uuids?.Where(u => !string.IsNullOrWhiteSpace(u)).ToList() ?? new();

        if (string.IsNullOrWhiteSpace(tr) || uuids.Count == 0) return null;

        return new List<Dictionary<string, object?>>
        {
            new()
            {
                ["TipoRelacion"] = tr,
                ["UUID"] = uuids
            }
        };
    }


    private static string RedondearDosDecimales(decimal? valor)
    {
        return Math.Round(valor ?? 0m, 2, MidpointRounding.AwayFromZero).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string RedondearSeisDecimales(decimal? valor)
    {
        return Math.Round(valor ?? 0m, 6, MidpointRounding.AwayFromZero).ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
    }





    private static List<Dictionary<string, object?>> MapConceptos(IniCfdiDocument doc)
    {

        var f = doc.Factura;
        var list = new List<Dictionary<string, object?>>();
        if (doc.Conceptos == null || doc.Conceptos.Count == 0) return list;

        int ixrubi = 1;
        var item = new Dictionary<string, object?>();
        foreach (var c in doc.Conceptos)
        {
            // Normaliza
            var descuento = c.Descuento ?? 0m;

            // Arma el item una sola vez

            var valorus = RedondearDosDecimales(c.ValorUnitario);
            var impo = RedondearDosDecimales(c.Importe);
            if (f.TipoComprobante == "I" || f.TipoComprobante == "E")
            {
                 item = new Dictionary<string, object?>
                {
                    ["Cantidad"] = c.Cantidad,
                    ["ClaveUnidad"] = c.ClaveUnidad,
                    ["ID"] = ixrubi,
                    ["Descripcion"] = c.Descripcion,
                    ["ValorUnitario"] = valorus,
                    ["Importe"] = impo,
                    ["ClaveProdServ"] = c.ClaveProdServ,
                    ["ObjetoImp"] = c.ObjetoImp
                };
            }
            if (f.TipoComprobante == "P" )
            {
                 item = new Dictionary<string, object?>
                {
                    ["Cantidad"] = c.Cantidad,
                    ["ClaveUnidad"] = c.ClaveUnidad,
                 
                    ["Descripcion"] = c.Descripcion,
                    ["ValorUnitario"] = "0",
                    ["Importe"] = "0",
                    ["ClaveProdServ"] = c.ClaveProdServ,
                    ["ObjetoImp"] = c.ObjetoImp
                };
            }

            // Solo si hay descuento > 0, se agrega
            if (descuento > 0m)
            {
                var dec = RedondearDosDecimales(descuento);
                item["Descuento"] = dec;
            }





            // Impuestos de concepto (MF: Impuestos:{Traslados:[...], Retenciones:[...]})
            var impConcepto = new Dictionary<string, object?>();

            if (c.Impuestos?.Traslados?.Count > 0)
            {
                impConcepto["Traslados"] = c.Impuestos.Traslados.Select(t => new Dictionary<string, object?>
                {
                    ["Base"] = t.Base,
                    ["Impuesto"] = t.Impuesto,
                    ["TipoFactor"] = t.TipoFactor,
                    ["TasaOCuota"] = t.TasaOCuota,
                    ["Importe"] = t.Importe
                }).ToList();
            }

            if (c.Impuestos?.Retenciones?.Count > 0)
            {
                impConcepto["Retenciones"] = c.Impuestos.Retenciones.Select(r => new Dictionary<string, object?>
                {
                    ["Base"] = r.Base,
                    ["Impuesto"] = r.Impuesto,
                    ["TipoFactor"] = r.TipoFactor,
                    ["TasaOCuota"] = r.TasaOCuota,
                    ["Importe"] = r.Importe
                }).ToList();
            }

            if (impConcepto.Count > 0)
                item["Impuestos"] = impConcepto;

            // Extras del concepto
            if (c.Extra != null)
            {
                foreach (var kv in c.Extra)
                {
                    if (!item.ContainsKey(kv.Key))
                        item[kv.Key] = kv.Value;
                }
            }

            if (c.Cantidad != null && c.Descripcion != null && c.Importe != null)
            {
                list.Add(item);
                ixrubi++;
            }
        }

        return list;
    }

    private static Dictionary<string, object?>? MapImpuestos(IniCfdiDocument doc)
    {
        var i = doc.Impuestos;
        if (i == null) return null;

        var impuestos = new Dictionary<string, object?>
        {
            ["TotalImpuestosTrasladados"] = i.TotalImpuestosTrasladados,
         //   ["TotalImpuestosRetenidos"] = i.TotalImpuestosRetenidos
        };

        // MF en tus ejemplos usa "translados" (minúsculas)
        if (i.Translados?.Count > 0)
        {
            impuestos["translados"] = i.Translados.Select(t => new Dictionary<string, object?>
            {
                ["Base"] = t.Base,
                ["impuesto"] = t.impuesto,
                ["tasa"] = t.tasa,
                ["importe"] = t.importe,
                ["TipoFactor"] = t.TipoFactor
            }).ToList();
        }

        if (i.Retenciones?.Count > 0)
        {
            impuestos["retenciones"] = i.Retenciones.Select(r => new Dictionary<string, object?>
            {
                ["impuesto"] = r.Impuesto,
                ["importe"] = r.Importe
            }).ToList();
        }

        // Extra
        foreach (var kv in i.Extra)
        {
            if (!impuestos.ContainsKey(kv.Key))
                impuestos[kv.Key] = kv.Value;
        }

        // Si no hay nada útil, regresamos null
        var hasAny =
            i.TotalImpuestosTrasladados.HasValue ||
          
            i.Translados?.Count > 0;

        return hasAny ? impuestos : null;
    }

    private static Dictionary<string, object?> MapPagos20(IniPagos20 p20)
    {
        var root = new Dictionary<string, object?>();

        // Totales
        root["Totales"] = new Dictionary<string, object?>
        {
            ["MontoTotalPagos"] = p20.Totales?.MontoTotalPagos,
            ["TotalTrasladosBaseIVA16"] = p20.Totales?.TotalTrasladosBaseIVA16,
            ["TotalTrasladosImpuestoIVA16"] = p20.Totales?.TotalTrasladosImpuestoIVA16,
            ["TotalTrasladosBaseIVA08"] = p20.Totales?.TotalTrasladosBaseIVA08,
            ["TotalTrasladosImpuestoIVA08"] = p20.Totales?.TotalTrasladosImpuestoIVA08,
            ["TotalTrasladosBaseIVA00"] = p20.Totales?.TotalTrasladosBaseIVA00,
            ["TotalTrasladosImpuestoIVA00"] = p20.Totales?.TotalTrasladosImpuestoIVA00,
            ["TotalRetencionesISR"] = p20.Totales?.TotalRetencionesISR,
            ["TotalRetencionesIVA"] = p20.Totales?.TotalRetencionesIVA,
            ["TotalRetencionesIEPS"] = p20.Totales?.TotalRetencionesIEPS
        }
        .Where(kv => kv.Value != null)
        .ToDictionary(kv => kv.Key, kv => kv.Value);

      
        // Pagos
        root["Pagos"] = (p20.Pagos ?? new()).Select(p =>
            new Dictionary<string, object?>
            {
                ["FechaPago"] = p.FechaPago,
                ["FormaDePagoP"] = p.FormaDePagoP,
                ["MonedaP"] = p.MonedaP,
                ["TipoCambioP"] = p.TipoCambioP,
                ["Monto"] = p.Monto,
                ["NumOperacion"] = p.NumOperacion,
                ["RfcEmisorCtaOrd"] = p.RfcEmisorCtaOrd,
                ["NomBancoOrdExt"] = p.NomBancoOrdExt,
                ["CtaOrdenante"] = p.CtaOrdenante,
                ["RfcEmisorCtaBen"] = p.RfcEmisorCtaBen,
                ["CtaBeneficiario"] = p.CtaBeneficiario,
                ["TipoCadPago"] = p.TipoCadPago,
                ["CertPago"] = p.CertPago,
                ["CadPago"] = p.CadPago,
                ["SelloPago"] = p.SelloPago,
                ["DoctoRelacionado"] = (p.DoctosRelacionados ?? new()).Select(d =>
                    new Dictionary<string, object?>
                    {
                        ["IdDocumento"] = d.IdDocumento,
                        ["Serie"] = d.Serie,
                        ["Folio"] = d.Folio,
                        ["MonedaDR"] = d.MonedaDR,
                        ["EquivalenciaDR"] = d.EquivalenciaDR,
                        ["NumParcialidad"] = d.NumParcialidad,
                        ["ImpSaldoAnt"] = d.ImpSaldoAnt,
                        ["ImpPagado"] = d.ImpPagado,
                        ["ImpSaldoInsoluto"] = d.ImpSaldoInsoluto,
                        ["ObjetoImpDR"] = d.ObjetoImpDR,
                        ["ImpuestosDR"] = MapImpuestosDR(d)
                    }
                    .Where(kv => kv.Value != null)
                    .ToDictionary(kv => kv.Key, kv => kv.Value)
                ).ToList(),
                ["ImpuestosP"] = MapImpuestosP(p)
            }
            .Where(kv => kv.Value != null)
            .ToDictionary(kv => kv.Key, kv => kv.Value)
        ).ToList();

        return root;
    }

    private static object? MapImpuestosP(IniPago20Pago p)
    {
        var hasT = p.ImpuestosP?.TrasladosP?.Count > 0;
        var hasR = p.ImpuestosP?.RetencionesP?.Count > 0;
        if (!hasT && !hasR) return null;

        var obj = new Dictionary<string, object?>();

        if (hasT)
        {
            obj["TrasladosP"] = p.ImpuestosP!.TrasladosP.Select(t => new Dictionary<string, object?>
            {
                ["BaseP"] = RedondearDosDecimales( t.BaseP),
                ["ImpuestoP"] = t.ImpuestoP,
                ["TipoFactorP"] = t.TipoFactorP,
                ["TasaOCuotaP"] = RedondearSeisDecimales( t.TasaOCuotaP),
                ["ImporteP"] =RedondearDosDecimales( t.ImporteP)
            }).ToList();
        }

        if (hasR)
        {
            obj["RetencionesP"] = p.ImpuestosP!.RetencionesP.Select(r => new Dictionary<string, object?>
            {
                ["ImpuestoP"] = r.ImpuestoP,
                ["ImporteP"] = r.ImporteP
            }).ToList();
        }

        return obj;
    }

    private static object? MapImpuestosDR(IniPago20DoctoRelacionado d)
    {
        var hasT = d.ImpuestosDR?.TrasladosDR?.Count > 0;
        var hasR = d.ImpuestosDR?.RetencionesDR?.Count > 0;
        if (!hasT && !hasR) return null;

        var obj = new Dictionary<string, object?>();

        if (hasT)
        {
            obj["TrasladoDR"] = d.ImpuestosDR!.TrasladosDR.Select(t => new Dictionary<string, object?>
            {
                ["BaseDR"] = RedondearDosDecimales( t.BaseDR),
                ["ImpuestoDR"] = t.ImpuestoDR,
                ["TipoFactorDR"] = t.TipoFactorDR,
                ["TasaOCuotaDR"] = RedondearSeisDecimales( t.TasaOCuotaDR),
                ["ImporteDR"] = RedondearDosDecimales(t.ImporteDR)
            }).ToList();
        }

        if (hasR)
        {
            obj["RetencionesDR"] = d.ImpuestosDR!.RetencionesDR.Select(r => new Dictionary<string, object?>
            {
                ["ImpuestoDR"] = r.ImpuestoDR,
                ["ImporteDR"] = r.ImporteDR
            }).ToList();
        }

        return obj;
    }

    // -----------------------
    // Utilidad: Raw -> objeto
    // -----------------------
    private static Dictionary<string, object?> ExtractObjectFromRaw(
        Dictionary<string, string> raw,
        string prefix)
    {
        var obj = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (raw == null || raw.Count == 0) return obj;

        foreach (var kv in raw)
        {
            if (!kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var key = kv.Key.Substring(prefix.Length); // ej: "Periodicidad"
            if (string.IsNullOrWhiteSpace(key)) continue;

            obj[key] = kv.Value;
        }

        return obj;
    }


}
