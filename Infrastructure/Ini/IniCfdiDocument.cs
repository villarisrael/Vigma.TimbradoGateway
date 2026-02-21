using System;
using System.Collections.Generic;

namespace TimbradoGateway.Infrastructure.Ini
{
    /// <summary>
    /// Documento CFDI leído desde INI. Puede ser:
    /// - Factura/Nota/Traslado (I/E/T)
    /// - Pago (P) con Complemento Pagos 2.0
    ///
    /// Regla de tu proyecto: Conf (certificados) SIEMPRE se sustituye por tenant_id.
    /// </summary>
    public sealed class IniCfdiDocument
    {
        public string VersionCfdi { get; set; } = "4.0";
        public string ValidacionLocal { get; set; } = "NO";

        public IniPac Pac { get; set; } = new();
        public IniConf Conf { get; set; } = new(); // se sustituye por tenant_id
        public IniFactura Factura { get; set; } = new();
        public IniEmisor Emisor { get; set; } = new();
        public IniReceptor Receptor { get; set; } = new();

        /// <summary>
        /// Conceptos para I/E/T.
        /// En P (Pago) normalmente solo hay un concepto estándar "Pago" (ClaveProdServ 84111506).
        /// </summary>
        public List<IniConcepto> Conceptos { get; set; } = new();

        public IniImpuestosGlobal Impuestos { get; set; } = new();

        public IniCfdiRelacionados? CfdiRelacionados { get; set; }

        /// <summary>
        /// Complementos tipados (Pagos 2.0) y bolsas flexibles para otros.
        /// </summary>
        public IniComplementos Complementos { get; set; } = new();

        /// <summary>
        /// Bolsa para conservar cualquier nodo adicional que venga del INI y no esté tipado.
        /// key recomendado: "seccion.clave" o "conceptos.0.xxx"
        /// </summary>
        public Dictionary<string, string> Raw { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    // ===========================
    // PAC / Conf
    // ===========================
    public sealed class IniPac
    {
        public string? Usuario { get; set; }
        public string? Pass { get; set; }
        public string Produccion { get; set; } = "NO"; // SI/NO
    }

    public sealed class IniConf
    {
        public string? Cer { get; set; }  // se ignora en tu flujo
        public string? Key { get; set; }  // se ignora en tu flujo
        public string? Pass { get; set; } // se ignora en tu flujo
    }

    // ===========================
    // Factura (Comprobante)
    // ===========================
    public sealed class IniFactura
    {
        // Basicos CFDI 4.0
        public string FechaExpedicion { get; set; } = "AUTO";
        public string? Serie { get; set; }
        public string? Folio { get; set; }

        public string Moneda { get; set; } = "MXN";
        public decimal? TipoCambio { get; set; } = 1m;

        public string? FormaPago { get; set; }      // 01, 99, etc. (en P suele ser 99)
        public string? MetodoPago { get; set; }     // PUE/PPD (en P suele NO aplicar o PPD según plantilla)
        public string? LugarExpedicion { get; set; }

        /// <summary>I, E, T, P</summary>
        public string? TipoComprobante { get; set; }

        public string? Exportacion { get; set; } = "01";

        public decimal? SubTotal { get; set; }
        public decimal? Descuento { get; set; }
        public decimal? Total { get; set; }

        public string? CondicionesDePago { get; set; }
        public string? Observaciones { get; set; }
        public string? Confirmacion { get; set; }

        /// <summary>Campos poco comunes, se guardan aquí si vienen (o en Raw).</summary>
        public Dictionary<string, string> Extra { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class IniEmisor
    {
        public string? Rfc { get; set; }
        public string? Nombre { get; set; }
        public string? RegimenFiscal { get; set; }
        public Dictionary<string, string> Extra { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class IniReceptor
    {
        public string? Rfc { get; set; }
        public string? Nombre { get; set; }
        public string? UsoCfdi { get; set; }
        public string? DomicilioFiscalReceptor { get; set; }
        public string? RegimenFiscalReceptor { get; set; }

        // Para extranjeros (opcionales)
        //public string? ResidenciaFiscal { get; set; }
        //public string? NumRegIdTrib { get; set; }

        public Dictionary<string, string> Extra { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class IniCfdiRelacionados
    {
        public string? TipoRelacion { get; set; }
        public List<string> Uuids { get; set; } = new();
    }

    // ===========================
    // Conceptos / Impuestos (I/E/T)
    // ===========================
    public sealed class IniConcepto
    {
        public decimal? Cantidad { get; set; }
        public string? Unidad { get; set; }
        public string? ClaveUnidad { get; set; }

        public string? Id { get; set; } // ID=...
      //  public string? NoIdentificacion { get; set; }
        public string? Descripcion { get; set; }

        public decimal? ValorUnitario { get; set; }
        public decimal? Importe { get; set; }
        public decimal? Descuento { get; set; }

        public string? ClaveProdServ { get; set; }
        public string? ObjetoImp { get; set; } // 01/02/03/04

        public Dictionary<string, string> Extra { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public IniImpuestosConcepto Impuestos { get; set; } = new();
    }

    public sealed class IniImpuestosConcepto
    {
        public List<IniTraslado> Traslados { get; set; } = new();
        public List<IniRetencion> Retenciones { get; set; } = new();
    }

    public sealed class IniTraslado
    {
        public decimal? Base { get; set; }
        public string? Impuesto { get; set; }    // 002, etc.
        public string? TipoFactor { get; set; }  // Tasa/Cuota/Exento
        public string? TasaOCuota { get; set; }
        public decimal? Importe { get; set; }
    }

    public sealed class IniRetencion
    {
        public decimal? Base { get; set; }
        public string? Impuesto { get; set; }    // 001/002/003
        public string? TipoFactor { get; set; }  // normalmente Tasa
        public string? TasaOCuota { get; set; }
        public decimal? Importe { get; set; }
    }

    public sealed class IniImpuestosGlobal
    {
        public decimal? TotalImpuestosTrasladados { get; set; }
     //   public decimal? TotalImpuestosRetenidos { get; set; }

        public List<IniTrasladoGlobal> Translados { get; set; } = new();
        public List<IniRetencionGlobal> Retenciones { get; set; } = new();

        public Dictionary<string, string> Extra { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class IniTrasladoGlobal
    {
        public decimal? Base { get; set; }
        public string? impuesto { get; set; }
        public string? tasa { get; set; }
        public decimal? importe { get; set; }
        public string? TipoFactor { get; set; }
    }

    public sealed class IniRetencionGlobal
    {
        public string? Impuesto { get; set; }
        public decimal? Importe { get; set; }
    }

    // ===========================
    // Complementos
    // ===========================
    public sealed class IniComplementos
    {
        /// <summary>
        /// Complemento Pagos 2.0 (si TipoComprobante = P).
        /// Si no aplica, queda null.
        /// </summary>
        public IniPagos20? Pagos20 { get; set; }

        /// <summary>
        /// Otros complementos aún no tipados:
        /// key: nombre (ej "CartaPorte31", "Nomina12")
        /// value: pares clave/valor
        /// </summary>
        public Dictionary<string, Dictionary<string, string>> Otros { get; set; }
            = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Grupos repetibles de complementos no tipados.
        /// </summary>
        public Dictionary<string, List<Dictionary<string, string>>> OtrosGrupos { get; set; }
            = new(StringComparer.OrdinalIgnoreCase);
    }

    // ===========================
    // Complemento Pagos 2.0 (robusto)
    // ===========================
    public sealed class IniPagos20
    {
        /// <summary>
        /// En Pagos 2.0 existen Totales (opcionales según plantilla).
        /// </summary>
        public IniPagos20Totales Totales { get; set; } = new();

        /// <summary>
        /// 1..N pagos
        /// </summary>
        public List<IniPago20Pago> Pagos { get; set; } = new();

        /// <summary>
        /// Conserva nodos extra que no tipamos.
        /// </summary>
        public Dictionary<string, string> Extra { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class IniPagos20Totales
    {
        // Estos nombres pueden variar; por eso los hacemos opcionales y conservamos Extra.
        public decimal? MontoTotalPagos { get; set; }

        // IVA
        public decimal? TotalTrasladosBaseIVA16 { get; set; }
        public decimal? TotalTrasladosImpuestoIVA16 { get; set; }

        public decimal? TotalTrasladosBaseIVA08 { get; set; }
        public decimal? TotalTrasladosImpuestoIVA08 { get; set; }

        public decimal? TotalTrasladosBaseIVA00 { get; set; }
        public decimal? TotalTrasladosImpuestoIVA00 { get; set; }

        // ISR / IEPS (si aplica)
        public decimal? TotalRetencionesISR { get; set; }
        public decimal? TotalRetencionesIVA { get; set; }
        public decimal? TotalRetencionesIEPS { get; set; }

        public Dictionary<string, string> Extra { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class IniPago20Pago
    {
        // Pago
        public string? FechaPago { get; set; }         // ISO: 2026-01-01T12:00:00
        public string? FormaDePagoP { get; set; }      // 01, 03, etc.
        public string? MonedaP { get; set; }           // MXN, USD
        public decimal? TipoCambioP { get; set; }      // si MonedaP != MXN
        public decimal? Monto { get; set; }

        public string? NumOperacion { get; set; }

        // Cuentas (opcionales)
        public string? RfcEmisorCtaOrd { get; set; }
        public string? NomBancoOrdExt { get; set; }
        public string? CtaOrdenante { get; set; }

        public string? RfcEmisorCtaBen { get; set; }
        public string? CtaBeneficiario { get; set; }

        // Cadena pago (opcionales)
        public string? TipoCadPago { get; set; }
        public string? CertPago { get; set; }
        public string? CadPago { get; set; }
        public string? SelloPago { get; set; }

        // Doctos relacionados
        public List<IniPago20DoctoRelacionado> DoctosRelacionados { get; set; } = new();

        // Impuestos del pago (a nivel pago)
        public IniPago20ImpuestosP ImpuestosP { get; set; } = new();

        public Dictionary<string, string> Extra { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class IniPago20DoctoRelacionado
    {
        // Documento relacionado
        public string? IdDocumento { get; set; }      // UUID
        public string? Serie { get; set; }
        public string? Folio { get; set; }
        public string? MonedaDR { get; set; }
        public decimal? EquivalenciaDR { get; set; }
        public int? NumParcialidad { get; set; }

        public decimal? ImpSaldoAnt { get; set; }
        public decimal? ImpPagado { get; set; }
        public decimal? ImpSaldoInsoluto { get; set; }

        public string? ObjetoImpDR { get; set; } // 01/02/03/04

        // Impuestos por DR
        public IniPago20ImpuestosDR ImpuestosDR { get; set; } = new();

        public Dictionary<string, string> Extra { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class IniPago20ImpuestosP
    {
        public List<IniPago20TrasladoP> TrasladosP { get; set; } = new();
        public List<IniPago20RetencionP> RetencionesP { get; set; } = new();
        public Dictionary<string, string> Extra { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class IniPago20TrasladoP
    {
        public decimal? BaseP { get; set; }
        public string? ImpuestoP { get; set; }     // 002, etc.
        public string? TipoFactorP { get; set; }   // Tasa
        public decimal? TasaOCuotaP { get; set; }
        public decimal? ImporteP { get; set; }
    }

    public sealed class IniPago20RetencionP
    {
        public string? ImpuestoP { get; set; } // 001/002/003
        public decimal? ImporteP { get; set; }
    }

    public sealed class IniPago20ImpuestosDR
    {
        public List<IniPago20TrasladoDR> TrasladosDR { get; set; } = new();
        public List<IniPago20RetencionDR> RetencionesDR { get; set; } = new();
        public Dictionary<string, string> Extra { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class IniPago20TrasladoDR
    {
        public decimal? BaseDR { get; set; }
        public string? ImpuestoDR { get; set; }
        public string? TipoFactorDR { get; set; }
        public decimal? TasaOCuotaDR { get; set; }
        public decimal? ImporteDR { get; set; }
    }

    public sealed class IniPago20RetencionDR
    {
        public string? ImpuestoDR { get; set; }
        public decimal? ImporteDR { get; set; }
    }
}
