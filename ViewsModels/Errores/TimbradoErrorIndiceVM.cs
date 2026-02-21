using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using Vigma.TimbradoGateway.ViewsModels.Errores;

namespace Vigma.TimbradoGateway.ViewModels.Errores
{
    public class TimbradoErrorIndiceVM
    {
        // filtros
        public int? TenantId { get; set; }
        public string? RfcEmisor { get; set; }
        public DateTime? FechaInicio { get; set; }
        public DateTime? FechaFinal { get; set; }

        // combos / resultados
        public List<SelectListItem> Tenants { get; set; } = new();
        public List<TimbradoErrorLogRowVM> Rows { get; set; } = new();
    }
}
