namespace Vigma.TimbradoGateway.DTOs;

/// <summary>
/// Body del endpoint POST /v1/estado.
/// </summary>
public class ConsultaEstadoSatRequest
{
    /// <summary>UUID del CFDI a consultar (Folio Fiscal).</summary>
    public string? Uuid { get; set; }

    /// <summary>RFC del Emisor del CFDI.</summary>
    public string? RfcEmisor { get; set; }

    /// <summary>RFC del Receptor del CFDI.</summary>
    public string? RfcReceptor { get; set; }

    /// <summary>Total del CFDI tal cual se emitió (string para respetar el formato exacto).</summary>
    public string? Total { get; set; }
}
