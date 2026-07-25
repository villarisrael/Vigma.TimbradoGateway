using System.ComponentModel.DataAnnotations.Schema;

namespace Vigma.TimbradoGateway.Models;

public class Tenant
{
    public int Id { get; set; }
    public string? Nombre { get; set; } = "";
    public string? ApiKeyHash { get; set; } = "";
    public string? ApiKeyEnc { get; set; }
    public string? ApiKeyLast4 { get; set; }
    public DateTime? ApiKeyRotatedUtc { get; set; }

    public DateTime? actualizado_utc { get; set; }

    public DateTime? creado_utc { get; set; }

    public bool Activo { get; set; }

    public string? PacUsuario { get; set; }
    public string? PacPasswordEnc { get; set; }
    public bool PacProduccion { get; set; }

    /// <summary>Ruta relativa del logo, ej: /logos/tenant_5.png — null si no tiene logo</summary>
    public string? LogoPath { get; set; }

    /// <summary>PAC activo: "multifacturas" (default) | "facturalo"</summary>
    public string PacProveedor { get; set; } = "multifacturas";

    /// <summary>
    /// API Key de FacturaLO PLUS para el ambiente de <b>PRODUCCIÓN</b> (32 chars).
    /// Columna: <c>pac_apikey_facturalo</c>. Null si el tenant aún no la tiene capturada.
    /// </summary>
    public string? PacApikeyFacturalo { get; set; }

    /// <summary>
    /// API Key de FacturaLO PLUS para el ambiente de <b>PRUEBAS / Sandbox</b> (32 chars).
    /// Columna: <c>pac_apikey_facturalo_test</c>. Null si el tenant aún no la tiene capturada.
    /// </summary>
    public string? PacApikeyFacturaloTest { get; set; }

    /// <summary>
    /// Devuelve la apikey de FacturaLO que corresponde al ambiente activo según
    /// <see cref="PacProduccion"/>. No se mapea a BD.
    /// </summary>
    [NotMapped]
    public string? PacApikeyFacturaloActiva
        => PacProduccion ? PacApikeyFacturalo : PacApikeyFacturaloTest;

    // ────────────────────────────────────────────────────────────────
    // NUEVO: Relación con Cliente (distribuidor)
    // ────────────────────────────────────────────────────────────────

    /// <summary>FK a clientes. Un tenant pertenece a un cliente distribuidor (nullable = sin cliente asignado)</summary>
    public long? ClienteId { get; set; }

    /// <summary>Navegación: Cliente distribuidor al que pertenece este tenant</summary>
    public virtual Cliente? Cliente { get; set; }
}
