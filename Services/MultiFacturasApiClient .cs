using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Vigma.TimbradoGateway.Services;

public interface IMultiFacturasApiClient
{
    /// <summary>
    /// Timbra enviando JSON al endpoint /api.
    /// MF requiere típicamente: modo=JSON, json={...}
    /// </summary>
    Task<string> TimbrarJsonAsync(string json, CancellationToken ct = default);
}

public sealed class MultiFacturasApiClient : IMultiFacturasApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<MultiFacturasApiClient> _log;

    // Opciones de serialización (por si en el futuro mandamos objeto, ahorita recibimos string json)
    private static readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public MultiFacturasApiClient(HttpClient http, ILogger<MultiFacturasApiClient> log)
    {
        _http = http;
        _log = log;
    }

    public async Task<string> TimbrarJsonAsync(string json, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("El JSON es requerido.", nameof(json));

        // MF: según tu prueba en Postman -> 2 variables "modo" y "json"
        // Lo mandamos como form-url-encoded (es lo más común en estos servicios)
        using var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string,string>("modo", "JSON"),
            new KeyValuePair<string,string>("json", json)
        });

        // POST a baseAddress (https://ws.multifacturas.com/api/) sin path extra
        using var resp = await _http.PostAsync("", content, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);

        // Si MF responde 200 con error en body, no lo tratamos como excepción HTTP.
        // Si quieres forzar exception para != 2xx, descomenta:
        // resp.EnsureSuccessStatusCode();

        _log.LogInformation("MF API status={StatusCode} len={Len}", (int)resp.StatusCode, raw?.Length ?? 0);

        return raw;
    }
}
