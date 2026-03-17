using Google.Apis.Auth.OAuth2;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Vigma.TimbradoGateway.Services.Alertas;

// ─────────────────────────────────────────────────────────────────────────────
//  FcmService
//  Envía notificaciones push via Firebase Cloud Messaging HTTP v1.
//  Las credenciales (ProjectId + ServiceAccountJson) vienen de appsettings.json
//  sección "Firebase" — un solo proyecto Firebase para toda la plataforma.
// ─────────────────────────────────────────────────────────────────────────────

public interface IFcmService
{
    /// <summary>
    /// Envía una notificación push a un token FCM específico.
    /// Retorna el firebase_message_id si fue exitoso, o lanza excepción si falla.
    /// </summary>
    Task<string> SendAsync(
        string fcmToken,
        string title,
        string message,
        string priority = "normal",
        Dictionary<string, string>? data = null,
        CancellationToken ct = default);
}

public sealed class FcmService : IFcmService
{
    private readonly HttpClient   _http;
    private readonly string       _projectId;
    private readonly string       _serviceAccountJson;

    public FcmService(HttpClient http, IConfiguration config)
    {
        _http = http;
        _projectId = config["Firebase:ProjectId"]
                     ?? throw new InvalidOperationException("Firebase:ProjectId no configurado.");

        var path = config["Firebase:ServiceAccountPath"]
                   ?? throw new InvalidOperationException("Firebase:ServiceAccountPath no configurado.");

        _serviceAccountJson = File.ReadAllText(path);
    }

    public async Task<string> SendAsync(
        string fcmToken,
        string title,
        string message,
        string priority  = "normal",
        Dictionary<string, string>? data = null,
        CancellationToken ct = default)
    {
        var accessToken = await GetAccessTokenAsync(ct);

        var url = $"https://fcm.googleapis.com/v1/projects/{_projectId}/messages:send";
        var dataPayload = new Dictionary<string, string>
        {
            ["title"] = title,
            ["message"] = message,
            ["priority"] = priority
         
        };

        // Agregar los data extras del request encima
        if (data is not null)
            foreach (var kv in data)
                dataPayload[kv.Key] = kv.Value;

        // Construir payload FCM HTTP v1
        var payload = new
        {
            message = new
            {
                token        = fcmToken,
                notification = new { title, body = message },
                android = new
                {
                    priority = priority.ToLower() == "high" ? "HIGH" : "NORMAL",
                    notification = new
                    {
                        sound        = "default",
                        click_action = "FLUTTER_NOTIFICATION_CLICK"
                    }
                },
                data = dataPayload
            }
        };

        var json    = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _http.PostAsync(url, content, ct);
        var body     = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"FCM error {(int)response.StatusCode}: {body}");

        // Extraer el message_id del response de Firebase
        using var doc = JsonDocument.Parse(body);
        var msgId = doc.RootElement
                       .GetProperty("name")
                       .GetString() ?? "ok";

        return msgId;
    }

    // ─── OAuth2 token desde Service Account JSON ──────────────────────────────
    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        var credential = GoogleCredential
            .FromJson(_serviceAccountJson)
            .CreateScoped("https://www.googleapis.com/auth/firebase.messaging");

        var tokenResponse = await credential
            .UnderlyingCredential
            .GetAccessTokenForRequestAsync(cancellationToken: ct);

        return tokenResponse;
    }
}
