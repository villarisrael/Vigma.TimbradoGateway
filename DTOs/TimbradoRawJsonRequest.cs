namespace Vigma.TimbradoGateway.DTOs;

/// <summary>
/// Request para el endpoint POST /v1/timbrar/json.
/// El cliente manda el JSON ya construido; el gateway solo inyecta
/// las credenciales PAC y el certificado del tenant antes de enviarlo a MF.
/// </summary>
public class TimbradoRawJsonRequest
{
    /// <summary>
    /// JSON completo listo para timbrar (estructura MultiFacturas).
    /// El gateway sobreescribirá PAC.usuario, PAC.pass, PAC.produccion,
    /// conf.cer, conf.key y conf.pass con los valores del tenant.
    /// </summary>
    public string json { get; set; } = "";
}
