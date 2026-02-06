using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;

namespace Vigma.TimbradoGateway.ViewModels.Timbrados
{
    public class TimbradoIndiceVM
    {
        public long? TenantId { get; set; }
        public string? RfcEmisor { get; set; }
        public DateTime? FechaInicio { get; set; }
        public DateTime? FechaFinal { get; set; } 

        public List<SelectListItem> Tenants { get; set; } = new();
        public List<TimbradoLogRowVM> Rows { get; set; } = new();
        public int CanceladasCount { get; set; }

    }

    public class TimbradoLogRowVM
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

        public bool Cancelada { get; set; }
        public decimal? Saldo { get; set; }
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
    }
}
