namespace Vigma.TimbradoGateway.DTOs;

public class TimbradoJsonRequest
{
    public string version_cfdi { get; set; } = "4.0";
    public string validacion_local { get; set; } = "NO";

    public PacDto PAC { get; set; } = new();
    public FacturaDto factura { get; set; } = new();
    public EmisorDto emisor { get; set; } = new();
    public ReceptorDto receptor { get; set; } = new();

    public List<ConceptoDto> conceptos { get; set; } = new();
    public ImpuestosDto? impuestos { get; set; }

    // Opcional: permitir conf en request, pero NO confiar en rutas de cliente
    public ConfDto? conf { get; set; }
}

public class PacDto
{
    public string usuario { get; set; } = "";
    public string pass { get; set; } = "";
    public string produccion { get; set; } = "NO"; // SI/NO
}

public class FacturaDto
{
    public string? condicionesDePago { get; set; }
    public string fecha_expedicion { get; set; } = "AUTO";
    public string? folio { get; set; }
    public string? forma_pago { get; set; }
    public string? LugarExpedicion { get; set; }
    public string? metodo_pago { get; set; }
    public string? moneda { get; set; }
    public string? serie { get; set; }
    public decimal subtotal { get; set; }
    public decimal tipocambio { get; set; } = 1;
    public string? tipocomprobante { get; set; }
    public decimal total { get; set; }
    public string? Exportacion { get; set; }
}

public class EmisorDto
{
    public string rfc { get; set; } = "";
    public string nombre { get; set; } = "";
    public string RegimenFiscal { get; set; } = "";
}

public class ReceptorDto
{
    public string rfc { get; set; } = "";
    public string nombre { get; set; } = "";
    public string UsoCFDI { get; set; } = "";
    public string DomicilioFiscalReceptor { get; set; } = "";
    public string RegimenFiscalReceptor { get; set; } = "";
}

public class ConceptoDto
{
    public decimal cantidad { get; set; }
    public string? unidad { get; set; }
    public string? ID { get; set; }
    public string descripcion { get; set; } = "";
    public decimal valorunitario { get; set; }
    public decimal importe { get; set; }
    public string? ClaveProdServ { get; set; }
    public string? ClaveUnidad { get; set; }
    public string? ObjetoImp { get; set; }
    public ImpuestosConceptoDto? Impuestos { get; set; }
}

public class ImpuestosConceptoDto
{
    public List<TrasladoDto>? Traslados { get; set; }
}

public class TrasladoDto
{
    public decimal Base { get; set; }
    public string Impuesto { get; set; } = "002";
    public string TipoFactor { get; set; } = "Tasa";
    public string TasaOCuota { get; set; } = "0.160000";
    public decimal Importe { get; set; }
}

public class ImpuestosDto
{
    public decimal TotalImpuestosTrasladados { get; set; }
  
    public List<TrasladoResumenDto>? translados { get; set; }
}

public class TrasladoResumenDto
{
    public decimal Base { get; set; }
    public string impuesto { get; set; } = "002";
    public string tasa { get; set; } = "0.160000";
    public decimal Importe { get; set; }
    public string TipoFactor { get; set; } = "Tasa";
}

public class ConfDto
{
    public string? cer { get; set; }
    public string? key { get; set; }
    public string? pass { get; set; }
}
