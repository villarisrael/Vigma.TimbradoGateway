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

    private static DateTime? TryParseOpenSslDate(string s)
    {
        // Ej: "May 18 11:43:51 2023 GMT"
        if (DateTime.TryParse(s.Replace("GMT", "").Trim(), out var dt))
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        return null;
    }
}
