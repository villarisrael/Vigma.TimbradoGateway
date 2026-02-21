using System;
using System.Globalization;
using System.Text.Json;

namespace Vigma.TimbradoGateway.Utils;

public sealed class MfApiMeta
{
    public int? CodigoMfNumero { get; set; }
    public string? CodigoMfTexto { get; set; }

    public string? Pac { get; set; }
    public string? Servidor { get; set; }
    public decimal? Ejecucion { get; set; }
    public bool? Abortar { get; set; }

    public decimal? Saldo { get; set; }
    public bool? Cancelada { get; set; }

    // Opcionales (si vienen)
    public string? Uuid { get; set; }
}

public static class MfApiMetaParser
{
    public static MfApiMeta ParseMeta(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new MfApiMeta { CodigoMfNumero = null, CodigoMfTexto = "Respuesta vacía del PAC/MF." };

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            var meta = new MfApiMeta
            {
                CodigoMfNumero = GetInt(root, "codigo_mf_numero") ?? GetInt(root, "codigo"),
                CodigoMfTexto = GetString(root, "codigo_mf_texto") ?? GetString(root, "mensaje") ?? GetString(root, "error"),
                Ejecucion = GetDecimal(root, "ejecucion"),
                Servidor = GetString(root, "servidor"),
                Pac = GetString(root, "PAC") ?? GetString(root, "pac"),
                Abortar = GetBool(root, "abortar"),
                Cancelada = GetBool(root, "cancelada"),
                Saldo = GetDecimal(root, "saldo"),

                // A veces viene como "uuid" o anidado dependiendo del endpoint
                Uuid = GetString(root, "uuid")
            };

            return meta;
        }
        catch (JsonException)
        {
            // Si no es JSON, lo tratamos como texto “raro”
            return new MfApiMeta
            {
                CodigoMfNumero = null,
                CodigoMfTexto = "Respuesta no-JSON: " + Truncate(raw, 500)
            };
        }
    }

    private static string? GetString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var p)) return null;
        if (p.ValueKind == JsonValueKind.String) return p.GetString();
        return p.ToString();
    }

    private static int? GetInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var p)) return null;

        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var i)) return i;
        if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out var s)) return s;
        return null;
    }

    private static bool? GetBool(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var p)) return null;

        if (p.ValueKind == JsonValueKind.True) return true;
        if (p.ValueKind == JsonValueKind.False) return false;

        if (p.ValueKind == JsonValueKind.String)
        {
            var v = (p.GetString() ?? "").Trim();
            if (bool.TryParse(v, out var b)) return b;
            if (string.Equals(v, "SI", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(v, "NO", StringComparison.OrdinalIgnoreCase)) return false;
            if (v == "1") return true;
            if (v == "0") return false;
        }

        if (p.ValueKind == JsonValueKind.Number)
        {
            if (p.TryGetInt32(out var n)) return n != 0;
        }

        return null;
    }

    private static decimal? GetDecimal(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var p)) return null;

        if (p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out var d)) return d;

        if (p.ValueKind == JsonValueKind.String)
        {
            var s = (p.GetString() ?? "").Trim();
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var di)) return di;
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out var dc)) return dc;
        }

        return null;
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s.Substring(0, max) + "...";
}
