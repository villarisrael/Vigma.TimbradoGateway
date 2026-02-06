using Microsoft.AspNetCore.Routing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using TimbradoGateway.Infrastructure.Ini;
using TimbradoGateway.Services;
using Vigma.TimbradoGateway.DTOs;
using Vigma.TimbradoGateway.Infrastructure;
using Vigma.TimbradoGateway.Models;
using Vigma.TimbradoGateway.Utils;
using static System.Reflection.Metadata.BlobBuilder;



namespace Vigma.TimbradoGateway.Services;


public interface ITimbradoService
{
    Task<TimbradoResponse> TimbrarDesdeIniAsync(string apiKey, string ini, CancellationToken ct = default); // SOAP actual
    Task<TimbradoResponse> TimbrarDesdeIniJsonAsync(string apiKey, string ini, CancellationToken ct = default); // NUEVO REST /api
}





public sealed class TimbradoService : ITimbradoService
{
    private readonly ITenantConfigService _tenantCfg;
    private readonly CryptoService _crypto;
    private readonly IMultiFacturasClient _mf;


    private readonly IMultiFacturasApiClient _mfApi;
    private readonly IniToMfRequestMapper _mapper;
    private readonly IIniBuilderService _iniBuilder;

  
    private readonly IIniParserService _iniParser;

    private readonly ITimbradoLogService _logs;

    public TimbradoService(
    ITenantConfigService tenantCfg,
    CryptoService crypto,
    IMultiFacturasClient mf,
    IIniBuilderService iniBuilder,
    IIniParserService iniParser,
    IniToMfRequestMapper mapper,
    IMultiFacturasApiClient mfApi,
    ITimbradoLogService logs)
    {
        _tenantCfg = tenantCfg;
        _crypto = crypto;
        _mf = mf;

        _iniBuilder = iniBuilder;
        _iniParser = iniParser;
        _mapper = mapper;
        _mfApi = mfApi;
        _logs = logs;
    }


    public async Task<TimbradoResponse> TimbrarDesdeIniAsync(string apiKey, string ini, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new UnauthorizedAccessException("Falta API Key (X-Api-Key).");
        if (string.IsNullOrWhiteSpace(ini))
            throw new ArgumentException("INI requerido.");

        var ini2 = NormalizeIniForGateway(ini);
        ini2 = UpsertIniKeyRoot(ini2, "MODOINI", "INI");


        // 2) RFC emisor
        var rfcEmisor = ExtractIniValue(ini2, "emisor", "rfc");
        if (string.IsNullOrWhiteSpace(rfcEmisor))
            throw new ArgumentException("No se encontró [emisor] rfc= en el INI.");

        // 3) Resolver tenant + cert
        var (tenant, cert) = await _tenantCfg.GetByApiKeyAsync(apiKey, rfcEmisor);

        // 4) credenciales PAC (forzar)
        var pacPass = string.IsNullOrWhiteSpace(tenant.PacPasswordEnc)
            ? ""
            : _crypto.DecryptFromBase64(tenant.PacPasswordEnc);

        ini2 = UpsertIniKeyInSection(ini2, "PAC", "usuario", tenant.PacUsuario ?? "");
        ini2 = UpsertIniKeyInSection(ini2, "PAC", "pass", pacPass);
        ini2 = UpsertIniKeyInSection(ini2, "PAC", "produccion", tenant.PacProduccion ? "SI" : "NO");

        // 5) Sustituir [conf] cer/key/pass (siempre)
        // OJO: aquí uso .CerPath/.KeyPath/.KeyPassEnc como ejemplo: AJUSTA a tu modelo real
        if (string.IsNullOrWhiteSpace(cert.CerPath) || string.IsNullOrWhiteSpace(cert.KeyPath))
            throw new ArgumentException("El certificado no tiene cer_path/key_path configurado.");

        Console.WriteLine($"[CERT] RFC={cert.RFC} CER={cert.CerPath} KEY={cert.KeyPath}");


        var cerBytes = await File.ReadAllBytesAsync(cert.CerPath, ct);
        var keyBytes = await File.ReadAllBytesAsync(cert.KeyPath, ct);

        var cerB64 = Convert.ToBase64String(cerBytes);
        var keyB64 = Convert.ToBase64String(keyBytes);
        string keyPass = "";

        if (!string.IsNullOrWhiteSpace(cert.KeyPasswordEnc))
        {
            var s = cert.KeyPasswordEnc.Trim();

            try
            {
                // Si estaba cifrado en base64 por tu CryptoService
                keyPass = _crypto.DecryptFromBase64(s);
            }
            catch
            {
                // Si estaba en texto plano (como ZH20051998), úsalo tal cual
                keyPass = s;
            }
        }

        ini2 = UpsertIniKeyInSection(ini2, "conf", "cer", cerB64);
        ini2 = UpsertIniKeyInSection(ini2, "conf", "key", keyB64);
        ini2 = UpsertIniKeyInSection(ini2, "conf", "pass", keyPass);

        ini2 = ini2.Replace("\n\n", "\n");
        ini2 = ini2.Replace("\n\n", "\n");

        Console.WriteLine($"[ini={ini2} ");
        // 6) Enviar al WS via MultiFacturasClient (SOAP timbrarini1)
        var raw = await _mf.TimbrarIniAsync(ini2, rfcEmisor, ct);

        // 7) Parse básico respuesta (uuid/xml/código)
        var parsed = MultiFacturasResponseParser.Parse(raw);


     

        return new TimbradoResponse
        {
            ok = parsed.ok,
            codigo = parsed.codigo,
            mensaje = parsed.mensaje,
            uuid = parsed.uuid,
            xmlTimbrado = parsed.xmlTimbrado,

            // Si el PAC respondió ok=false, NO es "error interno"; es rechazo del PAC.
            // Entonces error = null, y mandas mensaje/codigo.
            error = ini2,

            // rawPac solo para debug o cuando ok=false
            rawPac = parsed.ok ? null : raw,

            logId = 0
        };

    }

    // ---------------- Helpers ----------------

    private static string RemoveLinesStartingWith(string ini, string prefix)
    {
        var lines = NormalizeNewlines(ini).Split('\n');
        var sb = new StringBuilder();

        foreach (var raw in lines)
        {
            var t = raw.TrimStart();
            if (t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            sb.AppendLine(raw);
        }

        return sb.ToString().TrimEnd('\r', '\n');
    }

    private static string? ExtractIniValue(string ini, string section, string key)
    {
        var text = NormalizeNewlines(ini);
        var lines = text.Split('\n');
        var header = $"[{section}]";
        var inSection = false;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";") || line.StartsWith("#"))
                continue;

            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                inSection = string.Equals(line, header, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inSection) continue;

            var idx = line.IndexOf('=');
            if (idx <= 0) continue;

            var k = line.Substring(0, idx).Trim();
            if (!string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) continue;

            return line[(idx + 1)..].Trim();
        }

        return null;
    }

    private static string UpsertIniKeyInSection(string ini, string section, string key, string value)
    {
        var text = NormalizeNewlines(ini);
        var lines = text.Split('\n').ToList();

        var secHeader = $"[{section}]";
        int secStart = lines.FindIndex(l => string.Equals(l.Trim(), secHeader, StringComparison.OrdinalIgnoreCase));

        if (secStart < 0)
        {
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                lines.Add("");
            lines.Add(secHeader);
            lines.Add($"{key}={value}");
            return string.Join("\n", lines);
        }

        int secEnd = lines.FindIndex(secStart + 1, l =>
        {
            var t = l.Trim();
            return t.StartsWith("[") && t.EndsWith("]");
        });
        if (secEnd < 0) secEnd = lines.Count;

        for (int i = secStart + 1; i < secEnd; i++)
        {
            var t = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(t) || t.StartsWith(";") || t.StartsWith("#")) continue;

            var idx = t.IndexOf('=');
            if (idx <= 0) continue;

            var k = t.Substring(0, idx).Trim();
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = $"{key}={value}";
                return string.Join("\n", lines);
            }
        }

        lines.Insert(secEnd, $"{key}={value}");
        return string.Join("\n", lines);
    }

    private static string NormalizeNewlines(string s) => s.Replace("\r\n", "\n").Replace("\r", "\n");

    private static string NormalizeIniForGateway(string ini)
    {
       
        var text = NormalizeNewlines(ini);

        // A) quitar cfdi= y xml_debug= SIEMPRE
        text = RemoveLinesStartingWith(text, "cfdi=");
        text = RemoveLinesStartingWith(text, "xml_debug=");

        // B) quitar líneas duplicadas exactas consecutivas o repetidas (por pegado doble)
          text = RemoveDuplicateLines(text);

        // C) quitar bloques de sección duplicados (mismo header repetido)
        text = RemoveDuplicateSectionsKeepFirst(text);

        return text.Trim();
    }
    private static string RemoveDuplicateLines(string ini)
    {
        var lines = NormalizeNewlines(ini).Split('\n');
        var seen = new HashSet<string>(StringComparer.Ordinal); // exacto
        var sb = new StringBuilder();

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd(); // normaliza espacios al final
            if (string.IsNullOrWhiteSpace(line))
            {
                sb.AppendLine();
                continue;
            }

            // Si la misma línea ya apareció, la brincamos (esto elimina el "ini pegado 2 veces")
            if (!seen.Add(line))
                continue;

            sb.AppendLine(line);
        }

        return sb.ToString().TrimEnd('\r', '\n');
    }

    private static string RemoveDuplicateSectionsKeepFirst(string ini)
    {
        var lines = NormalizeNewlines(ini).Split('\n');
        var sb = new StringBuilder();

        var seenHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool skipping = false;

        foreach (var raw in lines)
        {
            var t = raw.Trim();

            if (t.StartsWith("[") && t.EndsWith("]"))
            {
                // si ya vimos este header, saltamos todo el bloque hasta el siguiente header
                if (!seenHeaders.Add(t))
                {
                    skipping = true;
                    continue;
                }

                skipping = false;
            }

            if (!skipping)
                sb.AppendLine(raw);
        }

        return sb.ToString().TrimEnd('\r', '\n');
    }

    private static string UpsertIniKeyRoot(string ini, string key, string value)
    {
        var lines = NormalizeNewlines(ini).Split('\n').ToList();
        var keyEq = key + "=";

        // 1) Buscar si ya existe en root (antes de la primera sección)
        for (int i = 0; i < lines.Count; i++)
        {
            var t = lines[i].Trim();

            // Si llegamos a una sección y no lo encontramos, dejamos de buscar
            if (t.StartsWith("[") && t.EndsWith("]"))
                break;

            if (t.StartsWith(keyEq, StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = $"{key}={value}";
                return string.Join("\n", lines);
            }
        }

        // 2) Insertarlo antes de la primera sección (o al final si no hay secciones)
        int insertAt = lines.FindIndex(l =>
        {
            var t = l.Trim();
            return t.StartsWith("[") && t.EndsWith("]");
        });

        if (insertAt < 0)
            insertAt = lines.Count;

        lines.Insert(insertAt, $"{key}={value}");

        return string.Join("\n", lines);
    }

    public async Task<TimbradoResponse> TimbrarDesdeIniJsonAsync(
    string apiKey,
    string ini,
    CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new UnauthorizedAccessException("Falta API Key (X-Api-key).");

        if (string.IsNullOrWhiteSpace(ini))
            throw new ArgumentException("INI requerido.");

        // 1) Normaliza INI
        var ini2 = NormalizeIniForGateway(ini);
        ini2 = ini2.Replace("\n\n", "\n").Replace("\n\n", "\n");

        // 2) RFC emisor
        var rfcEmisor = ExtractIniValue(ini2, "emisor", "rfc");
        var serie = ExtractIniValue(ini2, "factura", "Serie");
        var folio = ExtractIniValue(ini2, "factura", "Folio");
        var TipoDeComprobante = ExtractIniValue(ini2, "factura", "tipocomprobante");

        if (string.IsNullOrWhiteSpace(rfcEmisor))
            throw new ArgumentException("No se encontró [emisor] rfc= en el INI.");

        // 3) Resolver tenant + cert
        var (tenant, cert) = await _tenantCfg.GetByApiKeyAsync(apiKey, rfcEmisor);

        // 4) Parse tipado INI
        var doc = _iniParser.Parse(ini2);

        // 5) Mapper => JSON MF
        var jsonMf = await _mapper.MapToJsonAsync(doc, tenant, cert);

        // 6) Enviar a MF API
        var raw = await _mfApi.TimbrarJsonAsync(jsonMf, ct);

        // 7) Parse COMPLETO para respuesta (meta + xml + uuid + rawPac)
        var parsed = MfApiResponseParser.Parse(raw);

        var meta = parsed.Meta;

        // 8) Regla éxito
        var ok = meta.CodigoMfNumero == 0;

        // 9) Tipo
        const string tipo = "ini-json";

        // 10) Log (según tu regla)
        if (ok)
        {
            try
            {
                await _logs.LogOkAsync(
                    tenantId: tenant.Id,
                    rfcEmisor: rfcEmisor,
                    meta: meta,
                    uuid: parsed.Uuid ?? meta.Uuid,
                    tipo: tipo, xmltimbrado: parsed.XmlTimbrado, serie: serie, folio: folio, TipoDeComprobante: TipoDeComprobante,
                    ct: ct
                );
            }
            catch (Exception ex)
            {

            }
        }
        else
        {
            await _logs.LogErrorAsync(
                tenantId: tenant.Id,
                rfcEmisor: rfcEmisor,
                meta: meta,
                jsonEnviado: jsonMf,                 // solo en errores
                tipo: tipo,
                detalleInterno: meta.CodigoMfTexto,  // opcional
                ct: ct
            );
        }

        // 11) Respuesta hacia tu API (AQUÍ va lo importante)
        return new TimbradoResponse
        {
            ok = ok,

            codigo = meta.CodigoMfNumero?.ToString() ?? meta.CodigoMfTexto,
            mensaje = meta.CodigoMfTexto,

            uuid = parsed.Uuid ?? meta.Uuid,

            
            xmlTimbrado = parsed.XmlTimbrado,

           
            rawPac = parsed.RawPac,

            error = ok ? null : meta.CodigoMfTexto,

            // si más adelante quieres devolver el id del log, podemos hacer que LogOkAsync/LogErrorAsync lo regresen
            logId = 0
        };
    }



}