namespace Vigma.TimbradoGateway.DTOs;

/// <summary>
/// Respuesta del endpoint POST /v1/cancelar
/// </summary>
public sealed class CancelacionResponse
{
    public bool Ok { get; set; }
    public string? Codigo { get; set; }
    public string? Mensaje { get; set; }
    public string? Uuid { get; set; }

    /// <summary>Respuesta raw del PAC (solo cuando ok=false, para debug)</summary>
    public string? RawPac { get; set; }

    public long LogId { get; set; }
}
