namespace Vigma.TimbradoGateway.Models.Alertas;

// ─── Request: Registrar token FCM ────────────────────────────────────────────
public class RegisterTokenRequest
{
    /// <summary>ID del trabajador en el sistema del cliente</summary>
    public string  EntidadId      { get; set; } = "";

    /// <summary>Nombre legible del trabajador (opcional)</summary>
    public string? EntidadNombre  { get; set; }

    /// <summary>Token FCM del dispositivo Android</summary>
    public string  Token          { get; set; } = "";
}

// ─── Request: Enviar alerta ───────────────────────────────────────────────────
public class SendAlertRequest
{
    /// <summary>ID del trabajador destino (debe tener token FCM registrado)</summary>
    public string EntidadId   { get; set; } = "";

    /// <summary>Sistema o módulo que origina la alerta. Ej: "ModuloVentas"</summary>
    public string Origin      { get; set; } = "";

    /// <summary>Título de la notificación push</summary>
    public string Title       { get; set; } = "";

    /// <summary>Cuerpo del mensaje</summary>
    public string Message     { get; set; } = "";

    /// <summary>Prioridad: "high" | "normal". Default: normal</summary>
    public string Priority    { get; set; } = "normal";

    /// <summary>Datos extra opcionales que recibe la app (key-value)</summary>
    public Dictionary<string, string>? Data { get; set; }
}

// ─── Response: Resultado del envío ───────────────────────────────────────────
public class SendAlertResponse
{
    public bool    ok            { get; set; }
    public long?   logId         { get; set; }
    public string? firebaseMsgId { get; set; }
    public string? mensaje       { get; set; }
    public string? error         { get; set; }
}

// ─── Response: Registro de token ─────────────────────────────────────────────
public class RegisterTokenResponse
{
    public bool   ok       { get; set; }
    public int?   tokenId  { get; set; }
    public string mensaje  { get; set; } = "";
}
