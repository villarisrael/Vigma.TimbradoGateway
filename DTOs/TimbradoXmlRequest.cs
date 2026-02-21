namespace Vigma.TimbradoGateway.DTOs;

public class TimbradoXmlRequest
{
    public string xml { get; set; } = "";
    public bool sellarLocal { get; set; } = false; // por ahora C1 recomendado: false
}
