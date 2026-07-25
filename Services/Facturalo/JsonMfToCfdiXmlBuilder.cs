using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using Vigma.TimbradoGateway.Models;

namespace Vigma.TimbradoGateway.Services.Facturalo;

/// <summary>
/// Convierte un JObject con la estructura JSON estilo MultiFacturas
/// (la que produce IniToMfRequestMapper o la que envía el cliente al endpoint /json)
/// en un CFDI 4.0 XML SIN atributo Sello.
///
/// FacturaLO PLUS recibirá este XML + el keyPEM y se encargará de sellar y timbrar.
///
/// Soporta: TipoDeComprobante I, E, P (Pagos 2.0).
/// Inyecta NoCertificado y el certificado .cer.pem en base64 desde el modelo Certificado.
/// </summary>
public sealed class JsonMfToCfdiXmlBuilder
{
    // ── Namespaces CFDI 4.0 ──────────────────────────────────────────────────
    private static readonly XNamespace NsCfdi  = "http://www.sat.gob.mx/cfd/4";
    private static readonly XNamespace NsXsi   = "http://www.w3.org/2001/XMLSchema-instance";
    private static readonly XNamespace NsPago  = "http://www.sat.gob.mx/Pagos20";

    private const string SchemaLocCfdi =
        "http://www.sat.gob.mx/cfd/4 http://www.sat.gob.mx/sitio_internet/cfd/4/cfdv40.xsd";
    private const string SchemaLocPago =
        "http://www.sat.gob.mx/Pagos20 http://www.sat.gob.mx/sitio_internet/cfd/Pagos/Pagos20.xsd";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    /// Construye el XML CFDI 4.0 sin Sello a partir del JSON estilo MF.
    /// </summary>
    /// <param name="jsonMf">JObject con: factura, emisor, receptor, conceptos, impuestos, pagos20, CfdisRelacionados, InformacionGlobal.</param>
    /// <param name="cert">Certificado del tenant (proporciona NoCertificado y la ruta del .cer.pem).</param>
    public string BuildXmlSinSello(JObject jsonMf, Certificado cert)
    {
        if (jsonMf is null) throw new ArgumentNullException(nameof(jsonMf));
        if (cert   is null) throw new ArgumentNullException(nameof(cert));

        var factura  = jsonMf["factura"]  as JObject ?? throw new ArgumentException("Falta nodo 'factura' en el JSON.");
        var emisor   = jsonMf["emisor"]   as JObject ?? throw new ArgumentException("Falta nodo 'emisor' en el JSON.");
        var receptor = jsonMf["receptor"] as JObject ?? throw new ArgumentException("Falta nodo 'receptor' en el JSON.");

        var tipoComp = (factura.Value<string>("tipocomprobante") ?? "").Trim().ToUpperInvariant();
        if (tipoComp is not ("I" or "E" or "P"))
            throw new InvalidOperationException(
                $"TipoDeComprobante '{tipoComp}' no soportado por FacturaLO en este builder. Soportados: I, E, P.");

        // ── 1) Datos del certificado ─────────────────────────────────────────
        var noCertificado = NormalizarNoCertificado(cert.NoCertificado);
        if (string.IsNullOrWhiteSpace(noCertificado))
            throw new InvalidOperationException("El certificado del tenant no tiene 'no_certificado'.");

        var pemPath = ResolveCerPemPath(cert);
        var certBase64 = LeerCertificadoEnBase64(pemPath);

        // ── 2) Comprobante (atributos) ───────────────────────────────────────
        var comprobante = new XElement(NsCfdi + "Comprobante",
            new XAttribute(XNamespace.Xmlns + "cfdi", NsCfdi.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "xsi",  NsXsi.NamespaceName)
        );

        // Schema location (con o sin pagos20)
        var schemaLoc = SchemaLocCfdi;
        var hasPagos = tipoComp == "P" && jsonMf["pagos20"] is JObject;
        if (hasPagos)
        {
            comprobante.Add(new XAttribute(XNamespace.Xmlns + "pago20", NsPago.NamespaceName));
            schemaLoc = SchemaLocCfdi + " " + SchemaLocPago;
        }
        comprobante.Add(new XAttribute(NsXsi + "schemaLocation", schemaLoc));

        // Atributos requeridos
        AddAttr(comprobante, "Version", factura.Value<string>("version") ?? jsonMf.Value<string>("version_cfdi") ?? "4.0");
        AddAttrIfPresent(comprobante, "Serie",  factura.Value<string>("serie"));
        AddAttrIfPresent(comprobante, "Folio",  factura.Value<string>("folio"));

        var fechaIso = ResolverFecha(factura.Value<string>("fecha_expedicion"));
        AddAttr(comprobante, "Fecha", fechaIso);

        // FormaPago: requerido para I/E (cuando MetodoPago=PUE) y para Pagos suele ser opcional.
        // Para no fallar, lo incluimos si viene.
        AddAttrIfPresent(comprobante, "FormaPago", factura.Value<string>("forma_pago"));

        AddAttr(comprobante, "NoCertificado", noCertificado);
        AddAttr(comprobante, "Certificado",  certBase64);

        // CondicionesDePago (solo I/E)
        if (tipoComp != "P")
            AddAttrIfPresent(comprobante, "CondicionesDePago", factura.Value<string>("condicionesDePago"));

        // Totales
        // En P: SubTotal=0, Total=0 por regla del SAT
        if (tipoComp == "P")
        {
            AddAttr(comprobante, "SubTotal", "0");
        }
        else
        {
            AddAttr(comprobante, "SubTotal", FmtMonto(factura.Value<string>("subtotal")));
            var descStr = factura.Value<string>("descuento");
            if (!string.IsNullOrWhiteSpace(descStr) && decimal.TryParse(descStr, NumberStyles.Any, Inv, out var d) && d > 0m)
                AddAttr(comprobante, "Descuento", FmtMonto(descStr));
        }

        AddAttr(comprobante, "Moneda", factura.Value<string>("moneda") ?? "MXN");
        var tc = factura.Value<string>("tipocambio");
        if (!string.IsNullOrWhiteSpace(tc) && !string.Equals(factura.Value<string>("moneda"), "MXN", StringComparison.OrdinalIgnoreCase))
            AddAttr(comprobante, "TipoCambio", FmtDecimal(tc, 6));

        if (tipoComp == "P")
            AddAttr(comprobante, "Total", "0");
        else
            AddAttr(comprobante, "Total", FmtMonto(factura.Value<string>("total")));

        AddAttr(comprobante, "TipoDeComprobante", tipoComp);
        AddAttr(comprobante, "Exportacion", factura.Value<string>("Exportacion") ?? factura.Value<string>("exportacion") ?? "01");

        // MetodoPago: requerido para I/E
        if (tipoComp != "P")
            AddAttrIfPresent(comprobante, "MetodoPago", factura.Value<string>("metodo_pago"));

        AddAttrIfPresent(comprobante, "LugarExpedicion", factura.Value<string>("LugarExpedicion") ?? factura.Value<string>("lugarexpedicion"));
        AddAttrIfPresent(comprobante, "Confirmacion", factura.Value<string>("Confirmacion"));

        // ── 3) InformacionGlobal (opcional) ──────────────────────────────────
        if (jsonMf["InformacionGlobal"] is JObject ig && ig.HasValues)
        {
            var elIg = new XElement(NsCfdi + "InformacionGlobal");
            CopyAttrsFromObject(elIg, ig, "Periodicidad", "Meses", "Año");
            comprobante.Add(elIg);
        }

        // ── 4) CfdiRelacionados (opcional) — viene como arreglo desde MF ─────
        if (jsonMf["CfdisRelacionados"] is JArray rels && rels.Count > 0)
        {
            foreach (var rel in rels.OfType<JObject>())
            {
                var elRels = new XElement(NsCfdi + "CfdiRelacionados");
                AddAttrIfPresent(elRels, "TipoRelacion", rel.Value<string>("TipoRelacion"));

                if (rel["UUID"] is JArray uuids)
                {
                    foreach (var u in uuids)
                    {
                        var uu = u?.ToString();
                        if (string.IsNullOrWhiteSpace(uu)) continue;
                        var elRel = new XElement(NsCfdi + "CfdiRelacionado",
                            new XAttribute("UUID", uu));
                        elRels.Add(elRel);
                    }
                }
                comprobante.Add(elRels);
            }
        }

        // ── 5) Emisor ─────────────────────────────────────────────────────────
        var elEmisor = new XElement(NsCfdi + "Emisor");
        AddAttr(elEmisor, "Rfc", emisor.Value<string>("rfc") ?? "");
        AddAttr(elEmisor, "Nombre", emisor.Value<string>("nombre") ?? "");
        AddAttr(elEmisor, "RegimenFiscal", emisor.Value<string>("RegimenFiscal") ?? emisor.Value<string>("regimenfiscal") ?? "");
        AddAttrIfPresent(elEmisor, "FacAtrAdquirente", emisor.Value<string>("FacAtrAdquirente"));
        comprobante.Add(elEmisor);

        // ── 6) Receptor ───────────────────────────────────────────────────────
        var elReceptor = new XElement(NsCfdi + "Receptor");
        AddAttr(elReceptor, "Rfc", receptor.Value<string>("rfc") ?? "");
        AddAttr(elReceptor, "Nombre", receptor.Value<string>("nombre") ?? "");
        AddAttr(elReceptor, "DomicilioFiscalReceptor", receptor.Value<string>("DomicilioFiscalReceptor") ?? receptor.Value<string>("domiciliofiscalreceptor") ?? "");
        AddAttrIfPresent(elReceptor, "ResidenciaFiscal", receptor.Value<string>("ResidenciaFiscal"));
        AddAttrIfPresent(elReceptor, "NumRegIdTrib", receptor.Value<string>("NumRegIdTrib"));
        AddAttr(elReceptor, "RegimenFiscalReceptor", receptor.Value<string>("RegimenFiscalReceptor") ?? receptor.Value<string>("regimenfiscalreceptor") ?? "");
        AddAttr(elReceptor, "UsoCFDI", receptor.Value<string>("UsoCFDI") ?? receptor.Value<string>("usocfdi") ?? "");
        comprobante.Add(elReceptor);

        // ── 7) Conceptos ──────────────────────────────────────────────────────
        var elConceptos = new XElement(NsCfdi + "Conceptos");
        if (jsonMf["conceptos"] is JArray conceptos)
        {
            foreach (var c in conceptos.OfType<JObject>())
                elConceptos.Add(BuildConcepto(c, tipoComp));
        }
        comprobante.Add(elConceptos);

        // ── 8) Impuestos globales (solo I/E) ──────────────────────────────────
        if (tipoComp != "P" && jsonMf["impuestos"] is JObject impG && impG.HasValues)
        {
            var elImp = BuildImpuestosGlobales(impG);
            if (elImp.HasElements || elImp.HasAttributes)
                comprobante.Add(elImp);
        }

        // ── 9) Complemento Pagos20 ────────────────────────────────────────────
        if (hasPagos && jsonMf["pagos20"] is JObject p20)
        {
            var elComplemento = new XElement(NsCfdi + "Complemento");
            elComplemento.Add(BuildPagos20(p20));
            comprobante.Add(elComplemento);
        }

        // ── 10) Serializar ────────────────────────────────────────────────────
        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            comprobante);

        return SerializeUtf8(doc);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  CONCEPTOS
    // ─────────────────────────────────────────────────────────────────────────
    private XElement BuildConcepto(JObject c, string tipoComp)
    {
        var el = new XElement(NsCfdi + "Concepto");

        AddAttr(el, "ClaveProdServ", c.Value<string>("ClaveProdServ") ?? "");
        AddAttrIfPresent(el, "NoIdentificacion", c.Value<string>("NoIdentificacion"));

        // Para Pagos: Cantidad=1, ClaveUnidad=ACT, ValorUnitario=0, Importe=0, Descripcion="Pago"
        if (tipoComp == "P")
        {
            AddAttr(el, "Cantidad", "1");
            AddAttr(el, "ClaveUnidad", c.Value<string>("ClaveUnidad") ?? "ACT");
            AddAttr(el, "Descripcion", c.Value<string>("Descripcion") ?? "Pago");
            AddAttr(el, "ValorUnitario", "0");
            AddAttr(el, "Importe", "0");
            AddAttr(el, "ObjetoImp", c.Value<string>("ObjetoImp") ?? "01");
            return el;
        }

        AddAttr(el, "Cantidad", FmtDecimal(c.Value<string>("Cantidad"), 6));
        AddAttr(el, "ClaveUnidad", c.Value<string>("ClaveUnidad") ?? "");
        AddAttrIfPresent(el, "Unidad", c.Value<string>("Unidad"));
        AddAttr(el, "Descripcion", c.Value<string>("Descripcion") ?? "");
        AddAttr(el, "ValorUnitario", FmtMonto(c.Value<string>("ValorUnitario")));
        AddAttr(el, "Importe", FmtMonto(c.Value<string>("Importe")));

        var descStr = c.Value<string>("Descuento");
        if (!string.IsNullOrWhiteSpace(descStr) && decimal.TryParse(descStr, NumberStyles.Any, Inv, out var d) && d > 0m)
            AddAttr(el, "Descuento", FmtMonto(descStr));

        AddAttr(el, "ObjetoImp", c.Value<string>("ObjetoImp") ?? "02");

        // Impuestos del concepto
        if (c["Impuestos"] is JObject impC && impC.HasValues)
        {
            var elImpC = new XElement(NsCfdi + "Impuestos");

            if (impC["Traslados"] is JArray tras && tras.Count > 0)
            {
                var elTras = new XElement(NsCfdi + "Traslados");
                foreach (var t in tras.OfType<JObject>())
                {
                    var elT = new XElement(NsCfdi + "Traslado");
                    AddAttr(elT, "Base",       FmtMonto(t.Value<string>("Base")));
                    AddAttr(elT, "Impuesto",   t.Value<string>("Impuesto") ?? "");
                    AddAttr(elT, "TipoFactor", t.Value<string>("TipoFactor") ?? "");
                    var tipoFactor = t.Value<string>("TipoFactor") ?? "";
                    if (!string.Equals(tipoFactor, "Exento", StringComparison.OrdinalIgnoreCase))
                    {
                        AddAttr(elT, "TasaOCuota", FmtDecimal(t.Value<string>("TasaOCuota"), 6));
                        AddAttr(elT, "Importe",    FmtMonto(t.Value<string>("Importe")));
                    }
                    elTras.Add(elT);
                }
                if (elTras.HasElements) elImpC.Add(elTras);
            }

            if (impC["Retenciones"] is JArray rets && rets.Count > 0)
            {
                var elRets = new XElement(NsCfdi + "Retenciones");
                foreach (var r in rets.OfType<JObject>())
                {
                    var elR = new XElement(NsCfdi + "Retencion");
                    AddAttr(elR, "Base",       FmtMonto(r.Value<string>("Base")));
                    AddAttr(elR, "Impuesto",   r.Value<string>("Impuesto") ?? "");
                    AddAttr(elR, "TipoFactor", r.Value<string>("TipoFactor") ?? "Tasa");
                    AddAttr(elR, "TasaOCuota", FmtDecimal(r.Value<string>("TasaOCuota"), 6));
                    AddAttr(elR, "Importe",    FmtMonto(r.Value<string>("Importe")));
                    elRets.Add(elR);
                }
                if (elRets.HasElements) elImpC.Add(elRets);
            }

            if (elImpC.HasElements) el.Add(elImpC);
        }

        return el;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  IMPUESTOS GLOBALES (Comprobante.Impuestos)
    // ─────────────────────────────────────────────────────────────────────────
    private XElement BuildImpuestosGlobales(JObject imp)
    {
        var el = new XElement(NsCfdi + "Impuestos");

        // TotalImpuestosRetenidos
        var totRet = imp.Value<string>("TotalImpuestosRetenidos");
        if (!string.IsNullOrWhiteSpace(totRet))
            AddAttr(el, "TotalImpuestosRetenidos", FmtMonto(totRet));

        // TotalImpuestosTrasladados
        var totTras = imp.Value<string>("TotalImpuestosTrasladados");
        if (!string.IsNullOrWhiteSpace(totTras))
            AddAttr(el, "TotalImpuestosTrasladados", FmtMonto(totTras));

        // Retenciones (mapper las llama "retenciones" minúsculas)
        var arrRetA = imp["retenciones"] as JArray ?? imp["Retenciones"] as JArray;
        if (arrRetA != null && arrRetA.Count > 0)
        {
            var elRets = new XElement(NsCfdi + "Retenciones");
            foreach (var r in arrRetA.OfType<JObject>())
            {
                var elR = new XElement(NsCfdi + "Retencion");
                AddAttr(elR, "Impuesto", r.Value<string>("impuesto") ?? r.Value<string>("Impuesto") ?? "");
                AddAttr(elR, "Importe",  FmtMonto(r.Value<string>("importe") ?? r.Value<string>("Importe")));
                elRets.Add(elR);
            }
            if (elRets.HasElements) el.Add(elRets);
        }

        // Traslados (mapper los llama "translados" — typo conservado en el JSON MF)
        var arrTrasA = imp["translados"] as JArray ?? imp["Traslados"] as JArray ?? imp["traslados"] as JArray;
        if (arrTrasA != null && arrTrasA.Count > 0)
        {
            var elTras = new XElement(NsCfdi + "Traslados");
            foreach (var t in arrTrasA.OfType<JObject>())
            {
                var elT = new XElement(NsCfdi + "Traslado");
                AddAttr(elT, "Base",       FmtMonto(t.Value<string>("Base") ?? t.Value<string>("base")));
                AddAttr(elT, "Impuesto",   t.Value<string>("impuesto") ?? t.Value<string>("Impuesto") ?? "");
                AddAttr(elT, "TipoFactor", t.Value<string>("TipoFactor") ?? "Tasa");
                AddAttr(elT, "TasaOCuota", FmtDecimal(t.Value<string>("tasa") ?? t.Value<string>("TasaOCuota"), 6));
                AddAttr(elT, "Importe",    FmtMonto(t.Value<string>("importe") ?? t.Value<string>("Importe")));
                elTras.Add(elT);
            }
            if (elTras.HasElements) el.Add(elTras);
        }

        return el;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  COMPLEMENTO PAGOS 2.0
    // ─────────────────────────────────────────────────────────────────────────
    private XElement BuildPagos20(JObject p20)
    {
        var elPagos = new XElement(NsPago + "Pagos",
            new XAttribute("Version", "2.0"));

        // Totales
        if (p20["Totales"] is JObject tot && tot.HasValues)
        {
            var elTot = new XElement(NsPago + "Totales");

            CopyMontoAttr(elTot, tot, "TotalRetencionesIVA");
            CopyMontoAttr(elTot, tot, "TotalRetencionesISR");
            CopyMontoAttr(elTot, tot, "TotalRetencionesIEPS");

            CopyMontoAttr(elTot, tot, "TotalTrasladosBaseIVA16");
            CopyMontoAttr(elTot, tot, "TotalTrasladosImpuestoIVA16");
            CopyMontoAttr(elTot, tot, "TotalTrasladosBaseIVA8");
            CopyMontoAttr(elTot, tot, "TotalTrasladosImpuestoIVA8");
            CopyMontoAttr(elTot, tot, "TotalTrasladosBaseIVA0");
            CopyMontoAttr(elTot, tot, "TotalTrasladosImpuestoIVA0");
            CopyMontoAttr(elTot, tot, "TotalTrasladosBaseIVAExento");

            // Aliases con 08/00 que produce el mapper
            CopyMontoAttr(elTot, tot, "TotalTrasladosBaseIVA08", attrNameOverride: "TotalTrasladosBaseIVA8");
            CopyMontoAttr(elTot, tot, "TotalTrasladosImpuestoIVA08", attrNameOverride: "TotalTrasladosImpuestoIVA8");
            CopyMontoAttr(elTot, tot, "TotalTrasladosBaseIVA00", attrNameOverride: "TotalTrasladosBaseIVA0");
            CopyMontoAttr(elTot, tot, "TotalTrasladosImpuestoIVA00", attrNameOverride: "TotalTrasladosImpuestoIVA0");

            CopyMontoAttr(elTot, tot, "MontoTotalPagos");

            elPagos.Add(elTot);
        }

        // Pagos
        if (p20["Pagos"] is JArray pagos)
        {
            foreach (var p in pagos.OfType<JObject>())
                elPagos.Add(BuildPago(p));
        }

        return elPagos;
    }

    private XElement BuildPago(JObject p)
    {
        var el = new XElement(NsPago + "Pago");

        AddAttr(el, "FechaPago", p.Value<string>("FechaPago") ?? "");
        AddAttr(el, "FormaDePagoP", p.Value<string>("FormaDePagoP") ?? "");
        AddAttr(el, "MonedaP", p.Value<string>("MonedaP") ?? "MXN");
        if (!string.Equals(p.Value<string>("MonedaP"), "MXN", StringComparison.OrdinalIgnoreCase))
            AddAttrIfPresent(el, "TipoCambioP", FmtDecimal(p.Value<string>("TipoCambioP"), 6));
        AddAttr(el, "Monto", FmtMonto(p.Value<string>("Monto")));

        AddAttrIfPresent(el, "NumOperacion", p.Value<string>("NumOperacion"));
        AddAttrIfPresent(el, "RfcEmisorCtaOrd", p.Value<string>("RfcEmisorCtaOrd"));
        AddAttrIfPresent(el, "NomBancoOrdExt", p.Value<string>("NomBancoOrdExt"));
        AddAttrIfPresent(el, "CtaOrdenante", p.Value<string>("CtaOrdenante"));
        AddAttrIfPresent(el, "RfcEmisorCtaBen", p.Value<string>("RfcEmisorCtaBen"));
        AddAttrIfPresent(el, "CtaBeneficiario", p.Value<string>("CtaBeneficiario"));
        AddAttrIfPresent(el, "TipoCadPago", p.Value<string>("TipoCadPago"));
        AddAttrIfPresent(el, "CertPago", p.Value<string>("CertPago"));
        AddAttrIfPresent(el, "CadPago", p.Value<string>("CadPago"));
        AddAttrIfPresent(el, "SelloPago", p.Value<string>("SelloPago"));

        // DoctoRelacionado (mapper lo guarda en singular como arreglo)
        var docs = p["DoctoRelacionado"] as JArray ?? p["DoctosRelacionados"] as JArray;
        if (docs != null)
        {
            foreach (var d in docs.OfType<JObject>())
                el.Add(BuildDoctoRelacionado(d));
        }

        // ImpuestosP (a nivel pago)
        if (p["ImpuestosP"] is JObject impP && impP.HasValues)
        {
            var elImpP = new XElement(NsPago + "ImpuestosP");

            if (impP["RetencionesP"] is JArray retP && retP.Count > 0)
            {
                var elRets = new XElement(NsPago + "RetencionesP");
                foreach (var r in retP.OfType<JObject>())
                {
                    var elR = new XElement(NsPago + "RetencionP");
                    AddAttr(elR, "ImpuestoP", r.Value<string>("ImpuestoP") ?? "");
                    AddAttr(elR, "ImporteP", FmtMonto(r.Value<string>("ImporteP")));
                    elRets.Add(elR);
                }
                if (elRets.HasElements) elImpP.Add(elRets);
            }

            if (impP["TrasladosP"] is JArray trasP && trasP.Count > 0)
            {
                var elTras = new XElement(NsPago + "TrasladosP");
                foreach (var t in trasP.OfType<JObject>())
                {
                    var elT = new XElement(NsPago + "TrasladoP");
                    AddAttr(elT, "BaseP",       FmtMonto(t.Value<string>("BaseP")));
                    AddAttr(elT, "ImpuestoP",   t.Value<string>("ImpuestoP") ?? "");
                    AddAttr(elT, "TipoFactorP", t.Value<string>("TipoFactorP") ?? "Tasa");
                    AddAttr(elT, "TasaOCuotaP", FmtDecimal(t.Value<string>("TasaOCuotaP"), 6));
                    AddAttr(elT, "ImporteP",    FmtMonto(t.Value<string>("ImporteP")));
                    elTras.Add(elT);
                }
                if (elTras.HasElements) elImpP.Add(elTras);
            }

            if (elImpP.HasElements) el.Add(elImpP);
        }

        return el;
    }

    private XElement BuildDoctoRelacionado(JObject d)
    {
        var el = new XElement(NsPago + "DoctoRelacionado");

        AddAttr(el, "IdDocumento", d.Value<string>("IdDocumento") ?? "");
        AddAttrIfPresent(el, "Serie", d.Value<string>("Serie"));
        AddAttrIfPresent(el, "Folio", d.Value<string>("Folio"));
        AddAttr(el, "MonedaDR", d.Value<string>("MonedaDR") ?? "MXN");

        var monedaDR = d.Value<string>("MonedaDR") ?? "MXN";
        if (!string.Equals(monedaDR, "MXN", StringComparison.OrdinalIgnoreCase))
            AddAttrIfPresent(el, "EquivalenciaDR", FmtDecimal(d.Value<string>("EquivalenciaDR"), 10));

        AddAttr(el, "NumParcialidad", d.Value<string>("NumParcialidad") ?? "1");
        AddAttr(el, "ImpSaldoAnt",    FmtMonto(d.Value<string>("ImpSaldoAnt")));
        AddAttr(el, "ImpPagado",      FmtMonto(d.Value<string>("ImpPagado")));
        AddAttr(el, "ImpSaldoInsoluto", FmtMonto(d.Value<string>("ImpSaldoInsoluto")));
        AddAttr(el, "ObjetoImpDR", d.Value<string>("ObjetoImpDR") ?? "01");

        // ImpuestosDR
        if (d["ImpuestosDR"] is JObject impDR && impDR.HasValues)
        {
            var elImpDR = new XElement(NsPago + "ImpuestosDR");

            if (impDR["RetencionesDR"] is JArray retDR && retDR.Count > 0)
            {
                var elRets = new XElement(NsPago + "RetencionesDR");
                foreach (var r in retDR.OfType<JObject>())
                {
                    var elR = new XElement(NsPago + "RetencionDR");
                    AddAttr(elR, "BaseDR",       FmtMonto(r.Value<string>("BaseDR")));
                    AddAttr(elR, "ImpuestoDR",   r.Value<string>("ImpuestoDR") ?? "");
                    AddAttr(elR, "TipoFactorDR", r.Value<string>("TipoFactorDR") ?? "Tasa");
                    AddAttr(elR, "TasaOCuotaDR", FmtDecimal(r.Value<string>("TasaOCuotaDR"), 6));
                    AddAttr(elR, "ImporteDR",    FmtMonto(r.Value<string>("ImporteDR")));
                    elRets.Add(elR);
                }
                if (elRets.HasElements) elImpDR.Add(elRets);
            }

            // El mapper expone "TrasladoDR" (singular), pero el SAT pide "TrasladosDR" -> "TrasladoDR"
            var trasArr = impDR["TrasladoDR"] as JArray
                       ?? impDR["TrasladosDR"] as JArray;
            if (trasArr != null && trasArr.Count > 0)
            {
                var elTras = new XElement(NsPago + "TrasladosDR");
                foreach (var t in trasArr.OfType<JObject>())
                {
                    var elT = new XElement(NsPago + "TrasladoDR");
                    AddAttr(elT, "BaseDR",       FmtMonto(t.Value<string>("BaseDR")));
                    AddAttr(elT, "ImpuestoDR",   t.Value<string>("ImpuestoDR") ?? "");
                    AddAttr(elT, "TipoFactorDR", t.Value<string>("TipoFactorDR") ?? "Tasa");
                    var tipoFactor = t.Value<string>("TipoFactorDR") ?? "Tasa";
                    if (!string.Equals(tipoFactor, "Exento", StringComparison.OrdinalIgnoreCase))
                    {
                        AddAttr(elT, "TasaOCuotaDR", FmtDecimal(t.Value<string>("TasaOCuotaDR"), 6));
                        AddAttr(elT, "ImporteDR",    FmtMonto(t.Value<string>("ImporteDR")));
                    }
                    elTras.Add(elT);
                }
                if (elTras.HasElements) elImpDR.Add(elTras);
            }

            if (elImpDR.HasElements) el.Add(elImpDR);
        }

        return el;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Helpers de certificado
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// El SAT espera NoCertificado como exactamente 20 dígitos. Algunos certs en BD
    /// quedaron guardados con el SerialNumber en hexadecimal (cada par hex = un byte
    /// ASCII de un dígito). Esta función detecta ese caso y lo decodifica.
    /// </summary>
    private static string NormalizarNoCertificado(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var s = raw.Trim();

        // Caso A — ya viene en formato correcto: 20 dígitos
        if (System.Text.RegularExpressions.Regex.IsMatch(s, @"^\d{20}$"))
            return s;

        // Caso B — viene como hex de bytes ASCII (longitud par y solo hex)
        if (s.Length >= 40 && s.Length % 2 == 0 &&
            System.Text.RegularExpressions.Regex.IsMatch(s, @"^[0-9A-Fa-f]+$"))
        {
            try
            {
                var bytes = new byte[s.Length / 2];
                for (int i = 0; i < bytes.Length; i++)
                    bytes[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);

                var ascii = System.Text.Encoding.ASCII.GetString(bytes);
                var soloDigitos = new string(ascii.Where(char.IsDigit).ToArray());

                if (soloDigitos.Length == 20) return soloDigitos;
                if (soloDigitos.Length > 20) return soloDigitos[^20..];
                if (soloDigitos.Length > 0)  return soloDigitos.PadLeft(20, '0');
            }
            catch { /* cae al fallback */ }
        }

        // Caso C — algo inesperado: devolver tal cual (que truene el PAC con info)
        return s;
    }

    private static string ResolveCerPemPath(Certificado cert)
    {
        // Preferir cer_pem_path si está configurado
        if (!string.IsNullOrWhiteSpace(cert.CerPemPath) && File.Exists(cert.CerPemPath))
            return cert.CerPemPath!;

        // Convención del proyecto: junto al .cer existe el .cer.pem
        if (!string.IsNullOrWhiteSpace(cert.CerPath))
        {
            var sibling = cert.CerPath + ".pem"; // /path/cert.cer  ->  /path/cert.cer.pem
            if (File.Exists(sibling)) return sibling;

            // Otro intento: reemplazando extensión
            var dir = Path.GetDirectoryName(cert.CerPath);
            var fn  = Path.GetFileNameWithoutExtension(cert.CerPath);
            if (!string.IsNullOrWhiteSpace(dir) && !string.IsNullOrWhiteSpace(fn))
            {
                var alt = Path.Combine(dir!, fn + ".pem");
                if (File.Exists(alt)) return alt;
            }
        }

        throw new InvalidOperationException(
            "No se encontró el archivo .cer.pem del certificado. " +
            "Configura cer_pem_path o asegúrate que exista <cer_path>.pem.");
    }

    /// <summary>
    /// Lee el .cer.pem y devuelve la cadena base64 SIN headers BEGIN/END ni saltos de línea.
    /// </summary>
    private static string LeerCertificadoEnBase64(string pemPath)
    {
        var contenido = File.ReadAllText(pemPath);

        // Si ya viene en PEM (con headers)
        if (contenido.Contains("BEGIN CERTIFICATE", StringComparison.OrdinalIgnoreCase))
        {
            var sb = new StringBuilder(contenido.Length);
            foreach (var line in contenido.Split('\n'))
            {
                var t = line.Trim();
                if (string.IsNullOrEmpty(t)) continue;
                if (t.StartsWith("-----")) continue;
                sb.Append(t);
            }
            return sb.ToString();
        }

        // Si el archivo es base64 puro (sin headers) lo devolvemos limpio
        return new string(contenido.Where(ch => !char.IsWhiteSpace(ch)).ToArray());
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Helpers de fecha / formato
    // ─────────────────────────────────────────────────────────────────────────
    private static string ResolverFecha(string? fechaIni)
    {
        // Si "AUTO" o vacío, generamos en hora local del servidor
        if (string.IsNullOrWhiteSpace(fechaIni) ||
            string.Equals(fechaIni, "AUTO", StringComparison.OrdinalIgnoreCase))
        {
            return DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", Inv);
        }

        // Si ya viene con formato ISO, respetar
        if (DateTime.TryParse(fechaIni, Inv, DateTimeStyles.None, out var dt))
            return dt.ToString("yyyy-MM-ddTHH:mm:ss", Inv);

        return fechaIni!;
    }

    private static string FmtMonto(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "0.00";
        if (decimal.TryParse(raw, NumberStyles.Any, Inv, out var d))
            return Math.Round(d, 2, MidpointRounding.AwayFromZero).ToString("F2", Inv);
        return raw!;
    }

    private static string FmtDecimal(string? raw, int decimales)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "0";
        if (decimal.TryParse(raw, NumberStyles.Any, Inv, out var d))
            return Math.Round(d, decimales, MidpointRounding.AwayFromZero)
                       .ToString("F" + decimales, Inv);
        return raw!;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Helpers XML
    // ─────────────────────────────────────────────────────────────────────────
    private static void AddAttr(XElement el, string name, string value)
        => el.Add(new XAttribute(name, value ?? ""));

    private static void AddAttrIfPresent(XElement el, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            el.Add(new XAttribute(name, value!));
    }

    private static void CopyAttrsFromObject(XElement el, JObject o, params string[] keys)
    {
        foreach (var k in keys)
        {
            var v = o.Value<string>(k);
            if (!string.IsNullOrWhiteSpace(v))
                el.Add(new XAttribute(k, v!));
        }
    }

    private static void CopyMontoAttr(XElement el, JObject o, string keyJson, string? attrNameOverride = null)
    {
        var v = o.Value<string>(keyJson);
        if (string.IsNullOrWhiteSpace(v)) return;
        var attrName = attrNameOverride ?? keyJson;
        if (el.Attribute(attrName) != null) return; // no pisar
        el.Add(new XAttribute(attrName, FmtMonto(v)));
    }

    private static string SerializeUtf8(XDocument doc)
    {
        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = false,
            Encoding = new UTF8Encoding(false),
            Indent = false
        };

        using var ms = new MemoryStream();
        using (var xw = XmlWriter.Create(ms, settings))
            doc.Save(xw);

        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
