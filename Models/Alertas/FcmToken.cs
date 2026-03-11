using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Vigma.TimbradoGateway.Models.Alertas;

[Table("fcm_tokens")]
public class FcmToken
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("tenant_id")]
    public int TenantId { get; set; }

    [Column("entidad_id")]
    [MaxLength(100)]
    public string EntidadId { get; set; } = "";

    [Column("entidad_nombre")]
    [MaxLength(200)]
    public string? EntidadNombre { get; set; }

    [Column("token")]
    [MaxLength(500)]
    public string Token { get; set; } = "";

    [Column("activo")]
    public bool Activo { get; set; } = true;

    [Column("creado_utc")]
    public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;

    [Column("actualizado_utc")]
    public DateTime ActualizadoUtc { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(TenantId))]
    public Tenant? Tenant { get; set; }
}
