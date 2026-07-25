using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using Vigma.TimbradoGateway.Controllers;

namespace Vigma.TimbradoGateway.ViewModels.Timbrados
{
    public class TimbradoIndiceVM
    {
        public long? TenantId { get; set; }
        public string? RfcEmisor { get; set; }
        public string? Uuid { get; set; }
        public string? Folio { get; set; }
        public DateTime? FechaInicio { get; set; }
        public DateTime? FechaFinal { get; set; }

        public IEnumerable<SelectListItem> Tenants { get; set; } = Enumerable.Empty<SelectListItem>();
        public List<TimbradoRowVM> Rows { get; set; } = new();

        // Stats opcional
        public int CanceladasCount { get; set; }

        // ✅ Paginación
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
        public int TotalRows { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalRows / PageSize);
        public bool HasPrev => Page > 1;
        public bool HasNext => Page < TotalPages;
    }

    public class TimbradoRowVM
    {
        public long Id { get; set; }

        public long TenantId { get; set; }
        public string? RfcEmisor { get; set; }

        public string? Origen { get; set; }
        public string? TipoDeComprobante { get; set; }

        public string? Serie { get; set; }
        public string? Folio { get; set; }

        public string? Uuid { get; set; }
        public string? MensajeMf { get; set; }

        public bool Cancelada { get; set; }

        public decimal? Saldo { get; set; }

        // Si en DB lo guardas como UTC y lo muestras tal cual:
        public DateTime CreatedUtc { get; set; }
    }

    public class TimbradoDetalleVM
    {
        public long Id { get; set; }
        public long TenantId { get; set; }
        public string RfcEmisor { get; set; } = "";

        public string? Origen { get; set; }
        public string? TipoDeComprobante { get; set; }
        public string? Serie { get; set; }
        public string? Folio { get; set; }

        public string Uuid { get; set; } = "";
        public string? MensajeMf { get; set; }

        public string? XmlTimbrado { get; set; }

        public bool Cancelada { get; set; }
        public decimal? Saldo { get; set; }
        public DateTime CreatedUtc { get; set; }
        public string? JsonFormateado { get; set; }
        public string? Adicionales { get; set; }

        // ── Campos parseados del XML timbrado ────────────────────────────────
        // Comprobante
        public string? Fecha { get; set; }
        public string? FechaTimbrado { get; set; }
        public string? Subtotal { get; set; }
        public string? Total { get; set; }
        public string? FormaPago { get; set; }
        public string? MetodoPago { get; set; }
        public string? Moneda { get; set; }
        public string? TipoCambio { get; set; }
        public string? LugarExpedicion { get; set; }
        public string? NoCertificado { get; set; }
        public string? NoCertificadoSAT { get; set; }
        public string? CondicionesDePago { get; set; }
        // Emisor
        public string? NombreEmisor { get; set; }
        public string? RegimenFiscalEmisor { get; set; }
        // Receptor
        public string? RfcReceptor { get; set; }
        public string? NombreReceptor { get; set; }
        public string? UsoCFDI { get; set; }
        public string? DomicilioFiscalReceptor { get; set; }
        public string? RegimenFiscalReceptor { get; set; }
        // ── Sellos y cadena original ──────────────────────────────────────────
        public string? SelloSAT { get; set; }          // tfd:TimbreFiscalDigital/@SelloSAT
        public string? SelloCFD { get; set; }          // cfdi:Comprobante/@Sello
        public string? Certificado { get; set; }       // cfdi:Comprobante/@Certificado (base64)
        public string? CadenaOriginalTFD { get; set; } // Construida: ||Ver|UUID|Fecha|RfcProv|NoCertSAT||
    }
}
