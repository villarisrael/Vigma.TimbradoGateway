using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace Vigma.TimbradoGateway.Util;

public static class CertificadoReader
{
    /// <summary>
    /// Lee un certificado .cer y extrae información relevante
    /// </summary>
    public static CertificadoInfo? LeerCertificado(string cerPath)
    {
        if (string.IsNullOrWhiteSpace(cerPath) || !File.Exists(cerPath))
            return null;

        try
        {
            // Leer el archivo como bytes
            var certBytes = File.ReadAllBytes(cerPath);

            // Crear el certificado X509
            var cert = new X509Certificate2(certBytes);

            return new CertificadoInfo
            {
                NoCertificado = cert.SerialNumber,
                VigenciaInicio = cert.NotBefore.ToUniversalTime(),
                VigenciaFin = cert.NotAfter.ToUniversalTime(),
                Subject = cert.Subject,
                Issuer = cert.Issuer,
                RFC = ExtraerRFC(cert.Subject),
                RazonSocial = ExtraerRazonSocial(cert.Subject),
                EsValido = cert.NotBefore <= DateTime.UtcNow && cert.NotAfter >= DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            // Log el error si tienes un logger
            Console.WriteLine($"Error al leer certificado {cerPath}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Extrae el RFC del Subject del certificado
    /// El RFC suele estar en el campo x500UniqueIdentifier (OID 2.5.4.45)
    /// </summary>
    private static string ExtraerRFC(string subject)
    {
        try
        {
        
             // Buscar en el formato: 2.5.4.45=RFC / CURP
            var matchOID1 = Regex.Match(subject, @"x500UniqueIdentifier=([A-Z&Ñ]{3,4}\d{6}[A-Z0-9]{3})", RegexOptions.IgnoreCase);
            if (matchOID1.Success)
                return matchOID1.Groups[1].Value;

            var matchOID = Regex.Match(subject, @"2\.5\.4\.45=([A-Z&Ñ]{3,4}\d{6}[A-Z0-9]{3})", RegexOptions.IgnoreCase);
            if (matchOID.Success)
                return matchOID.Groups[1].Value;

            // Buscar patrón de RFC en cualquier parte del subject
            var matchRFC = Regex.Match(subject, @"([A-Z&Ñ]{3,4}\d{6}[A-Z0-9]{3})", RegexOptions.IgnoreCase);
            if (matchRFC.Success)
                return matchRFC.Groups[1].Value;

            return string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Extrae la Razón Social del Subject del certificado
    /// Busca en CN (Common Name) o en el campo name (2.5.4.41)
    /// </summary>
    private static string ExtraerRazonSocial(string subject)
    {
        try
        {
            // Buscar en CN=
            var matchCN = Regex.Match(subject, @"CN=([^,]+)");
            if (matchCN.Success)
                return matchCN.Groups[1].Value.Trim();

            // Buscar en 2.5.4.41= (name attribute)
            var matchName = Regex.Match(subject, @"2\.5\.4\.41=([^,]+)");
            if (matchName.Success)
                return matchName.Groups[1].Value.Trim();

            return string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}

public class CertificadoInfo
{
    public string NoCertificado { get; set; } = "";
    public DateTime? VigenciaInicio { get; set; }
    public DateTime? VigenciaFin { get; set; }
    public string Subject { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string RFC { get; set; } = "";
    public string RazonSocial { get; set; } = "";
    public bool EsValido { get; set; }
}
