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
    }

}
