namespace Vigma.TimbradoGateway.Models
{
    public class UsuarioOficina
    {
        public long Id { get; set; }
        public string Usuario { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public string Rol { get; set; } = "Oficina";
        public string? Nombre { get; set; }
        public bool Activo { get; set; } = true;
        public DateTime Creado { get; set; }

        // ────────────────────────────────────────────────────────────────
        // NUEVO: Relación con Cliente (para usuarios con Rol = "Cliente")
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// FK a clientes. Solo se llena si Rol = "Cliente".
        /// Indica a qué cliente/distribuidor pertenece este usuario.
        /// </summary>
        public long? ClienteId { get; set; }

        /// <summary>Navegación: Cliente al que pertenece este usuario</summary>
        public virtual Cliente? Cliente { get; set; }
    }

}
