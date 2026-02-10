using System;

namespace Vigma.TimbradoGateway.ViewsModels.Errores
{
    public class TimbradoErrorLogRowVM
    {
        public long Id { get; set; }
        public int TenantId { get; set; }
        public string RfcEmisor { get; set; } = "";
        public int? CodigoMfNumero { get; set; }
        public string? CodigoMfTexto { get; set; }
        public string? Jsonenviado { get; set; }
        public string? JsonFormateado { get; set; }
        public bool EsJsonValido { get; set; }
        public DateTime CreadoUtc { get; set; }
        public string? Adicionales { get; set; }
    }
}