
using MySqlConnector;

namespace Vigma.TimbradoGateway.Services
{

    public class OficinaUser
    {
        public long Id { get; set; }
        public string Usuario { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public string Rol { get; set; } = "Oficina";
        public string? Nombre { get; set; }
        public bool Activo { get; set; }
    }

    public interface IRepoUsuariosOficina
    {
        Task<OficinaUser?> GetByUsuarioAsync(string usuario, CancellationToken ct = default);
    }

    public class RepoUsuariosOficina : IRepoUsuariosOficina
    {
        private readonly string _cs;
        public RepoUsuariosOficina(IConfiguration cfg) => _cs = cfg.GetConnectionString("MySql")!;

        public async Task<OficinaUser?> GetByUsuarioAsync(string usuario, CancellationToken ct = default)
        {
            await using var cn = new MySqlConnection(_cs);
            await cn.OpenAsync(ct);

            var sql = @"SELECT Id, Usuario, PasswordHash, Rol, Nombre, Activo
                    FROM usuarios_oficina
                    WHERE Usuario = @u
                    LIMIT 1;";

            await using var cmd = new MySqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@u", usuario);

            await using var rd = await cmd.ExecuteReaderAsync(ct);
            if (!await rd.ReadAsync(ct)) return null;

            return new OficinaUser
            {
                Id = rd.GetInt64("Id"),
                Usuario = rd.GetString("Usuario"),
                PasswordHash = rd.GetString("PasswordHash"),
                Rol = rd.GetString("Rol"),
                Nombre = rd.IsDBNull(rd.GetOrdinal("Nombre")) ? null : rd.GetString("Nombre"),
                Activo = rd.GetBoolean("Activo")
            };
        }
    }

}
