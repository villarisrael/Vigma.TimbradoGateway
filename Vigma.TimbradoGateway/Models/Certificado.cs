namespace Vigma.TimbradoGateway.Models;

public class Certificado
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string RFC { get; set; } = "";

    public string? TipoCarga { get; set; }

    public string? CerPath { get; set; }
    public string? KeyPath { get; set; }
    public string? PfxPath { get; set; }

    public string? CerPemPath { get; set; }
    public string? KeyPemPath { get; set; }

    public string? KeyPasswordEnc { get; set; }

    public string? NoCertificado { get; set; }
    public DateTime? VigenciaInicio { get; set; }
    public DateTime? VigenciaFin { get; set; }

    public bool Activo { get; set; } = true;

    public DateTime CreadoUtc { get; set; }          // DATETIME NOT NULL normalmente
    public DateTime? ActualizadoUtc { get; set; }    // DATETIME NULL

    public string? ErrorLast { get; set; }
}
