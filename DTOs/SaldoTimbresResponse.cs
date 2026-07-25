namespace Vigma.TimbradoGateway.DTOs
{
    public sealed class SaldoTimbresResponse
    {
        public bool Ok { get; set; }
        public string? Codigo { get; set; }
        public string? Mensaje { get; set; }
        /// <summary>
        /// Timbres disponibles. Null cuando el PAC no devolvió el dato
        /// (ej. &lt;saldo xsi:nil="true"/&gt;) — distinto de un saldo real en 0.
        /// </summary>
        public int? Saldo { get; set; }
        public string? XmlCrudo { get; set; }
    }

}
