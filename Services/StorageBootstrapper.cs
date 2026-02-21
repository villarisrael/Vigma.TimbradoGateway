namespace Vigma.TimbradoGateway.Services;

public class StorageBootstrapper
{
    private readonly IConfiguration _cfg;
    private readonly ILogger<StorageBootstrapper> _log;

    public StorageBootstrapper(IConfiguration cfg, ILogger<StorageBootstrapper> log)
    {
        _cfg = cfg;
        _log = log;
    }

    public void EnsureFolders()
    {
        var certBase = _cfg["Timbrado:CertBasePath"] ?? "/opt/timbrado/certs";
        var tmpPath = _cfg["Timbrado:TmpPath"] ?? "/opt/timbrado/tmp";

        EnsureDir(certBase, "CertBasePath");
        EnsureDir(tmpPath, "TmpPath");
    }

    private void EnsureDir(string path, string label)
    {
        try
        {
            Directory.CreateDirectory(path);

            // Prueba de escritura (para detectar permisos)
            var testFile = Path.Combine(path, ".write_test");
            File.WriteAllText(testFile, DateTime.UtcNow.ToString("O"));
            File.Delete(testFile);

            _log.LogInformation("OK carpeta {Label}: {Path}", label, path);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "No se pudo preparar carpeta {Label}: {Path}", label, path);
            throw; // así falla el arranque y lo ves en journalctl
        }
    }
}
