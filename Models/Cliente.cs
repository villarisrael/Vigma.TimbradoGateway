namespace Vigma.TimbradoGateway.Models
{
    /// <summary>
    /// Representa un cliente distribuidor que tiene múltiples tenants.
    /// Mapea tabla: clientes
    /// </summary>
    public class Cliente
    {
        /// <summary>ID del cliente (PK)</summary>
        public long Id { get; set; }

        /// <summary>Razón social del cliente</summary>
        public string Nombre { get; set; } = "";

        /// <summary>RFC del cliente (opcional)</summary>
        public string? Rfc { get; set; }

        /// <summary>Ruta del logo para el portal cliente, ej: /logos/cliente_1.png</summary>
        public string? LogoPath { get; set; }

        /// <summary>Estado del cliente</summary>
        public bool Activo { get; set; } = true;

        /// <summary>Fecha de creación en UTC</summary>
        public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;

        // ────────────────────────────────────────────────────────────────
        // Relaciones (no mapeadas en BD, solo para navegación en C#)
        // ────────────────────────────────────────────────────────────────

        /// <summary>Tenants que pertenecen a este cliente</summary>
        public virtual ICollection<Tenant>? Tenants { get; set; }

        /// <summary>Usuarios oficina asociados a este cliente</summary>
        public virtual ICollection<UsuarioOficina>? UsuariosOficina { get; set; }
    }
}
