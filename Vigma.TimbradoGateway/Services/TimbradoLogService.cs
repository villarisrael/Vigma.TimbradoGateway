using System;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vigma.TimbradoGateway.Infrastructure;
using Vigma.TimbradoGateway.Models.Logs;
using Vigma.TimbradoGateway.Utils;

namespace Vigma.TimbradoGateway.Services;

public interface ITimbradoLogService
{
    Task LogOkAsync(
        long tenantId,
        string rfcEmisor,
        MfApiMeta meta,
        string? uuid,
        string? tipo,
        string xmltimbrado,
        string? serie,
        string? folio,
        string? tipoDeComprobante,
        IReadOnlyDictionary<string, string>? adicionales = null,
        CancellationToken ct = default);

    Task LogErrorAsync(
        long tenantId,
        string rfcEmisor,
        MfApiMeta meta,
        string jsonEnviado,
        string? tipo,
        string? detalleInterno = null,
        IReadOnlyDictionary<string, string>? adicionales = null,
        CancellationToken ct = default);
}



public sealed class TimbradoLogService : ITimbradoLogService
{
    private readonly TimbradoDbContext _db;
    public TimbradoLogService(TimbradoDbContext db) => _db = db;

    public async Task LogOkAsync(
     long tenantId,
     string? rfcEmisor,
     MfApiMeta? meta,
     string? uuid,
     string? tipo,
     string? xmltimbrado,
     string? serie,
     string? folio,
     string? tipoDeComprobante, IReadOnlyDictionary<string, string>? adicionales = null,
     CancellationToken ct = default)
    {
        // meta puede venir null si algo falla arriba; no queremos que truene el logger
        //meta ??= new MfApiMeta();

        // UUID obligatorio en tu modelo (string no-null)
        // 1) usa uuid argumento
        // 2) si no, usa meta.Uuid
        // 3) si tampoco, deja vacío (o genera un placeholder)
        var uuidFinal = uuid ?? "";

        TimbradoOkLog row = new TimbradoOkLog();
        
          row. TenantId = tenantId;
          row. RfcEmisor = rfcEmisor ?? "";
          
          row. Uuid = uuidFinal;
          
          row. Serie = serie;
          row. Folio = folio;
          
           // si quieres guardar el XML timbrado en OK
          row. xmlTimbrado = string.IsNullOrWhiteSpace(xmltimbrado) ? "" : xmltimbrado;
          
           // OJO: ya no casteamos
          row. Cancelada = meta.Cancelada ?? false;
          row. Abortar = meta.Abortar ?? false;
          
          row. Saldo = meta.Saldo;
          row. Servidor = meta.Servidor;
          row. Ejecucion = meta.Ejecucion;
          row. Pac = meta.Pac;
          
           // si tu meta trae código y texto; mapea aquí (si tu clase MfApiMeta los tiene)
          row. codigo_Mf = meta.CodigoMfNumero?.ToString();
          row. mensaje_Mf = meta.CodigoMfTexto;

        // TipoDeComprobante (CFDI I/E/P/etc.)
          row.TipoDeComprobante = tipoDeComprobante;

           // TipoComprobante (aquí tú lo estás usando como "tipo origen": ini-json)
          row. Origen = tipo;

          row.Adicionales = SerializeAdicionales(adicionales);

            row.created_utc = MexicoNow(); // o DateTime.UtcNow si decides volver a UTC
        try
        {
            _db.TimbradoOkLogs.Add(row);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            await FileErrorLogger.LogDbErrorAsync(
                ex,
                extra: $"TenantId={row.TenantId}, RfcEmisor={row.RfcEmisor}, Uuid={row.Uuid}",
                ct: ct
            );

            // opcional: relanza si quieres que tu API responda error
            
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task LogErrorAsync(long tenantId, string rfcEmisor, MfApiMeta meta, string jsonEnviado, string? tipo, string? detalleInterno = null, IReadOnlyDictionary<string, string>? adicionales = null, CancellationToken ct = default)
    {
        TimbradoErrorLog row = new TimbradoErrorLog();
        row.TenantId = tenantId;

        // string: default "" (vacío)
        row.RfcEmisor = rfcEmisor ?? "";

        // nullable int/string/bool/decimal: si no viene, deja null (o si quieres forzar default, abajo te dejo variante)
        row.CodigoMfNumero = meta?.CodigoMfNumero;      // int?
        row.CodigoMfTexto = meta?.CodigoMfTexto ?? ""; // string? => default ""

        row.Cancelada = meta?.Cancelada;                // bool?
        row.Saldo = meta?.Saldo;                    // decimal?
        row.Servidor = meta?.Servidor ?? "";           // string? => default ""
        row.Ejecucion = meta?.Ejecucion;                // decimal?
        row.Abortar = meta?.Abortar;                  // bool?
        row.Pac = meta?.Pac ?? "";                // string? => default ""

        row.JsonEnviado = jsonEnviado ?? "";         // longtext => default ""
        row.DetalleInterno = detalleInterno ?? "";      // longtext => default ""
        row.Tipo = tipo ?? "";                // "ini" | "ini-json" => default ""

        row.CreadoUtc = MexicoNow();
        row.Adicionales = SerializeAdicionales(adicionales);


        _db.TimbradoErrorLogs.Add(row);
        await _db.SaveChangesAsync(ct);
    }


    private static string? SerializeAdicionales(IReadOnlyDictionary<string, string>? adicionales)
    {
        if (adicionales == null || adicionales.Count == 0) return null;

        // Limpia valores vacíos por si acaso
        var clean = adicionales
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key.Trim(), kv => kv.Value.Trim(), StringComparer.OrdinalIgnoreCase);

        if (clean.Count == 0) return null;

        var opts = new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // por si vienen acentos/UTF-8 sin escapes feos
        };

        return JsonSerializer.Serialize(clean, opts);
    }

    public static DateTime MexicoNow()
    {
        try
        {
            // Linux
            var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        }
        catch
        {
            // Windows fallback
            var tz = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time (Mexico)");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        }
    }


}
