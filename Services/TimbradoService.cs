using Microsoft.AspNetCore.Routing;
using Newtonsoft.Json.Linq;
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
using Vigma.TimbradoGateway.Services.Facturalo;
using Vigma.TimbradoGateway.Utils;
using static System.Reflection.Metadata.BlobBuilder;



namespace Vigma.TimbradoGateway.Services;


public interface ITimbradoService
{
    Task<TimbradoResponse> TimbrarDesdeIniAsync(string apiKey, string ini,
         IReadOnlyDictionary<string, string>? adicionales = null,
        CancellationToken ct = default); // SOAP actual

    Task<TimbradoResponse> TimbrarDesdeIniJsonAsync(string apiKey, string ini,
         IReadOnlyDictionary<string, string>? adicionales = null,
        CancellationToken ct = default); // REST /api vía INI

    /// <summary>
    /// El cliente manda el JSON ya construido.
    /// El gateway inyecta PAC + cert del tenant y lo pasa directo a MultiFacturas.
    /// </summary>
    Task<TimbradoResponse> TimbrarDesdeJsonAsync(string apiKey, string json,
        IReadOnlyDictionary<string, string>? adicionales = null,
        CancellationToken ct = default);

    /// <summary>
    /// El cliente manda el XML CFDI sin el atributo Sello.
    /// El gateway lee el keyPEM del tenant y delega el sellado+timbrado a FacturaLO PLUS.
    /// Solo disponible cuando pac_proveedor = 'facturalo'.
    /// </summary>
    Task<TimbradoResponse> TimbrarDesdeXmlAsync(string apiKey, string xmlSinSello,
        IReadOnlyDictionary<string, string>? adicionales = null,
        CancellationToken ct = default);
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
    private readonly IFacturaloClient _facturalo;
    private readonly JsonMfToCfdiXmlBuilder _xmlBuilder;

    public TimbradoService(
        ITenantConfigService tenantCfg,
        CryptoService crypto,
        IMultiFacturasClient mf,
        IIniBuilderService iniBuilder,
        IIniParserService iniParser,
        IniToMfRequestMapper mapper,
        IMultiFacturasApiClient mfApi,
        ITimbradoLogService logs,
        IFacturaloClient facturalo,
        JsonMfToCfdiXmlBuilder xmlBuilder)
    {
        _tenantCfg = tenantCfg;
        _crypto    = crypto;
        _mf        = mf;
        _iniBuilder = iniBuilder;
        _iniParser  = iniParser;
        _mapper     = mapper;
        _mfApi      = mfApi;
        _logs       = logs;
        _facturalo  = facturalo;
        _xmlBuilder = xmlBuilder;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Helper: cuando el tenant está en pac_proveedor='facturalo', tomamos el
    //  JSON estilo MF, lo convertimos a CFDI XML 4.0 sin Sello, y lo enviamos
    //  a FacturaLO PLUS (timbrarConSello).
    //
    //  Reusa tenant + cert YA RESUELTOS para evitar segundo query a BD.
    // ─────────────────────────────────────────────────────────────────────────
    private async Task<TimbradoResponse> TimbrarConFacturaloDesdeJsonMfAsync(
        Tenant tenant,
        Certificado cert,
        JObject jsonMf,
        string tipo,
        string rfcEmisor,
        string? serie,
        string? folio,
        string? tipoDeComprobante,
        IReadOnlyDictionary<string, string>? adicionales,
        CancellationToken ct)
    {
        // 1) Validaciones específicas de FacturaLO
        var apikeyFl = tenant.PacApikeyFacturaloActiva;
        if (string.IsNullOrWhiteSpace(apikeyFl))
            throw new InvalidOperationException(
                $"El tenant no tiene configurada la API Key de FacturaLO PLUS para el ambiente " +
                $"{(tenant.PacProduccion ? "PRODUCCIÓN" : "PRUEBAS")}.");

        // Resolver ruta del key.pem con fallback (para certs antiguos sin la columna seteada)
        var keyPemPath = ResolverKeyPemPath(cert);
        if (string.IsNullOrWhiteSpace(keyPemPath))
            throw new InvalidOperationException(
                "No se pudo localizar el archivo .key.pem del certificado. " +
                "Re-sube el certificado del tenant para regenerar los PEM.");

        if (string.IsNullOrWhiteSpace(cert.NoCertificado))
            throw new InvalidOperationException("El certificado no tiene 'no_certificado'.");

        // 2) Construir CFDI XML 4.0 sin Sello a partir del JSON MF
        string xmlSinSello;
        try
        {
            xmlSinSello = _xmlBuilder.BuildXmlSinSello(jsonMf, cert);
        }
        catch (Exception ex)
        {
            // Loggeamos como error de construcción (no llegamos al PAC)
            try
            {
                var metaBuild = new Vigma.TimbradoGateway.Utils.MfApiMeta
                {
                    Pac = "facturalo",
                    Servidor = tenant.PacProduccion ? "prod" : "dev",
                    CodigoMfTexto = "Error construyendo XML CFDI"
                };
                await _logs.LogErrorAsync(
                    tenantId:       tenant.Id,
                    rfcEmisor:      rfcEmisor,
                    meta:           metaBuild,
                    jsonEnviado:    jsonMf.ToString(Newtonsoft.Json.Formatting.None),
                    tipo:           tipo + "-facturalo",
                    detalleInterno: "Error construyendo XML CFDI: " + ex.Message,
                    adicionales:    adicionales,
                    ct:             ct);
            }
            catch { }
            throw new InvalidOperationException("Error construyendo el CFDI XML para FacturaLO: " + ex.Message, ex);
        }

        // 3) Leer keyPEM del cert (usando la ruta resuelta con fallback)
        var keyPem = await File.ReadAllTextAsync(keyPemPath, ct);

        // 4) Llamar a FacturaLO — timbrarConSello (ellos sellan y timbran)
        var flResp = await _facturalo.TimbrarConSelloAsync(
            apikey:      apikeyFl!,
            xmlSinSello: xmlSinSello,
            keyPem:      keyPem,
            produccion:  tenant.PacProduccion,
            ct:          ct);

        // Éxito si el código es "200" o "0" (cubre ambos PACs: FacturaLO y MultiFacturas)
        var ok = flResp.Code == "200" || flResp.Code == "0";
        var uuidResp = ok ? ExtraerUuidDelXmlTimbrado(flResp.Data) : null;
        var tipoLog = tipo + "-facturalo"; // ej: ini-facturalo, ini-json-facturalo, raw-json-facturalo

        // Normalizamos el mensaje cuando es éxito para que los sistemas cliente
        // y el listado en BD vean siempre "OK" (o "[MODO PRUEBAS] OK" si aplica).
        // En errores conservamos el texto crudo de FacturaLO.
        var mensajeNormalizado = ok
            ? (tenant.PacProduccion ? "OK" : "[MODO PRUEBAS] OK")
            : flResp.Message;

        // Construimos un MfApiMeta con los datos de FacturaLO para que el log
        // tenga llenas las columnas codigo_mf_*, pac, servidor, etc.
        int? codigoNum = int.TryParse(flResp.Code, out var c) ? c : (int?)null;
        var metaFl = new Vigma.TimbradoGateway.Utils.MfApiMeta
        {
            Pac            = "facturalo",
            Servidor       = tenant.PacProduccion ? "prod" : "dev",
            CodigoMfNumero = codigoNum,
            CodigoMfTexto  = mensajeNormalizado,
            Uuid           = uuidResp,
            Cancelada      = false,
            Abortar        = false
        };

        // 5) Logging
        if (ok)
        {
            try
            {
                await _logs.LogOkAsync(
                    tenantId:          tenant.Id,
                    rfcEmisor:         rfcEmisor,
                    meta:              metaFl,
                    uuid:              uuidResp,
                    tipo:              tipoLog,
                    xmltimbrado:       flResp.Data,
                    serie:             serie,
                    folio:             folio,
                    tipoDeComprobante: tipoDeComprobante,
                    adicionales:       adicionales,
                    ct:                ct);
            }
            catch { /* el logger no debe tumbar la respuesta */ }
        }
        else
        {
            try
            {
                // Para diagnóstico, agregamos al detalle_interno la URL REAL que se invocó
                // y los primeros/últimos 4 caracteres de la apikey enviada.
                var apikeyMask = OfuscarApikey(apikeyFl);
                var urlReal = !string.IsNullOrWhiteSpace(flResp.UrlUsada)
                    ? flResp.UrlUsada
                    : (tenant.PacProduccion
                        ? "https://app.facturaloplus.com/ws/servicio.do"
                        : "https://dev.facturaloplus.com/ws/servicio.do");
                var detalle =
                    $"[{flResp.Code}] {flResp.Message} | " +
                    $"ambiente={(tenant.PacProduccion ? "prod" : "dev")} | " +
                    $"url={urlReal} | " +
                    $"apikey={apikeyMask}";

                await _logs.LogErrorAsync(
                    tenantId:       tenant.Id,
                    rfcEmisor:      rfcEmisor,
                    meta:           metaFl,
                    jsonEnviado:    xmlSinSello,
                    tipo:           tipoLog,
                    detalleInterno: detalle,
                    adicionales:    adicionales,
                    ct:             ct);
            }
            catch { }
        }

        // 6) Respuesta al cliente — shape consistente con el flujo MultiFacturas
        //    para que el SDK cliente no distinga entre proveedores.
        //
        // IMPORTANTE: el SDK cliente considera éxito SOLO cuando codigo = "0".
        // FacturaLO devuelve "200" en éxito, así que lo normalizamos a "0" para
        // mantener compatibilidad con SDKs ya desplegados en producción.
        // El código original (200) se conserva en error y rawPac cuando falla.
        var codigoNormalizado = ok ? "0" : flResp.Code;
        var codigoNumNormalizado = ok ? 0 : codigoNum;

        return new TimbradoResponse
        {
            ok                = ok,
            codigo            = codigoNormalizado,
            mensaje           = mensajeNormalizado,
            uuid              = uuidResp,
            xmlTimbrado       = ok ? flResp.Data : null,
            cfdi              = ok ? flResp.Data : null,
            rawPac            = ok ? null : flResp.Data,
            error             = ok ? null : $"[{flResp.Code}] {flResp.Message}",
            logId             = 0,
            codigo_mf_numero  = codigoNumNormalizado,
            codigo_mf_texto   = mensajeNormalizado
        };
    }


    public async Task<TimbradoResponse> TimbrarDesdeIniAsync(string apiKey, string ini, 
        IReadOnlyDictionary<string, string>? adicionales = null, 
        CancellationToken ct = default)
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

        // 3.1) Branch FacturaLO PLUS — parsear INI, mapear a JSON MF y mandar XML
        if (string.Equals(tenant.PacProveedor, "facturalo", StringComparison.OrdinalIgnoreCase))
        {
            var serieIni = ExtractIniValue(ini2, "factura", "Serie");
            var folioIni = ExtractIniValue(ini2, "factura", "Folio");
            var tipoCompIni = ExtractIniValue(ini2, "factura", "tipocomprobante");

            var docIni = _iniParser.Parse(ini2);
            var jsonMfIni = await _mapper.MapToJsonAsync(docIni, tenant, cert);
            var jobjMfIni = JObject.Parse(jsonMfIni);

            return await TimbrarConFacturaloDesdeJsonMfAsync(
                tenant:            tenant,
                cert:              cert,
                jsonMf:            jobjMfIni,
                tipo:              "ini",
                rfcEmisor:         rfcEmisor,
                serie:             serieIni,
                folio:             folioIni,
                tipoDeComprobante: tipoCompIni,
                adicionales:       adicionales,
                ct:                ct);
        }

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
       //   text = RemoveDuplicateLines(text);   no activar o marcara error no clasificado

        // C) quitar bloques de sección duplicados (mismo header repetido)
        text = RemoveDuplicateSectionsKeepFirst(text);

        return text.Trim();
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
    //************************************timbrar desde JSON ya construido ************************//
    public async Task<TimbradoResponse> TimbrarDesdeJsonAsync(
        string apiKey,
        string json,
        IReadOnlyDictionary<string, string>? adicionales = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new UnauthorizedAccessException("Falta API Key (X-Api-Key).");
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("El campo 'json' es requerido.");

        // 1) Parsear el JSON entrante
        JObject jobj;
        try
        {
            jobj = JObject.Parse(json);
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"El JSON enviado no es válido: {ex.Message}");
        }

        // 2) Extraer RFC del emisor para identificar el tenant
        var rfcEmisor = jobj["emisor"]?["rfc"]?.Value<string>()?.Trim();
        if (string.IsNullOrWhiteSpace(rfcEmisor))
            throw new ArgumentException("No se encontró emisor.rfc en el JSON.");

        // 3) Extraer datos de identificación para log
        var serie              = jobj["factura"]?["serie"]?.Value<string>();
        var folio              = jobj["factura"]?["folio"]?.Value<string>();
        var tipoDeComprobante  = jobj["factura"]?["tipocomprobante"]?.Value<string>();

        // 4) Resolver tenant + certificado
        var (tenant, cert) = await _tenantCfg.GetByApiKeyAsync(apiKey, rfcEmisor);

        // 4.1) Branch FacturaLO PLUS — convertir JSON->XML y mandar a /xml
        if (string.Equals(tenant.PacProveedor, "facturalo", StringComparison.OrdinalIgnoreCase))
        {
            return await TimbrarConFacturaloDesdeJsonMfAsync(
                tenant:            tenant,
                cert:              cert,
                jsonMf:            jobj,
                tipo:              "raw-json",
                rfcEmisor:         rfcEmisor,
                serie:             serie,
                folio:             folio,
                tipoDeComprobante: tipoDeComprobante,
                adicionales:       adicionales,
                ct:                ct);
        }

        // 5) Inyectar credenciales PAC del tenant (sobreescribe lo que venga en el JSON)
        var pacPass = string.IsNullOrWhiteSpace(tenant.PacPasswordEnc)
            ? ""
            : _crypto.DecryptFromBase64(tenant.PacPasswordEnc);

        if (jobj["PAC"] == null) jobj["PAC"] = new JObject();
        jobj["PAC"]!["usuario"]    = tenant.PacUsuario ?? "";
        jobj["PAC"]!["pass"]       = pacPass;
        jobj["PAC"]!["produccion"] = tenant.PacProduccion ? "SI" : "NO";

        // 6) Inyectar certificado del tenant (cer / key / pass)
        if (string.IsNullOrWhiteSpace(cert.CerPath) || string.IsNullOrWhiteSpace(cert.KeyPath))
            throw new ArgumentException("El certificado del tenant no tiene cer_path / key_path configurado.");

        var cerBytes = await File.ReadAllBytesAsync(cert.CerPath, ct);
        var keyBytes = await File.ReadAllBytesAsync(cert.KeyPath, ct);
        var cerB64   = Convert.ToBase64String(cerBytes);
        var keyB64   = Convert.ToBase64String(keyBytes);

        string keyPass = "";
        if (!string.IsNullOrWhiteSpace(cert.KeyPasswordEnc))
        {
            var s = cert.KeyPasswordEnc.Trim();
            try   { keyPass = _crypto.DecryptFromBase64(s); }
            catch { keyPass = s; } // texto plano
        }

        if (jobj["conf"] == null) jobj["conf"] = new JObject();
        jobj["conf"]!["cer"]  = cerB64;
        jobj["conf"]!["key"]  = keyB64;
        jobj["conf"]!["pass"] = keyPass;

        // 7) Serializar de vuelta y enviar a MultiFacturas
        var jsonFinal = jobj.ToString(Newtonsoft.Json.Formatting.None);
        var raw = await _mfApi.TimbrarJsonAsync(jsonFinal, ct);

        // 8) Parsear respuesta
        var parsed = MfApiResponseParser.Parse(raw);
        var meta   = parsed.Meta;
        var ok     = meta.CodigoMfNumero == 0;

        const string tipo = "raw-json";

        // 9) Logging
        if (ok)
        {
            try
            {
                await _logs.LogOkAsync(
                    tenantId:          tenant.Id,
                    rfcEmisor:         rfcEmisor,
                    meta:              meta,
                    uuid:              parsed.Uuid ?? meta.Uuid,
                    tipo:              tipo,
                    xmltimbrado:       parsed.XmlTimbrado ?? "",
                    serie:             serie,
                    folio:             folio,
                    tipoDeComprobante: tipoDeComprobante,
                    adicionales:       adicionales,
                    ct:                ct);
            }
            catch { /* el logger no debe tumbar la respuesta */ }
        }
        else
        {
            await _logs.LogErrorAsync(
                tenantId:       tenant.Id,
                rfcEmisor:      rfcEmisor,
                meta:           meta,
                jsonEnviado:    jsonFinal,
                tipo:           tipo,
                detalleInterno: meta.CodigoMfTexto,
                adicionales:    adicionales,
                ct:             ct);
        }

        // 10) Respuesta al cliente
        return new TimbradoResponse
        {
            ok          = ok,
            codigo      = meta.CodigoMfNumero?.ToString() ?? meta.CodigoMfTexto,
            mensaje     = meta.CodigoMfTexto,
            uuid        = parsed.Uuid ?? meta.Uuid,
            xmlTimbrado = parsed.XmlTimbrado,
            rawPac      = parsed.RawPac,
            error       = ok ? null : meta.CodigoMfTexto,
            logId       = 0
        };
    }

    //************************************timbrar ***************************************************//
    public async Task<TimbradoResponse> TimbrarDesdeIniJsonAsync(
    string apiKey,
    string ini,
    IReadOnlyDictionary<string, string>? adicionales = null,
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

        // 5.1) Branch FacturaLO PLUS — usar el mismo JSON MF para construir XML
        if (string.Equals(tenant.PacProveedor, "facturalo", StringComparison.OrdinalIgnoreCase))
        {
            var jobjMf = JObject.Parse(jsonMf);
            return await TimbrarConFacturaloDesdeJsonMfAsync(
                tenant:            tenant,
                cert:              cert,
                jsonMf:            jobjMf,
                tipo:              "ini-json",
                rfcEmisor:         rfcEmisor,
                serie:             serie,
                folio:             folio,
                tipoDeComprobante: TipoDeComprobante,
                adicionales:       adicionales,
                ct:                ct);
        }

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
                    tipo: tipo, xmltimbrado: parsed.XmlTimbrado, serie: serie, folio: folio, tipoDeComprobante: TipoDeComprobante,adicionales: adicionales,
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
                adicionales: adicionales,
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

    // ── TimbrarDesdeXmlAsync — FacturaLO PLUS ────────────────────────────────
    public async Task<TimbradoResponse> TimbrarDesdeXmlAsync(
        string apiKey,
        string xmlSinSello,
        IReadOnlyDictionary<string, string>? adicionales = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new UnauthorizedAccessException("Falta API Key (X-Api-Key).");
        if (string.IsNullOrWhiteSpace(xmlSinSello))
            throw new ArgumentException("El campo 'xml' es requerido.");

        // 1) Extraer RFC emisor del XML para identificar el tenant
        var rfcEmisor = ExtraerRfcEmisorDelXml(xmlSinSello);
        if (string.IsNullOrWhiteSpace(rfcEmisor))
            throw new ArgumentException("No se encontró el RFC del Emisor en el XML (cfdi:Emisor Rfc).");

        // 2) Resolver tenant + certificado
        var (tenant, cert) = await _tenantCfg.GetByApiKeyAsync(apiKey, rfcEmisor);

        // 3) Validar que el tenant use FacturaLO
        if (!tenant.PacProveedor.Equals("facturalo", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "El endpoint /xml solo está disponible para tenants con pac_proveedor = 'facturalo'. " +
                $"Este tenant usa '{tenant.PacProveedor}'.");

        var apikeyXml = tenant.PacApikeyFacturaloActiva;
        if (string.IsNullOrWhiteSpace(apikeyXml))
            throw new InvalidOperationException(
                $"El tenant no tiene configurada la API Key de FacturaLO PLUS para el ambiente " +
                $"{(tenant.PacProduccion ? "PRODUCCIÓN" : "PRUEBAS")}.");

        // 4) Leer keyPEM del certificado (generado al subir el CSD)
        if (string.IsNullOrWhiteSpace(cert.KeyPemPath))
            throw new InvalidOperationException("El certificado no tiene key_pem_path configurado.");

        var keyPem = await File.ReadAllTextAsync(cert.KeyPemPath, ct);

        // 5) Llamar a FacturaLO — timbrarConSello (ellos sellan y timbran)
        var flResp = await _facturalo.TimbrarConSelloAsync(
            apikey:      apikeyXml!,
            xmlSinSello: xmlSinSello,
            keyPem:      keyPem,
            produccion:  tenant.PacProduccion,
            ct:          ct);

        // 6) Interpretar respuesta: code "200" o "0" = éxito (ambos PACs)
        var ok = flResp.Code == "200" || flResp.Code == "0";

        // 7) Logging
        const string tipo = "xml-facturalo";
        var uuidResp = ok ? ExtraerUuidDelXmlTimbrado(flResp.Data) : null;

        // Mensaje unificado: "OK" o "[MODO PRUEBAS] OK" en éxito; texto crudo en error.
        var mensajeNormalizado = ok
            ? (tenant.PacProduccion ? "OK" : "[MODO PRUEBAS] OK")
            : flResp.Message;

        // Meta para que el log llene columnas codigo_mf_*, pac, servidor, etc.
        int? codigoNumXml = int.TryParse(flResp.Code, out var cXml) ? cXml : (int?)null;
        var metaXml = new Vigma.TimbradoGateway.Utils.MfApiMeta
        {
            Pac            = "facturalo",
            Servidor       = tenant.PacProduccion ? "prod" : "dev",
            CodigoMfNumero = codigoNumXml,
            CodigoMfTexto  = mensajeNormalizado,
            Uuid           = uuidResp,
            Cancelada      = false,
            Abortar        = false
        };

        if (ok)
        {
            try
            {
                await _logs.LogOkAsync(
                    tenantId:          tenant.Id,
                    rfcEmisor:         rfcEmisor,
                    meta:              metaXml,
                    uuid:              uuidResp,
                    tipo:              tipo,
                    xmltimbrado:       flResp.Data,
                    serie:             null,
                    folio:             null,
                    tipoDeComprobante: null,
                    adicionales:       adicionales,
                    ct:                ct);
            }
            catch { /* el logger no debe tumbar la respuesta */ }
        }
        else
        {
            try
            {
                // Para diagnóstico, agregamos al detalle_interno la URL REAL que se invocó
                var urlRealXml = !string.IsNullOrWhiteSpace(flResp.UrlUsada)
                    ? flResp.UrlUsada
                    : (tenant.PacProduccion
                        ? "https://app.facturaloplus.com/ws/servicio.do"
                        : "https://dev.facturaloplus.com/ws/servicio.do");
                var detalleXml =
                    $"[{flResp.Code}] {flResp.Message} | " +
                    $"ambiente={(tenant.PacProduccion ? "prod" : "dev")} | " +
                    $"url={urlRealXml}";

                await _logs.LogErrorAsync(
                    tenantId:       tenant.Id,
                    rfcEmisor:      rfcEmisor,
                    meta:           metaXml,
                    jsonEnviado:    xmlSinSello,
                    tipo:           tipo,
                    detalleInterno: detalleXml,
                    adicionales:    adicionales,
                    ct:             ct);
            }
            catch { }
        }

        // 8) Respuesta al cliente — shape consistente con MultiFacturas
        // Normalizamos codigo "200" → "0" en éxito para compatibilidad con SDKs cliente.
        var codigoNormalizadoXml = ok ? "0" : flResp.Code;
        var codigoNumNormalizadoXml = ok ? 0 : codigoNumXml;

        return new TimbradoResponse
        {
            ok                = ok,
            codigo            = codigoNormalizadoXml,
            mensaje           = mensajeNormalizado,
            uuid              = uuidResp,
            xmlTimbrado       = ok ? flResp.Data : null,
            cfdi              = ok ? flResp.Data : null,
            rawPac            = ok ? null : flResp.Data,
            error             = ok ? null : $"[{flResp.Code}] {flResp.Message}",
            logId             = 0,
            codigo_mf_numero  = codigoNumNormalizadoXml,
            codigo_mf_texto   = mensajeNormalizado
        };
    }

    /// <summary>
    /// Devuelve una representación ofuscada de la apikey para diagnóstico:
    /// "ABCD...WXYZ (len=32)". Útil en detalle_interno sin exponer credenciales.
    /// </summary>
    private static string OfuscarApikey(string? apikey)
    {
        if (string.IsNullOrWhiteSpace(apikey)) return "(vacía)";
        var k = apikey.Trim();
        if (k.Length <= 8) return $"*** (len={k.Length})";
        return $"{k[..4]}...{k[^4..]} (len={k.Length})";
    }

    /// <summary>
    /// Resuelve la ruta del archivo .key.pem del certificado, con varios fallbacks
    /// pensados para certificados ya cargados antes de que se persistiera key_pem_path.
    /// Convención del proyecto: junto al .key existe el .key.pem, y/o existe en
    /// /opt/timbrado/certs/{tenantId}/{RFC}/{RFC}.key.pem
    /// </summary>
    private static string? ResolverKeyPemPath(Certificado cert)
    {
        // 1) Si la columna está seteada y el archivo existe, úsala
        if (!string.IsNullOrWhiteSpace(cert.KeyPemPath) && File.Exists(cert.KeyPemPath))
            return cert.KeyPemPath!;

        // 2) Convención: <key_path>.pem
        if (!string.IsNullOrWhiteSpace(cert.KeyPath))
        {
            var sibling = cert.KeyPath + ".pem";
            if (File.Exists(sibling)) return sibling;

            // 3) <dir-de-key>/{RFC}.key.pem
            var dir = Path.GetDirectoryName(cert.KeyPath);
            if (!string.IsNullOrWhiteSpace(dir) && !string.IsNullOrWhiteSpace(cert.RFC))
            {
                var byRfc = Path.Combine(dir!, cert.RFC + ".key.pem");
                if (File.Exists(byRfc)) return byRfc;
            }

            // 4) <dir-de-key>/<basename-key>.pem  (reemplazando extensión)
            var fnNoExt = Path.GetFileNameWithoutExtension(cert.KeyPath);
            if (!string.IsNullOrWhiteSpace(dir) && !string.IsNullOrWhiteSpace(fnNoExt))
            {
                var alt = Path.Combine(dir!, fnNoExt + ".pem");
                if (File.Exists(alt)) return alt;
            }
        }

        return null;
    }

    // ── Helpers XML ──────────────────────────────────────────────────────────

    private static string? ExtraerRfcEmisorDelXml(string xml)
    {
        try
        {
            var doc    = System.Xml.Linq.XDocument.Parse(xml);
            var emisor = doc.Descendants()
                            .FirstOrDefault(e => e.Name.LocalName.Equals("Emisor",
                                StringComparison.OrdinalIgnoreCase));
            return emisor?.Attribute("Rfc")?.Value?.Trim();
        }
        catch { return null; }
    }

    private static string? ExtraerUuidDelXmlTimbrado(string xml)
    {
        try
        {
            var doc    = System.Xml.Linq.XDocument.Parse(xml);
            var timbre = doc.Descendants()
                            .FirstOrDefault(e => e.Name.LocalName.Equals("TimbreFiscalDigital",
                                StringComparison.OrdinalIgnoreCase));
            return timbre?.Attribute("UUID")?.Value?.Trim()
                ?? timbre?.Attribute("uuid")?.Value?.Trim();
        }
        catch { return null; }
    }
}