namespace Vigma.TimbradoGateway.DTOs
{
    public sealed class SaldoTimbresResponse
    {
        public bool Ok { get; set; }
        public string? Codigo { get; set; }
        public string? Mensaje { get; set; }
        public int Saldo { get; set; }
        public string? XmlCrudo { get; set; }
    }

}
