namespace Vigma.TimbradoGateway.Models;

public class Tenant
{
    public int Id { get; set; }
    public string? Nombre { get; set; } = "";
    public string? ApiKeyHash { get; set; } = "";
    public string? ApiKeyEnc { get; set; }
    public string? ApiKeyLast4 { get; set; }
    public DateTime? ApiKeyRotatedUtc { get; set; }

    public DateTime? actualizado_utc { get; set; }

    public DateTime? creado_utc { get; set; }

    public bool Activo { get; set; }

    public string? PacUsuario { get; set; }
    public string? PacPasswordEnc { get; set; }
    public bool PacProduccion { get; set; }

}
