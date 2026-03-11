using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Vigma.TimbradoGateway.Models.Alertas;

[Table("alert_logs")]
public class AlertLog
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("tenant_id")]
    public int TenantId { get; set; }

    [Column("entidad_id")]
    [MaxLength(100)]
    public string EntidadId { get; set; } = "";

    [Column("entidad_nombre")]
    [MaxLength(200)]
    public string? EntidadNombre { get; set; }

    [Column("origin")]
    [MaxLength(150)]
    public string Origin { get; set; } = "";

    [Column("title")]
    [MaxLength(200)]
    public string Title { get; set; } = "";

    [Column("message")]
    [MaxLength(1000)]
    public string Message { get; set; } = "";

    [Column("fcm_token")]
    [MaxLength(500)]
    public string? FcmToken { get; set; }

    [Column("status")]
    [MaxLength(20)]
    public string Status { get; set; } = "";   // "sent" | "failed"

    [Column("firebase_msg_id")]
    [MaxLength(300)]
    public string? FirebaseMsgId { get; set; }

    [Column("error_detail", TypeName = "TEXT")]
    public string? ErrorDetail { get; set; }

    [Column("sent_at")]
    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(TenantId))]
    public Tenant? Tenant { get; set; }
}
