namespace Vigma.TimbradoGateway.Utils
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    public static class FileErrorLogger
    {
        private static readonly SemaphoreSlim _lock = new(1, 1);

        public static async Task LogDbErrorAsync(Exception ex, string? extra = null, CancellationToken ct = default)
        {
            try
            {
                var path = GetLogPath("baseerror.txt");

                var sb = new StringBuilder();
                sb.AppendLine("==================================================");
                sb.AppendLine($"UTC: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}");
                sb.AppendLine($"Local: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                if (!string.IsNullOrWhiteSpace(extra))
                {
                    sb.AppendLine("Extra:");
                    sb.AppendLine(extra);
                }

                sb.AppendLine("Exception:");
                sb.AppendLine(FlattenException(ex));

                sb.AppendLine("==================================================");
                sb.AppendLine();

                await _lock.WaitAsync(ct);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await File.AppendAllTextAsync(path, sb.ToString(), Encoding.UTF8, ct);
                }
                finally
                {
                    _lock.Release();
                }
            }
            catch
            {
                // No vuelvas a lanzar: el logger nunca debe tumbar tu flujo.
            }
        }

        private static string GetLogPath(string fileName)
        {
            // Opción A: carpeta de la app (recomendado para servicios/IIS)
            var baseDir = AppContext.BaseDirectory;

            // Puedes cambiarlo a un path fijo si quieres:
            // var baseDir = @"C:\Logs\TimbradoGateway";
            // o en Linux: "/var/log/timbradogateway"

            return Path.Combine(baseDir, "logs", fileName);
        }

        private static string FlattenException(Exception ex)
        {
            var sb = new StringBuilder();
            int level = 0;

            for (var e = ex; e != null; e = e.InnerException)
            {
                sb.AppendLine($"--- Level {level} ---");
                sb.AppendLine($"Type: {e.GetType().FullName}");
                sb.AppendLine($"Message: {e.Message}");
                sb.AppendLine($"HResult: {e.HResult}");
                sb.AppendLine("StackTrace:");
                sb.AppendLine(e.StackTrace);
                sb.AppendLine();
                level++;
            }

            return sb.ToString();
        }
    }

}
