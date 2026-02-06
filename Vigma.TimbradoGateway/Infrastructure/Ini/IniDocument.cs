using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TimbradoGateway.Infrastructure.Ini
{

   

    /// <summary>
    /// Parser INI -> IniCfdiDocument (robusto para:
    /// - CFDI I/E/T con conceptos e impuestos
    /// - CFDI P con complemento pagos20
    /// 
    /// Soporta secciones anidadas tipo:
    /// [conceptos.0]
    /// [conceptos.0.Impuestos.Traslados.0]
    /// [impuestos.translados.0]
    /// [pagos20.Pagos.0]
    /// [pagos20.Pagos.0.DoctoRelacionado.0]
    /// </summary>
    public static class IniDocument
    {
        public static IniCfdiDocument Parse(string ini)
        {
            if (string.IsNullOrWhiteSpace(ini))
                throw new ArgumentException("INI vacío.");

            var parsed = ParseToEntries(ini);

            var doc = new IniCfdiDocument();

            // Root keys (no sección)
            doc.VersionCfdi = GetString(parsed, "", "version_cfdi") ?? doc.VersionCfdi;
            doc.ValidacionLocal = GetString(parsed, "", "validacion_local") ?? doc.ValidacionLocal;

            // PAC
            doc.Pac.Usuario = GetString(parsed, "pac", "usuario");
            doc.Pac.Pass = GetString(parsed, "pac", "pass");
            doc.Pac.Produccion = GetString(parsed, "pac", "produccion") ?? doc.Pac.Produccion;

            // CONF
            doc.Conf.Cer = GetString(parsed, "conf", "cer");
            doc.Conf.Key = GetString(parsed, "conf", "key");
            doc.Conf.Pass = GetString(parsed, "conf", "pass");

            // FACTURA
            doc.Factura.CondicionesDePago = GetString(parsed, "factura", "condicionesDePago");
            doc.Factura.FechaExpedicion = GetString(parsed, "factura", "fecha_expedicion") ?? doc.Factura.FechaExpedicion;
            doc.Factura.Folio = GetString(parsed, "factura", "folio");
            doc.Factura.FormaPago = GetString(parsed, "factura", "forma_pago");
            doc.Factura.LugarExpedicion = GetString(parsed, "factura", "LugarExpedicion") ?? GetString(parsed, "factura", "lugarexpedicion");
            doc.Factura.MetodoPago = GetString(parsed, "factura", "metodo_pago");
            doc.Factura.Moneda = GetString(parsed, "factura", "moneda") ?? doc.Factura.Moneda;
            doc.Factura.Serie = GetString(parsed, "factura", "serie");
            doc.Factura.SubTotal = GetDecimal(parsed, "factura", "subtotal");

            doc.Factura.Descuento = GetDecimal(parsed, "factura", "descuento") ?? 0;
            doc.Factura.TipoCambio = GetDecimal(parsed, "factura", "tipocambio") ?? doc.Factura.TipoCambio;
            doc.Factura.TipoComprobante = GetString(parsed, "factura", "tipocomprobante");
            doc.Factura.Total = GetDecimal(parsed, "factura", "total");
            doc.Factura.Exportacion = GetString(parsed, "factura", "Exportacion") ?? GetString(parsed, "factura", "exportacion") ?? doc.Factura.Exportacion;

            // EMISOR
            doc.Emisor.Rfc = GetString(parsed, "emisor", "rfc");
            doc.Emisor.Nombre = GetString(parsed, "emisor", "nombre");
            doc.Emisor.RegimenFiscal = GetString(parsed, "emisor", "RegimenFiscal") ?? GetString(parsed, "emisor", "regimenfiscal");

            // RECEPTOR
            doc.Receptor.Rfc = GetString(parsed, "receptor", "rfc");
            doc.Receptor.Nombre = GetString(parsed, "receptor", "nombre");
            doc.Receptor.UsoCfdi = GetString(parsed, "receptor", "UsoCFDI") ?? GetString(parsed, "receptor", "usocfdi");
            doc.Receptor.DomicilioFiscalReceptor = GetString(parsed, "receptor", "DomicilioFiscalReceptor") ?? GetString(parsed, "receptor", "domiciliofiscalreceptor");
            doc.Receptor.RegimenFiscalReceptor = GetString(parsed, "receptor", "RegimenFiscalReceptor") ?? GetString(parsed, "receptor", "regimenfiscalreceptor");
            //doc.Receptor.ResidenciaFiscal = GetString(parsed, "receptor", "ResidenciaFiscal") ?? GetString(parsed, "receptor", "residenciafiscal");
            //doc.Receptor.NumRegIdTrib = GetString(parsed, "receptor", "NumRegIdTrib") ?? GetString(parsed, "receptor", "numregidtrib");

            // CFDIs relacionados (si llega como:
            // [CfdisRelacionados] TipoRelacion=..  UUID=... (una sola)
            // o como:
            // [CfdisRelacionados.0] TipoRelacion=.. UUID.0=.. UUID.1=..
            ReadCfdisRelacionados(parsed, doc);

            // CONCEPTOS (con impuestos)
            ReadConceptos(parsed, doc);

            // IMPUESTOS globales
            ReadImpuestosGlobal(parsed, doc);

            // PAGOS 2.0
            ReadPagos20(parsed, doc);

            // Guardar todo lo "no tipado" en Raw (incluye extras y cosas raras del ini)
            FillRaw(parsed, doc);

            return doc;
        }

        // ======================================================
        // Parser base: convierte INI en mapa: sectionPath -> (key -> value)
        // ======================================================
        private static Dictionary<string, Dictionary<string, string>> ParseToEntries(string ini)
        {
            var text = NormalizeNewlines(ini);
            var lines = text.Split('\n');

            var map = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            string currentSection = ""; // root
            EnsureSection(map, currentSection);

            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith(";") || line.StartsWith("#")) continue;

                // Section header
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    currentSection = line.Substring(1, line.Length - 2).Trim();
                    currentSection = currentSection ?? "";
                    EnsureSection(map, currentSection);
                    continue;
                }

                // key=value
                var idx = line.IndexOf('=');
                if (idx <= 0) continue;

                var key = line.Substring(0, idx).Trim();
                var val = line.Substring(idx + 1).Trim();

                if (string.IsNullOrWhiteSpace(key)) continue;

                EnsureSection(map, currentSection);

                // si clave repetida, nos quedamos con la última (más útil en pegados)
                map[currentSection][key] = val;
            }

            // normalizar secciones a lower (pero conservando keys tal cual)
            // para facilitar: "PAC" == "pac"
            var normalized = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in map)
            {
                normalized[kv.Key.Trim()] = kv.Value;
            }

            return normalized;
        }

        private static void EnsureSection(Dictionary<string, Dictionary<string, string>> map, string section)
        {
            if (!map.ContainsKey(section))
                map[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private static string NormalizeNewlines(string s) => s.Replace("\r\n", "\n").Replace("\r", "\n");

        // ======================================================
        // Lectores tipados
        // ======================================================
        private static void ReadCfdisRelacionados(Dictionary<string, Dictionary<string, string>> parsed, IniCfdiDocument doc)
        {
            // Caso 1: [CfdisRelacionados]
            if (parsed.TryGetValue("CfdisRelacionados", out var sec))
            {
                var tr = GetStringFromDict(sec, "TipoRelacion");
                if (!string.IsNullOrWhiteSpace(tr))
                {
                    doc.CfdiRelacionados ??= new IniCfdiRelacionados();
                    doc.CfdiRelacionados.TipoRelacion = tr;

                    // UUID puede venir como "UUID=xxx,yyy" o "UUID.0=.."
                    var uuidSingle = GetStringFromDict(sec, "UUID");
                    if (!string.IsNullOrWhiteSpace(uuidSingle))
                    {
                        foreach (var u in SplitUuids(uuidSingle))
                            doc.CfdiRelacionados.Uuids.Add(u);
                    }

                    foreach (var kv in sec)
                    {
                        if (kv.Key.StartsWith("UUID.", StringComparison.OrdinalIgnoreCase))
                        {
                            var v = kv.Value?.Trim();
                            if (!string.IsNullOrWhiteSpace(v))
                                doc.CfdiRelacionados.Uuids.Add(v);
                        }
                    }
                }
            }

            // Caso 2: [CfdisRelacionados.0], [CfdisRelacionados.1] ...
            var relSections = parsed.Keys
                .Where(k => k.StartsWith("CfdisRelacionados.", StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (relSections.Count > 0)
            {
                // Tu modelo soporta 1, tomamos el primero y el resto lo dejamos en Raw.
                var first = relSections[0];
                var s = parsed[first];

                var tr = GetStringFromDict(s, "TipoRelacion");
                if (!string.IsNullOrWhiteSpace(tr))
                {
                    doc.CfdiRelacionados ??= new IniCfdiRelacionados();
                    doc.CfdiRelacionados.TipoRelacion = tr;
                }

                // UUID array
                foreach (var kv in s)
                {
                    if (string.Equals(kv.Key, "UUID", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var u in SplitUuids(kv.Value))
                            doc.CfdiRelacionados?.Uuids.Add(u);
                    }
                    else if (kv.Key.StartsWith("UUID.", StringComparison.OrdinalIgnoreCase))
                    {
                        var v = kv.Value?.Trim();
                        if (!string.IsNullOrWhiteSpace(v))
                            doc.CfdiRelacionados?.Uuids.Add(v);
                    }
                }
            }
        }

        private static void ReadConceptos(Dictionary<string, Dictionary<string, string>> parsed, IniCfdiDocument doc)
        {
            // Secciones: conceptos.0, conceptos.1 ...
            var conceptSections = parsed.Keys
                .Where(k => k.StartsWith("conceptos.", StringComparison.OrdinalIgnoreCase))
                .Where(k => IsDirectIndexSection(k, "conceptos")) // conceptos.{i} (no conceptos.{i}.Impuestos)
                .OrderBy(k => ExtractIndex(k, "conceptos"))
                .ToList();


            int idxrubi = 1;
            foreach (var secName in conceptSections)
            {
                var sec = parsed[secName];
                var c = new IniConcepto();

                decimal? descuento = GetDecimalFromDict(sec, "descuento");

                if (descuento.HasValue && descuento.Value > 0m)
                {
                    c.Descuento = descuento.Value;   // o c.Descuento = descuento;
                }




                c.Cantidad = GetDecimalFromDict(sec, "cantidad");
                c.Unidad = GetStringFromDict(sec, "unidad");
                c.ClaveUnidad = GetStringFromDict(sec, "ClaveUnidad") ?? GetStringFromDict(sec, "ClaveUnidad");
                c.Id = idxrubi.ToString();
              //  c.NoIdentificacion = GetStringFromDict(sec, "NoIdentificacion");
                c.Descripcion = GetStringFromDict(sec, "descripcion");
                c.ValorUnitario = GetDecimalFromDict(sec, "valorunitario");
                c.Importe = GetDecimalFromDict(sec, "importe");
            
                c.ClaveProdServ = GetStringFromDict(sec, "ClaveProdServ") ?? GetStringFromDict(sec, "claveprodserv");
                c.ObjetoImp = GetStringFromDict(sec, "ObjetoImp") ?? GetStringFromDict(sec, "objetoimp");
                

                // impuestos de concepto -> traslados y retenciones
                var idx = ExtractIndex(secName, "conceptos");
                ReadConceptoImpuestos(parsed, idx, c);

                // extras en el concepto (claves que no mapeamos)
                foreach (var kv in sec)
                {
                    if (IsKnownConceptKey(kv.Key)) continue;
                    c.Extra[kv.Key] = kv.Value;
                }

                doc.Conceptos.Add(c);
                idxrubi++;
            }
        }

        private static void ReadConceptoImpuestos(Dictionary<string, Dictionary<string, string>> parsed, int index, IniConcepto c)
        {
            // Traslados: [conceptos.{i}.Impuestos.Traslados.{t}]
            var prefT = $"conceptos.{index}.Impuestos.Traslados.";
            var trasladoSections = parsed.Keys
                .Where(k => k.StartsWith(prefT, StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => ExtractTrailingIndex(k))
                .ToList();

            foreach (var secName in trasladoSections)
            {
                var sec = parsed[secName];
                c.Impuestos.Traslados.Add(new IniTraslado
                {
                    Base = GetDecimalFromDict(sec, "Base"),
                    Impuesto = GetStringFromDict(sec, "Impuesto"),
                    TipoFactor = GetStringFromDict(sec, "TipoFactor"),
                    TasaOCuota = GetStringFromDict(sec, "TasaOCuota"),
                    Importe = GetDecimalFromDict(sec, "Importe"),
                });
            }

            // Retenciones: [conceptos.{i}.Impuestos.Retenciones.{t}]
            var prefR = $"conceptos.{index}.Impuestos.Retenciones.";
            var retSections = parsed.Keys
                .Where(k => k.StartsWith(prefR, StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => ExtractTrailingIndex(k))
                .ToList();

            foreach (var secName in retSections)
            {
                var sec = parsed[secName];
                c.Impuestos.Retenciones.Add(new IniRetencion
                {
                    Base = GetDecimalFromDict(sec, "Base"),
                    Impuesto = GetStringFromDict(sec, "Impuesto"),
                    TipoFactor = GetStringFromDict(sec, "TipoFactor"),
                    TasaOCuota = GetStringFromDict(sec, "TasaOCuota"),
                    Importe = GetDecimalFromDict(sec, "Importe"),
                });
            }
        }

        private static void ReadImpuestosGlobal(Dictionary<string, Dictionary<string, string>> parsed, IniCfdiDocument doc)
        {
            if (!parsed.TryGetValue("impuestos", out var sec))
                return;

            doc.Impuestos.TotalImpuestosTrasladados = GetDecimalFromDict(sec, "TotalImpuestosTrasladados");
         //   doc.Impuestos.TotalImpuestosRetenidos = GetDecimalFromDict(sec, "TotalImpuestosRetenidos");

            foreach (var kv in sec)
            {
                if (IsKnownImpuestosGlobalKey(kv.Key)) continue;
                doc.Impuestos.Extra[kv.Key] = kv.Value;
            }

            // traslados globales:
            // [impuestos.traslados.0]
            var trasSections = parsed.Keys
                .Where(k => k.StartsWith("impuestos.translados.", StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => ExtractTrailingIndex(k))
                .ToList();

            foreach (var secName in trasSections)
            {
                var s = parsed[secName];
                doc.Impuestos.Translados.Add(new IniTrasladoGlobal
                {
                    Base = GetDecimalFromDict(s, "Base"),
                    impuesto = GetStringFromDict(s, "impuesto") ?? GetStringFromDict(s, "Impuesto"),
                    tasa = GetStringFromDict(s, "tasaocuota") ?? GetStringFromDict(s, "TasaOCuota"),
                    importe = GetDecimalFromDict(s, "importe") ?? GetDecimalFromDict(s, "Importe"),
                    TipoFactor = GetStringFromDict(s, "TipoFactor")
                });
            }

            // retenciones globales:
            // [impuestos.retenciones.0]
            var retSections = parsed.Keys
                .Where(k => k.StartsWith("impuestos.retenciones.", StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => ExtractTrailingIndex(k))
                .ToList();

            foreach (var secName in retSections)
            {
                var s = parsed[secName];
                doc.Impuestos.Retenciones.Add(new IniRetencionGlobal
                {
                    Impuesto = GetStringFromDict(s, "impuesto") ?? GetStringFromDict(s, "Impuesto"),
                    Importe = GetDecimalFromDict(s, "importe") ?? GetDecimalFromDict(s, "Importe")
                });
            }
        }

        private static void ReadPagos20(Dictionary<string, Dictionary<string, string>> parsed, IniCfdiDocument doc)
        {
            // Si no hay secciones pagos20.* no hacemos nada
            var hasPagos20 = parsed.Keys.Any(k => k.StartsWith("pagos20", StringComparison.OrdinalIgnoreCase));
            if (!hasPagos20) return;

            doc.Complementos.Pagos20 ??= new IniPagos20();

            // Totales: [pagos20.Totales]
            if (parsed.TryGetValue("pagos20.Totales", out var tot))
            {
                doc.Complementos.Pagos20.Totales.MontoTotalPagos = GetDecimalFromDict(tot, "MontoTotalPagos");

                doc.Complementos.Pagos20.Totales.TotalTrasladosBaseIVA16 = GetDecimalFromDict(tot, "TotalTrasladosBaseIVA16");
                doc.Complementos.Pagos20.Totales.TotalTrasladosImpuestoIVA16 = GetDecimalFromDict(tot, "TotalTrasladosImpuestoIVA16");
                doc.Complementos.Pagos20.Totales.TotalTrasladosBaseIVA08 = GetDecimalFromDict(tot, "TotalTrasladosBaseIVA08");
                doc.Complementos.Pagos20.Totales.TotalTrasladosImpuestoIVA08 = GetDecimalFromDict(tot, "TotalTrasladosImpuestoIVA08");
                doc.Complementos.Pagos20.Totales.TotalTrasladosBaseIVA00 = GetDecimalFromDict(tot, "TotalTrasladosBaseIVA00");
                doc.Complementos.Pagos20.Totales.TotalTrasladosImpuestoIVA00 = GetDecimalFromDict(tot, "TotalTrasladosImpuestoIVA00");

                doc.Complementos.Pagos20.Totales.TotalRetencionesISR = GetDecimalFromDict(tot, "TotalRetencionesISR");
                doc.Complementos.Pagos20.Totales.TotalRetencionesIVA = GetDecimalFromDict(tot, "TotalRetencionesIVA");
                doc.Complementos.Pagos20.Totales.TotalRetencionesIEPS = GetDecimalFromDict(tot, "TotalRetencionesIEPS");

                foreach (var kv in tot)
                {
                    if (IsKnownPagosTotalesKey(kv.Key)) continue;
                    doc.Complementos.Pagos20.Totales.Extra[kv.Key] = kv.Value;
                }
            }

            // Pagos: [pagos20.Pagos.0], [pagos20.Pagos.1]...
            var pagoSections = parsed.Keys
                .Where(k => k.StartsWith("pagos20.Pagos.", StringComparison.OrdinalIgnoreCase))
                .Where(k => IsDirectIndexSection(k, "pagos20.Pagos")) // pagos20.Pagos.{i}
                .OrderBy(k => ExtractIndex(k, "pagos20.Pagos"))
                .ToList();

            foreach (var secName in pagoSections)
            {
                var s = parsed[secName];
                var idx = ExtractIndex(secName, "pagos20.Pagos");

                var p = new IniPago20Pago
                {
                    FechaPago = GetStringFromDict(s, "FechaPago"),
                    FormaDePagoP = GetStringFromDict(s, "FormaDePagoP"),
                    MonedaP = GetStringFromDict(s, "MonedaP"),
                    TipoCambioP = GetDecimalFromDict(s, "TipoCambioP"),
                    Monto = GetDecimalFromDict(s, "Monto"),
                    NumOperacion = GetStringFromDict(s, "NumOperacion"),

                    RfcEmisorCtaOrd = GetStringFromDict(s, "RfcEmisorCtaOrd"),
                    NomBancoOrdExt = GetStringFromDict(s, "NomBancoOrdExt"),
                    CtaOrdenante = GetStringFromDict(s, "CtaOrdenante"),

                    RfcEmisorCtaBen = GetStringFromDict(s, "RfcEmisorCtaBen"),
                    CtaBeneficiario = GetStringFromDict(s, "CtaBeneficiario"),

                    TipoCadPago = GetStringFromDict(s, "TipoCadPago"),
                    CertPago = GetStringFromDict(s, "CertPago"),
                    CadPago = GetStringFromDict(s, "CadPago"),
                    SelloPago = GetStringFromDict(s, "SelloPago"),
                };

                foreach (var kv in s)
                {
                    if (IsKnownPagoKey(kv.Key)) continue;
                    p.Extra[kv.Key] = kv.Value;
                }

                // Doctos relacionados: [pagos20.Pagos.{i}.DoctoRelacionado.{d}]
                var drPrefix = $"pagos20.Pagos.{idx}.DoctoRelacionado.";
                var drSections = parsed.Keys
                    .Where(k => k.StartsWith(drPrefix, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(k => ExtractTrailingIndex(k))
                    .ToList();

                foreach (var drSecName in drSections)
                {
                    var ds = parsed[drSecName];

                    var dr = new IniPago20DoctoRelacionado
                    {
                        IdDocumento = GetStringFromDict(ds, "IdDocumento"),
                        Serie = GetStringFromDict(ds, "Serie"),
                        Folio = GetStringFromDict(ds, "Folio"),
                        MonedaDR = GetStringFromDict(ds, "MonedaDR"),
                        EquivalenciaDR = GetDecimalFromDict(ds, "EquivalenciaDR"),
                        NumParcialidad = GetIntFromDict(ds, "NumParcialidad"),
                        ImpSaldoAnt = GetDecimalFromDict(ds, "ImpSaldoAnt"),
                        ImpPagado = GetDecimalFromDict(ds, "ImpPagado"),
                        ImpSaldoInsoluto = GetDecimalFromDict(ds, "ImpSaldoInsoluto"),
                        ObjetoImpDR = GetStringFromDict(ds, "ObjetoImpDR"),
                    };

                    foreach (var kv in ds)
                    {
                        if (IsKnownDoctoRelKey(kv.Key)) continue;
                        dr.Extra[kv.Key] = kv.Value;
                    }

                    // ImpuestosDR: [pagos20.Pagos.{i}.DoctoRelacionado.{d}.ImpuestosDR.TrasladosDR.{t}]
                    ReadPagoImpuestosDR(parsed, drSecName, dr);

                    p.DoctosRelacionados.Add(dr);
                }

                // ImpuestosP: [pagos20.Pagos.{i}.ImpuestosP.TrasladosP.{t}] y RetencionesP
                ReadPagoImpuestosP(parsed, idx, p);

                doc.Complementos.Pagos20.Pagos.Add(p);
            }
        }

        private static void ReadPagoImpuestosP(Dictionary<string, Dictionary<string, string>> parsed, int pagoIndex, IniPago20Pago p)
        {
            var trasPrefix = $"pagos20.Pagos.{pagoIndex}.ImpuestosP.TrasladosP.";
            var trasSections = parsed.Keys
                .Where(k => k.StartsWith(trasPrefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => ExtractTrailingIndex(k))
                .ToList();

            foreach (var secName in trasSections)
            {
                var s = parsed[secName];
                p.ImpuestosP.TrasladosP.Add(new IniPago20TrasladoP
                {
                    BaseP = GetDecimalFromDict(s, "BaseP"),
                    ImpuestoP = GetStringFromDict(s, "ImpuestoP"),
                    TipoFactorP = GetStringFromDict(s, "TipoFactorP"),
                    TasaOCuotaP = GetDecimalFromDict(s, "TasaOCuotaP"),
                    ImporteP = GetDecimalFromDict(s, "ImporteP")
                });
            }

            var retPrefix = $"pagos20.Pagos.{pagoIndex}.ImpuestosP.RetencionesP.";
            var retSections = parsed.Keys
                .Where(k => k.StartsWith(retPrefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => ExtractTrailingIndex(k))
                .ToList();

            foreach (var secName in retSections)
            {
                var s = parsed[secName];
                p.ImpuestosP.RetencionesP.Add(new IniPago20RetencionP
                {
                    ImpuestoP = GetStringFromDict(s, "ImpuestoP"),
                    ImporteP = GetDecimalFromDict(s, "ImporteP")
                });
            }
        }

        private static void ReadPagoImpuestosDR(Dictionary<string, Dictionary<string, string>> parsed, string drSectionName, IniPago20DoctoRelacionado dr)
        {
            // drSectionName = "pagos20.Pagos.0.DoctoRelacionado.0"
            var trasPrefix = drSectionName + ".ImpuestosDR.TrasladosDR.";
            var trasSections = parsed.Keys
                .Where(k => k.StartsWith(trasPrefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => ExtractTrailingIndex(k))
                .ToList();

            foreach (var secName in trasSections)
            {
                var s = parsed[secName];
                dr.ImpuestosDR.TrasladosDR.Add(new IniPago20TrasladoDR
                {
                    BaseDR = GetDecimalFromDict(s, "BaseDR"),
                    ImpuestoDR = GetStringFromDict(s, "ImpuestoDR"),
                    TipoFactorDR = GetStringFromDict(s, "TipoFactorDR"),
                    TasaOCuotaDR = GetDecimalFromDict(s, "TasaOCuotaDR"),
                    ImporteDR = GetDecimalFromDict(s, "ImporteDR")
                });
            }

            var retPrefix = drSectionName + ".ImpuestosDR.RetencionesDR.";
            var retSections = parsed.Keys
                .Where(k => k.StartsWith(retPrefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => ExtractTrailingIndex(k))
                .ToList();

            foreach (var secName in retSections)
            {
                var s = parsed[secName];
                dr.ImpuestosDR.RetencionesDR.Add(new IniPago20RetencionDR
                {
                    ImpuestoDR = GetStringFromDict(s, "ImpuestoDR"),
                    ImporteDR = GetDecimalFromDict(s, "ImporteDR")
                });
            }
        }

        // ======================================================
        // Raw: guarda todo lo que no sea “básico” ya mapeado
        // ======================================================
        private static void FillRaw(Dictionary<string, Dictionary<string, string>> parsed, IniCfdiDocument doc)
        {
            foreach (var sec in parsed)
            {
                var secName = sec.Key.Trim();
                var dict = sec.Value;

                foreach (var kv in dict)
                {
                    // Guardamos en Raw con formato: "seccion.clave" o "clave" si root
                    var rawKey = string.IsNullOrWhiteSpace(secName)
                        ? kv.Key
                        : $"{secName}.{kv.Key}";

                    if (!doc.Raw.ContainsKey(rawKey))
                        doc.Raw[rawKey] = kv.Value;
                }
            }
        }

        // ======================================================
        // Helpers de lectura
        // ======================================================
        private static string? GetString(Dictionary<string, Dictionary<string, string>> parsed, string section, string key)
        {
            if (!parsed.TryGetValue(section, out var sec)) return null;
            return GetStringFromDict(sec, key);
        }

        private static string? GetStringFromDict(Dictionary<string, string> sec, string key)
        {
            if (sec.TryGetValue(key, out var v)) return v;
            return null;
        }

        private static decimal? GetDecimal(Dictionary<string, Dictionary<string, string>> parsed, string section, string key)
        {
            if (!parsed.TryGetValue(section, out var sec)) return null;
            return GetDecimalFromDict(sec, key);
        }

        private static decimal? GetDecimalFromDict(Dictionary<string, string> sec, string key)
        {
            if (!sec.TryGetValue(key, out var v)) return null;
            if (string.IsNullOrWhiteSpace(v)) return null;

            v = v.Trim();

            // Intenta Invariant
            if (decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                return d;

            // Intenta cultura es-MX (por si vienen comas)
            if (decimal.TryParse(v, NumberStyles.Any, new CultureInfo("es-MX"), out d))
                return d;

            return null;
        }

        private static int? GetIntFromDict(Dictionary<string, string> sec, string key)
        {
            if (!sec.TryGetValue(key, out var v)) return null;
            if (string.IsNullOrWhiteSpace(v)) return null;

            if (int.TryParse(v.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var i))
                return i;

            if (int.TryParse(v.Trim(), NumberStyles.Any, new CultureInfo("es-MX"), out i))
                return i;

            return null;
        }

        private static List<string> SplitUuids(string value)
        {
            return value.Split(new[] { ',', ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToList();
        }

        // ======================================================
        // Detección de secciones indexadas
        // ======================================================
        // true para "conceptos.0" pero false para "conceptos.0.Impuestos"
        private static bool IsDirectIndexSection(string section, string basePath)
        {
            // Ej section="conceptos.0" basePath="conceptos"
            // Ej section="pagos20.Pagos.0" basePath="pagos20.Pagos"

            if (!section.StartsWith(basePath + ".", StringComparison.OrdinalIgnoreCase))
                return false;

            var rest = section.Substring(basePath.Length + 1); // "0" o "0.Impuestos..."
            if (string.IsNullOrWhiteSpace(rest)) return false;

            // Debe ser solo número, sin más puntos
            if (rest.Contains('.')) return false;

            return int.TryParse(rest, out _);
        }

        private static int ExtractIndex(string section, string basePath)
        {
            // section="conceptos.3" basePath="conceptos"
            var rest = section.Substring(basePath.Length + 1);
            if (!int.TryParse(rest, out var idx)) return -1;
            return idx;
        }

        private static int ExtractTrailingIndex(string sectionName)
        {
            // section="conceptos.0.Impuestos.Traslados.3" -> 3
            var lastDot = sectionName.LastIndexOf('.');
            if (lastDot < 0) return -1;

            var tail = sectionName.Substring(lastDot + 1);
            return int.TryParse(tail, out var idx) ? idx : -1;
        }

        // ======================================================
        // Keys conocidas para evitar meterlas en "Extra" (no crítico)
        // ======================================================
        private static bool IsKnownConceptKey(string k)
        {
            return k.Equals("cantidad", StringComparison.OrdinalIgnoreCase)
                || k.Equals("unidad", StringComparison.OrdinalIgnoreCase)
                || k.Equals("ClaveUnidad", StringComparison.OrdinalIgnoreCase)
                || k.Equals("ID", StringComparison.OrdinalIgnoreCase)
                || k.Equals("Id", StringComparison.OrdinalIgnoreCase)
                || k.Equals("NoIdentificacion", StringComparison.OrdinalIgnoreCase)
                || k.Equals("descripcion", StringComparison.OrdinalIgnoreCase)
                || k.Equals("valorunitario", StringComparison.OrdinalIgnoreCase)
                || k.Equals("importe", StringComparison.OrdinalIgnoreCase)
                || k.Equals("descuento", StringComparison.OrdinalIgnoreCase)
                || k.Equals("ClaveProdServ", StringComparison.OrdinalIgnoreCase)
                || k.Equals("ObjetoImp", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsKnownImpuestosGlobalKey(string k)
        {
            return k.Equals("TotalImpuestosTrasladados", StringComparison.OrdinalIgnoreCase)
                || k.Equals("TotalImpuestosRetenidos", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsKnownPagosTotalesKey(string k)
        {
            return k.Equals("MontoTotalPagos", StringComparison.OrdinalIgnoreCase)
                || k.StartsWith("TotalTraslados", StringComparison.OrdinalIgnoreCase)
                || k.StartsWith("TotalRetenciones", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsKnownPagoKey(string k)
        {
            return k.Equals("FechaPago", StringComparison.OrdinalIgnoreCase)
                || k.Equals("FormaDePagoP", StringComparison.OrdinalIgnoreCase)
                || k.Equals("MonedaP", StringComparison.OrdinalIgnoreCase)
                || k.Equals("TipoCambioP", StringComparison.OrdinalIgnoreCase)
                || k.Equals("Monto", StringComparison.OrdinalIgnoreCase)
                || k.Equals("NumOperacion", StringComparison.OrdinalIgnoreCase)
                || k.Equals("RfcEmisorCtaOrd", StringComparison.OrdinalIgnoreCase)
                || k.Equals("NomBancoOrdExt", StringComparison.OrdinalIgnoreCase)
                || k.Equals("CtaOrdenante", StringComparison.OrdinalIgnoreCase)
                || k.Equals("RfcEmisorCtaBen", StringComparison.OrdinalIgnoreCase)
                || k.Equals("CtaBeneficiario", StringComparison.OrdinalIgnoreCase)
                || k.Equals("TipoCadPago", StringComparison.OrdinalIgnoreCase)
                || k.Equals("CertPago", StringComparison.OrdinalIgnoreCase)
                || k.Equals("CadPago", StringComparison.OrdinalIgnoreCase)
                || k.Equals("SelloPago", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsKnownDoctoRelKey(string k)
        {
            return k.Equals("IdDocumento", StringComparison.OrdinalIgnoreCase)
                || k.Equals("Serie", StringComparison.OrdinalIgnoreCase)
                || k.Equals("Folio", StringComparison.OrdinalIgnoreCase)
                || k.Equals("MonedaDR", StringComparison.OrdinalIgnoreCase)
                || k.Equals("EquivalenciaDR", StringComparison.OrdinalIgnoreCase)
                || k.Equals("NumParcialidad", StringComparison.OrdinalIgnoreCase)
                || k.Equals("ImpSaldoAnt", StringComparison.OrdinalIgnoreCase)
                || k.Equals("ImpPagado", StringComparison.OrdinalIgnoreCase)
                || k.Equals("ImpSaldoInsoluto", StringComparison.OrdinalIgnoreCase)
                || k.Equals("ObjetoImpDR", StringComparison.OrdinalIgnoreCase);
        }
    }
}
