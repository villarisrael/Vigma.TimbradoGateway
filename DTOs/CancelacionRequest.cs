using System.ComponentModel.DataAnnotations;

namespace Vigma.TimbradoGateway.DTOs;

/// <summary>
/// Body del POST /v1/cancelar
/// </summary>
public sealed class CancelacionRequest
{
    /// <summary>RFC del emisor del CFDI a cancelar</summary>
    [Required]
    [MaxLength(13)]
    public string RfcEmisor { get; set; } = "";

    /// <summary>UUID del CFDI a cancelar (formato xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx)</summary>
    [Required]
    [MaxLength(36)]
    public string Uuid { get; set; } = "";

    /// <summary>
    /// Motivo de cancelación SAT:
    ///   01 = Comprobante emitido con errores con relación
    ///   02 = Comprobante emitido con errores sin relación
    ///   03 = No se llevó a cabo la operación
    ///   04 = Operación nominativa relacionada en una factura global
    /// </summary>
    [Required]
    [MaxLength(2)]
    public string Motivo { get; set; } = "02";

    /// <summary>UUID del CFDI sustituto (solo requerido cuando Motivo = "01")</summary>
    [MaxLength(36)]
    public string? UuidSustitucion { get; set; }
}
