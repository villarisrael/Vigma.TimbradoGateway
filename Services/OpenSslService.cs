using System.Diagnostics;

namespace Vigma.TimbradoGateway.Services;

public class OpenSslService
{
    public async Task RunAsync(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "openssl",
            Arguments = args,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        using var p = Process.Start(psi) ?? throw new Exception("No se pudo iniciar OpenSSL.");
        var stderr = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();

        if (p.ExitCode != 0)
            throw new Exception($"OpenSSL falló: {stderr}".Trim());
    }

    public async Task<(DateTime? start, DateTime? end, string? serial)> ReadCertInfoAsync(string cerPemPath)
    {
        // openssl x509 -in file -noout -startdate -enddate -serial
        var psi = new ProcessStartInfo
        {
            FileName = "openssl",
            Arguments = $"x509 -in \"{cerPemPath}\" -noout -startdate -enddate -serial",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var p = Process.Start(psi) ?? throw new Exception("No se pudo leer el certificado con OpenSSL.");
        var output = await p.StandardOutput.ReadToEndAsync();
        var err = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        if (p.ExitCode != 0) throw new Exception(err);

        DateTime? start = null, end = null;
        string? serial = null;

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var l = line.Trim();
            if (l.StartsWith("notBefore="))
                start = TryParseOpenSslDate(l["notBefore=".Length..]);
            else if (l.StartsWith("notAfter="))
                end = TryParseOpenSslDate(l["notAfter=".Length..]);
            else if (l.StartsWith("serial="))
                serial = l["serial=".Length..].Trim();
        }

        return (start, end, serial);
    }

    /// <summary>
    /// Extrae el módulo (clave pública) del certificado DER.
    /// </summary>
    public async Task<string> GetModuloCertAsync(string cerDerPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName  = "openssl",
            Arguments = $"x509 -inform DER -in \"{cerDerPath}\" -noout -modulus",
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute = false
        };
        using var p = Process.Start(psi) ?? throw new Exception("No se pudo iniciar OpenSSL.");
        var output = await p.StandardOutput.ReadToEndAsync();
        var stderr = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        if (p.ExitCode != 0) throw new Exception($"OpenSSL (modulo cer) falló: {stderr}".Trim());
        // Salida: "Modulus=AABB...CC\n"
        return output.Trim().Replace("Modulus=", "", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Extrae el módulo (clave pública) del archivo .key DER cifrado con la contraseña dada.
    /// </summary>
    public async Task<string> GetModuloKeyAsync(string keyDerPath, string password)
    {
        var psi = new ProcessStartInfo
        {
            FileName  = "openssl",
            Arguments = $"rsa -inform DER -in \"{keyDerPath}\" -passin pass:{EscapeShell(password)} -noout -modulus",
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute = false
        };
        using var p = Process.Start(psi) ?? throw new Exception("No se pudo iniciar OpenSSL.");
        var output = await p.StandardOutput.ReadToEndAsync();
        var stderr = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        if (p.ExitCode != 0) throw new Exception($"OpenSSL (modulo key) falló: {stderr}".Trim());
        return output.Trim().Replace("Modulus=", "", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifica que el .cer y el .key sean del mismo par de claves.
    /// Lanza InvalidOperationException si no coinciden.
    /// </summary>
    public async Task VerificarParClavesAsync(string cerDerPath, string keyDerPath, string keyPassword)
    {
        var moduloCer = await GetModuloCertAsync(cerDerPath);
        var moduloKey = await GetModuloKeyAsync(keyDerPath, keyPassword);

        if (!string.Equals(moduloCer, moduloKey, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "El archivo .cer y el .key no son del mismo par de claves SAT. " +
                "Descarga ambos archivos juntos desde el portal del SAT y vuelve a subirlos.");
    }

    /// <summary>Escapa la contraseña para usarla en argumento de consola.</summary>
    private static string EscapeShell(string s) => s.Replace("'", "'\\''");

    private static DateTime? TryParseOpenSslDate(string s)
    {
        // Ej: "May 18 11:43:51 2023 GMT"
        if (DateTime.TryParse(s.Replace("GMT", "").Trim(), out var dt))
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        return null;
    }
}
