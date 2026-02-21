using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TimbradoGateway.Contracts.Mf
{
    /// <summary>
    /// Modelo robusto del JSON que tú generarás desde INI (y mandarás al microservicio de envío).
    /// Incluye Factura (I/E) + Pagos 2.0 (P) + variantes reales vistas en tus ejemplos.
    /// </summary>
    public sealed class MfRequest
    {
        [JsonPropertyName("version_cfdi")]
        public string? VersionCfdi { get; set; } = "4.0";

        [JsonPropertyName("validacion_local")]
        public string? ValidacionLocal { get; set; } = "NO";

        /// <summary>
        /// En Pagos 2.0 algunas plantillas envían: "complemento": "pagos20"
        /// </summary>
        [JsonPropertyName("complemento")]
        public string? Complemento { get; set; }

        [JsonPropertyName("PAC")]
        public MfPac? PAC { get; set; }

        [JsonPropertyName("conf")]
        public MfConf? Conf { get; set; }  // en tu flujo se sustituye por tenant_id

        [JsonPropertyName("factura")]
        public MfFactura? Factura { get; set; }

        [JsonPropertyName("emisor")]
        public MfEmisor? Emisor { get; set; }

        [JsonPropertyName("receptor")]
        public MfReceptor? Receptor { get; set; }

        /// <summary>
        /// Solo aplica para público en general (XAXX010101000) y CFDI global.
        /// </summary>
        [JsonPropertyName("InformacionGlobal")]
        public MfInformacionGlobal? InformacionGlobal { get; set; }

        /// <summary>
        /// Relacionados por grupos (cada item = un TipoRelacion con muchos UUID).
        /// </summary>
        [JsonPropertyName("CfdisRelacionados")]
        public List<MfCfdisRelacionadosGroup>? CfdisRelacionados { get; set; }

        [JsonPropertyName("conceptos")]
        public List<MfConcepto>? Conceptos { get; set; }

        [JsonPropertyName("impuestos")]
        public MfImpuestos? Impuestos { get; set; }

        /// <summary>
        /// Complemento Pagos 2.0 (si TipoComprobante=P)
        /// </summary>
        [JsonPropertyName("pagos20")]
        public MfPagos20? Pagos20 { get; set; }

        /// <summary>
        /// Bolsa de respaldo para campos que aparezcan y aún no tipemos.
        /// Útil para no perder data al convertir INI→JSON.
        /// </summary>
        [JsonExtensionData]
        public Dictionary<string, object>? Extra { get; set; }
    }

    // ----------------------------
    // PAC / conf
    // ----------------------------
    public sealed class MfPac
    {
        [JsonPropertyName("usuario")]
        public string? Usuario { get; set; }

        [JsonPropertyName("pass")]
        public string? Pass { get; set; }

        [JsonPropertyName("produccion")]
        public string? Produccion { get; set; } = "NO";
    }

    public sealed class MfConf
    {
        [JsonPropertyName("cer")]
        public string? Cer { get; set; }

        [JsonPropertyName("key")]
        public string? Key { get; set; }

        [JsonPropertyName("pass")]
        public string? Pass { get; set; }
    }

    // ----------------------------
    // factura/emisor/receptor
    // ----------------------------
    public sealed class MfFactura
    {
        [JsonPropertyName("condicionesDePago")]
        public string? CondicionesDePago { get; set; }

        [JsonPropertyName("descuento")]
        public string? Descuento { get; set; }  // a veces viene string "0.00"

        [JsonPropertyName("fecha_expedicion")]
        public string? FechaExpedicion { get; set; } // "AUTO" o fecha ISO

        [JsonPropertyName("serie")]
        public string? Serie { get; set; }

        [JsonPropertyName("folio")]
        public string? Folio { get; set; }

        [JsonPropertyName("forma_pago")]
        public string? FormaPago { get; set; }

        [JsonPropertyName("metodo_pago")]
        public string? MetodoPago { get; set; }

        [JsonPropertyName("moneda")]
        public string? Moneda { get; set; }

        [JsonPropertyName("tipocambio")]
        public object? TipoCambio { get; set; } // puede venir int, decimal o string

        [JsonPropertyName("tipocomprobante")]
        public string? TipoComprobante { get; set; } // I/E/P/T

        [JsonPropertyName("LugarExpedicion")]
        public string? LugarExpedicion { get; set; }

        [JsonPropertyName("Exportacion")]
        public string? Exportacion { get; set; }

        [JsonPropertyName("subtotal")]
        public object? SubTotal { get; set; } // puede ser 298 o "0"

        [JsonPropertyName("total")]
        public object? Total { get; set; } // puede ser 345.68 o "0"

        [JsonExtensionData]
        public Dictionary<string, object>? Extra { get; set; }
    }

    public sealed class MfEmisor
    {
        [JsonPropertyName("rfc")]
        public string? Rfc { get; set; }

        [JsonPropertyName("nombre")]
        public string? Nombre { get; set; }

        [JsonPropertyName("RegimenFiscal")]
        public string? RegimenFiscal { get; set; }

        [JsonExtensionData]
        public Dictionary<string, object>? Extra { get; set; }
    }

    public sealed class MfReceptor
    {
        [JsonPropertyName("rfc")]
        public string? Rfc { get; set; }

        [JsonPropertyName("nombre")]
        public string? Nombre { get; set; }

        [JsonPropertyName("UsoCFDI")]
        public string? UsoCfdi { get; set; }

        [JsonPropertyName("DomicilioFiscalReceptor")]
        public string? DomicilioFiscalReceptor { get; set; }

        /// <summary>
        /// En tu ejemplo venía duplicado y uno con espacio al final.
        /// Tipamos el correcto + capturamos el “con espacio” vía JsonExtensionData.
        /// </summary>
        [JsonPropertyName("RegimenFiscalReceptor")]
        public string? RegimenFiscalReceptor { get; set; }

        [JsonExtensionData]
        public Dictionary<string, object>? Extra { get; set; }
    }

    // ----------------------------
    // InformacionGlobal (CFDI global)
    // ----------------------------
    public sealed class MfInformacionGlobal
    {
        [JsonPropertyName("Periodicidad")]
        public string? Periodicidad { get; set; } // 01/02/03...

        [JsonPropertyName("Meses")]
        public string? Meses { get; set; } // "01".."12" o varios según reglas

        [JsonPropertyName("Año")]
        public string? Anio { get; set; } // ojo: viene con ñ en JSON real

        [JsonExtensionData]
        public Dictionary<string, object>? Extra { get; set; }
    }

    // ----------------------------
    // CfdisRelacionados (lista de grupos)
    // ----------------------------
    public sealed class MfCfdisRelacionadosGroup
    {
        [JsonPropertyName("TipoRelacion")]
        public string? TipoRelacion { get; set; }

        [JsonPropertyName("UUID")]
        public List<string>? UUID { get; set; }

        [JsonExtensionData]
        public Dictionary<string, object>? Extra { get; set; }
    }

    // ----------------------------
    // Conceptos
    // ----------------------------
    public sealed class MfConcepto
    {
        [JsonPropertyName("cantidad")]
        public object? Cantidad { get; set; } // a veces número, a veces string

        [JsonPropertyName("unidad")]
        public string? Unidad { get; set; }

        [JsonPropertyName("ID")]
        public string? ID { get; set; }

        [JsonPropertyName("descripcion")]
        public string? Descripcion { get; set; }

        [JsonPropertyName("ValorUnitario")]
        public object? ValorUnitario { get; set; }

        [JsonPropertyName("importe")]
        public object? Importe { get; set; }

        [JsonPropertyName("ClaveProdServ")]
        public string? ClaveProdServ { get; set; }

        [JsonPropertyName("ClaveUnidad")]
        public string? ClaveUnidad { get; set; }

        [JsonPropertyName("ObjetoImp")]
        public string? ObjetoImp { get; set; }

        [JsonPropertyName("Impuestos")]
        public MfConceptoImpuestos? Impuestos { get; set; }

        [JsonExtensionData]
        public Dictionary<string, object>? Extra { get; set; }
    }

    public sealed class MfConceptoImpuestos
    {
        [JsonPropertyName("Traslados")]
        public List<MfTrasladoDetalle>? Traslados { get; set; }

        [JsonPropertyName("Retenciones")]
        public List<MfRetencionDetalle>? Retenciones { get; set; }

        [JsonExtensionData]
        public Dictionary<string, object>? Extra { get; set; }
    }

    public sealed class MfTrasladoDetalle
    {
        [JsonPropertyName("Base")]
        public object? Base { get; set; }

        [JsonPropertyName("Impuesto")]
        public string? Impuesto { get; set; }

        [JsonPropertyName("TipoFactor")]
        public string? TipoFactor { get; set; }

        [JsonPropertyName("TasaOCuota")]
        public object? TasaOCuota { get; set; }

        [JsonPropertyName("Importe")]
        public object? Importe { get; set; }

        [JsonExtensionData]
        public Dictionary<string, object>? Extra { get; set; }
    }

    public sealed class MfRetencionDetalle
    {
        [JsonPropertyName("Base")]
        public object? Base { get; set; }

        [JsonPropertyName("Impuesto")]
        public string? Impuesto { get; set; }

        [JsonPropertyName("TipoFactor")]
        public string? TipoFactor { get; set; }

        [JsonPropertyName("TasaOCuota")]
        public object? TasaOCuota { get; set; }

        [JsonPropertyName("Importe")]
        public object? Importe { get; set; }

        [JsonExtensionData]
        public Dictionary<string, object>? Extra { get; set; }
    }

    // ----------------------------
    // Impuestos globales (ojo: "translados" y fields minúscula)
    // ----------------------------
    public sealed class MfImpuestos
    {
        [JsonPropertyName("TotalImpuestosTrasladados")]
        public object? TotalImpuestosTrasladados { get; set; }

        //[JsonPropertyName("TotalImpuestosRetenidos")]
        //public object? TotalImpuestosRetenidos { get; set; }

        /// <summary>
        /// En tus ejemplos viene como "translados" (con n).
        /// Lo tipamos así para no perder el payload.
        /// </summary>
        [JsonPropertyName("translados")]
        public List<MfTrasladoGlobal>? translados { get; set; }
         
        /// <summary>Si alguna plantilla lo manda bien como "Traslados".</summary>
        /// //usa translados para diferenciar entre el concepto y el resumen
        //[JsonPropertyName("Translados")]
        //public List<MfTrasladoGlobal>? Translados { get; set; }

        [JsonPropertyName("retenciones")]
        public List<MfRetencionGlobal>? Retenciones { get; set; }

        [JsonExtensionData]
        public Dictionary<string, object>? Extra { get; set; }
    }

    public sealed class MfTrasladoGlobal
    {
        [JsonPropertyName("Base")]
        public object? Base { get; set; }

        [JsonPropertyName("impuesto")]
        public string? ImpuestoLower { get; set; }

        [JsonPropertyName("Impuesto")]
        public string? Impuesto { get; set; }

        [JsonPropertyName("tasa")]
        public object? TasaLower { get; set; }

        [JsonPropertyName("TasaOCuota")]
        public object? TasaOCuota { get; set; }

        [JsonPropertyName("importe")]
        public object? ImporteLower { get; set; }

        [JsonPropertyName("Importe")]
        public object? Importe { get; set; }

        [JsonPropertyName("TipoFactor")]
        public string? TipoFactor { get; set; }

        [JsonExtensionData]
        public Dictionary<string, object>? Extra { get; set; }
    }

    public sealed class MfRetencionGlobal
    {
        [JsonPropertyName("Impuesto")]
        public string? Impuesto { get; set; }

        [JsonPropertyName("Importe")]
        public object? Importe { get; set; }

        [JsonExtensionData]
        public Dictionary<string, object>? Extra { get; set; }
    }

    // ----------------------------
    // Pagos 2.0
    // ----------------------------
    public sealed class MfPagos20
    {
        [JsonPropertyName("Pagos")]
        public List<MfPago20Pago>? Pagos { get; set; }

        [JsonPropertyName("Totales")]
        public MfPago20Totales? Totales { get; set; }

        [JsonExtensionData]
        public Dictionary<string, object>? Extra { get; set; }
    }

    public sealed class MfPago20Totales
    {
        [JsonPropertyName("MontoTotalPagos")]
        public object? MontoTotalPagos { get; set; }

        [JsonExtensionData]
        public Dictionary<string, object>? Extra { get; set; }
    }

    public sealed class MfPago20Pago
    {
        [JsonPropertyName("FechaPago")]
        public string? FechaPago { get; set; }

        [JsonPropertyName("FormaDePagoP")]
        public string? FormaDePagoP { get; set; }

        [JsonPropertyName("MonedaP")]
        public string? MonedaP { get; set; }

        [JsonPropertyName("TipoCambioP")]
        public object? TipoCambioP { get; set; }

        [JsonPropertyName("Monto")]
        public object? Monto { get; set; }

        [JsonPropertyName("NomBancoOrdExt")]
        public object? NomBancoOrdExt { get; set; }

        /// <summary>En tu JSON viene como "DoctoRelacionado" (singular) pero es array.</summary>
        [JsonPropertyName("DoctoRelacionado")]
        public List<MfPago20DoctoRelacionado>? DoctoRelacionado { get; set; }

        [JsonExtensionData]
        public Dictionary<string, object>? Extra { get; set; }
    }

    public sealed class MfPago20DoctoRelacionado
    {
        [JsonPropertyName("IdDocumento")]
        public string? IdDocumento { get; set; }

        [JsonPropertyName("Serie")]
        public string? Serie { get; set; }

        [JsonPropertyName("Folio")]
        public string? Folio { get; set; }

        [JsonPropertyName("MonedaDR")]
        public string? MonedaDR { get; set; }

        [JsonPropertyName("NumParcialidad")]
        public object? NumParcialidad { get; set; }

        [JsonPropertyName("ImpSaldoAnt")]
        public object? ImpSaldoAnt { get; set; }

        [JsonPropertyName("ImpPagado")]
        public object? ImpPagado { get; set; }

        [JsonPropertyName("ImpSaldoInsoluto")]
        public object? ImpSaldoInsoluto { get; set; }

        [JsonPropertyName("EquivalenciaDR")]
        public object? EquivalenciaDR { get; set; }

        [JsonPropertyName("ObjetoImpDR")]
        public string? ObjetoImpDR { get; set; }

        [JsonExtensionData]
        public Dictionary<string, object>? Extra { get; set; }
    }
}
