using System.Security.Cryptography.X509Certificates;
using System.Text;
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
                // El SerialNumber de .NET regresa hex. El SAT codifica el NoCertificado
                // como bytes ASCII dentro del serial, así que decodificamos hex → ASCII.
                NoCertificado = DecodificarNoCertificadoSat(cert.SerialNumber),
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
    /// Convierte el SerialNumber hexadecimal de un X509Certificate2 en el NoCertificado
    /// de 20 dígitos que espera el SAT. El SAT codifica los 20 dígitos como bytes ASCII
    /// dentro del serial del certificado, por eso X509Certificate2.SerialNumber regresa
    /// algo como "3030303031..." (hex de '0','0','0','0','1',...).
    ///
    /// Estrategia:
    /// 1. Decodifica hex → bytes
    /// 2. Interpreta bytes como ASCII
    /// 3. Filtra solo dígitos
    /// 4. Si quedan 20 → listo
    /// 5. Si quedan más de 20 → toma los últimos 20 (descarta bytes de signo/padding)
    /// 6. Si algo falla → devuelve el serial original (mejor algo que nada)
    /// </summary>
    public static string DecodificarNoCertificadoSat(string? serialHex)
    {
        if (string.IsNullOrWhiteSpace(serialHex)) return "";

        var hex = serialHex.Trim().Replace(" ", "").Replace("-", "").Replace(":", "");

        // Si la longitud no es par o tiene caracteres no-hex, devolvemos tal cual
        if (hex.Length % 2 != 0) return serialHex;
        if (!Regex.IsMatch(hex, @"^[0-9A-Fa-f]+$")) return serialHex;

        try
        {
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);

            var ascii = Encoding.ASCII.GetString(bytes);

            // Quedarnos solo con dígitos por si hay padding o bytes raros
            var soloDigitos = new string(ascii.Where(char.IsDigit).ToArray());

            if (soloDigitos.Length == 20) return soloDigitos;

            // Si vienen más de 20 dígitos (bytes de signo, etc.), tomar últimos 20
            if (soloDigitos.Length > 20) return soloDigitos[^20..];

            // Si vienen menos, padear a la izquierda con ceros
            if (soloDigitos.Length > 0) return soloDigitos.PadLeft(20, '0');

            // Si no son ASCII numéricos, no es un CSD del SAT — devolver original
            return serialHex;
        }
        catch
        {
            return serialHex;
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
