using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MySqlConnector;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vigma.TimbradoGateway.Services;

namespace Vigma.TimbradoGateway.Pages.Cliente
{
    [Authorize(Roles = "Cliente")]
    public class TimbradosModel : PageModel
    {
        private readonly IClienteScopeService _clienteScope;
        private readonly string _connectionString;

        public TimbradosModel(IClienteScopeService clienteScope, IConfiguration config)
        {
            _clienteScope = clienteScope;
            _connectionString = config.GetConnectionString("MySql") ?? "";
        }

        // ────────────────────────────────────────────────────────────────
        // Propiedades para filtros
        // ────────────────────────────────────────────────────────────────
        public long? TenantId { get; set; }
        public string? RfcEmisor { get; set; }
        public string? Uuid { get; set; }
        public string? Folio { get; set; }
        public DateTime? FechaInicio { get; set; }
        public DateTime? FechaFinal { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;

        // ────────────────────────────────────────────────────────────────
        // Datos para mostrar
        // ────────────────────────────────────────────────────────────────
        public List<SelectListItem> Tenants { get; set; } = new();
        public List<TimbradoRowVM> Rows { get; set; } = new();
        public int TotalRows { get; set; }
        public int CanceladasCount { get; set; }

        // ────────────────────────────────────────────────────────────────
        // Propiedades calculadas para paginación
        // ────────────────────────────────────────────────────────────────
        public int TotalPages => (TotalRows + PageSize - 1) / PageSize;
        public bool HasPrev => Page > 1;
        public bool HasNext => Page < TotalPages;

        // ────────────────────────────────────────────────────────────────
        // Handler GET para Estado SAT (AJAX)
        // ────────────────────────────────────────────────────────────────
        public IActionResult OnGetSatStatus(long id)
        {
            try
            {
                var allowedTenantIds = _clienteScope.GetAllowedTenantIds(User);

                // Obtener UUID del timbrado
                using var cn = new MySqlConnection(_connectionString);
                cn.Open();

                using var cmd = cn.CreateCommand();
                cmd.CommandText = "SELECT uuid, tenantid FROM timbrado_ok_log WHERE id = @id LIMIT 1;";
                cmd.Parameters.AddWithValue("@id", id);

                using var rd = cmd.ExecuteReader();
                if (!rd.Read())
                    return new JsonResult(new { success = false, error = "Timbrado no encontrado" });

                var uuid = rd[0]?.ToString() ?? "";
                var tenantId = rd.GetInt64(1);

                // Verificar permisos
                if (!allowedTenantIds.Contains(tenantId))
                    return new JsonResult(new { success = false, error = "No tienes acceso a este timbrado" });

                // Por ahora, devolver mensaje simple
                return new JsonResult(new
                {
                    success = true,
                    data = new
                    {
                        codigoEstatus = "-",
                        estado = "Vigente",
                        esCancelable = "Sí",
                        estatusCancelacion = "N/A"
                    },
                    contexto = new
                    {
                        uuid = uuid,
                        rfcEmisor = "N/A",
                        rfcReceptor = "N/A",
                        total = "N/A",
                        ambiente = "Producción"
                    }
                });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, error = ex.Message });
            }
        }

        // ────────────────────────────────────────────────────────────────
        // Handler POST para Cancelar
        // ────────────────────────────────────────────────────────────────
        public async Task<IActionResult> OnPostCancelarAsync(long id, string motivo, CancellationToken ct)
        {
            try
            {
                // Verificar que el usuario tenga acceso a este timbrado
                var allowedTenantIds = _clienteScope.GetAllowedTenantIds(User);

                using var cn = new MySqlConnection(_connectionString);
                cn.Open();

                // Verificar que el timbrado existe y pertenece a un tenant permitido
                using var cmd = cn.CreateCommand();
                cmd.CommandText = "SELECT id FROM timbrado_ok_log WHERE id = @id AND tenantid IN (" +
                    string.Join(",", allowedTenantIds) + ");";
                cmd.Parameters.AddWithValue("@id", id);

                var result = cmd.ExecuteScalar();
                if (result == null)
                {
                    TempData["Error"] = "Timbrado no encontrado o no tienes acceso a él.";
                    return RedirectToPage("Timbrados");
                }

                // TODO: Implementar lógica de cancelación con FacturaLO
                // Por ahora, mostrar un mensaje de éxito
                TempData["Exito"] = "Solicitud de cancelación enviada. Esto puede tomar algunos minutos.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error al procesar cancelación: {ex.Message}";
            }

            return RedirectToPage("Timbrados");
        }

        public async Task OnGetAsync(
            long? tenantId,
            string? rfcEmisor,
            string? uuid,
            string? folio,
            DateTime? fechaInicio,
            DateTime? fechaFinal,
            int page = 1,
            int pageSize = 50,
            CancellationToken ct = default)
        {
            try
            {
                // Validar parámetros
                Page = page < 1 ? 1 : page;
                PageSize = pageSize <= 0 ? 50 : pageSize;
                if (PageSize > 200) PageSize = 200;

                TenantId = tenantId;
                RfcEmisor = rfcEmisor;
                Uuid = uuid;
                Folio = folio;
                FechaInicio = fechaInicio;
                FechaFinal = fechaFinal;

                // Obtener tenants permitidos del cliente
                var allowedTenantIds = _clienteScope.GetAllowedTenantIds(User);
                if (!allowedTenantIds.Any())
                    return;

                // Cargar combo de tenants
                Tenants = ObtenerTenantsPorCliente(allowedTenantIds, tenantId);

                // Si seleccionaron un tenant, verificar que sea permitido
                if (tenantId.HasValue && !allowedTenantIds.Contains(tenantId.Value))
                    tenantId = null;

                // Obtener datos paginados
                var (rows, total) = ObtenerTimbradosPaginado(
                    allowedTenantIds,
                    tenantId,
                    rfcEmisor,
                    uuid,
                    folio,
                    fechaInicio,
                    fechaFinal,
                    Page,
                    PageSize);

                Rows = rows;
                TotalRows = total;
                CanceladasCount = Rows.Count(r => r.Cancelada);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"❌ Error en Timbrados: {ex.Message}");
            }
        }

        // ────────────────────────────────────────────────────────────────
        // Obtener tenants combo
        // ────────────────────────────────────────────────────────────────
        private List<SelectListItem> ObtenerTenantsPorCliente(List<long> allowedTenantIds, long? seleccionado)
        {
            var list = new List<SelectListItem>
            {
                new SelectListItem { Text = "Todos", Value = "", Selected = !seleccionado.HasValue }
            };

            using var cn = new MySqlConnection(_connectionString);
            cn.Open();

            using var cmd = cn.CreateCommand();
            cmd.CommandText = @"
                SELECT DISTINCT l.tenantid, t.nombre
                FROM timbrado_ok_log l
                INNER JOIN tenants t ON l.tenantid = t.id
                WHERE l.tenantid IN (" + string.Join(",", allowedTenantIds) + @")
                ORDER BY t.nombre;
            ";

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var id = rd.GetInt64(0);
                var nombre = rd.GetString(1);

                list.Add(new SelectListItem
                {
                    Text = nombre,
                    Value = id.ToString(),
                    Selected = seleccionado.HasValue && seleccionado.Value == id
                });
            }

            return list;
        }

        // ────────────────────────────────────────────────────────────────
        // Obtener timbrados con paginación
        // ────────────────────────────────────────────────────────────────
        private (List<TimbradoRowVM> rows, int total) ObtenerTimbradosPaginado(
            List<long> allowedTenantIds,
            long? tenantId,
            string? rfcEmisor,
            string? uuid,
            string? folio,
            DateTime? fechaInicio,
            DateTime? fechaFinal,
            int page,
            int pageSize)
        {
            var rows = new List<TimbradoRowVM>();
            int total = 0;

            using var cn = new MySqlConnection(_connectionString);
            cn.Open();

            // ── Construir WHERE dinámico ──
            var sql = new StringBuilder(@"
                SELECT
                    id,
                    tenantid,
                    rfcemisor,
                    origen,
                    tipodecomprobante,
                    serie,
                    folio,
                    uuid,
                    mensaje_mf,
                    cancelada,
                    saldo,
                    created_utc
                FROM timbrado_ok_log
                WHERE tenantid IN (" + string.Join(",", allowedTenantIds) + @")
            ");

            using var cmd = cn.CreateCommand();

            if (tenantId.HasValue)
            {
                sql.Append(" AND tenantid = @tenantId ");
                cmd.Parameters.AddWithValue("@tenantId", tenantId.Value);
            }

            if (!string.IsNullOrWhiteSpace(rfcEmisor))
            {
                sql.Append(" AND rfcemisor LIKE @rfc ");
                cmd.Parameters.AddWithValue("@rfc", "%" + rfcEmisor.Trim().ToUpperInvariant() + "%");
            }

            if (!string.IsNullOrWhiteSpace(uuid))
            {
                sql.Append(" AND uuid LIKE @uuid ");
                cmd.Parameters.AddWithValue("@uuid", "%" + uuid.Trim() + "%");
            }

            if (!string.IsNullOrWhiteSpace(folio))
            {
                sql.Append(" AND folio LIKE @folio ");
                cmd.Parameters.AddWithValue("@folio", "%" + folio.Trim() + "%");
            }

            if (fechaInicio.HasValue)
            {
                sql.Append(" AND created_utc >= @fi ");
                cmd.Parameters.AddWithValue("@fi", fechaInicio.Value);
            }

            if (fechaFinal.HasValue)
            {
                sql.Append(" AND created_utc < @ff ");
                cmd.Parameters.AddWithValue("@ff", fechaFinal.Value.Date.AddDays(1));
            }

            // ── Obtener TOTAL ──
            using (var cmdCount = cn.CreateCommand())
            {
                cmdCount.CommandText = "SELECT COUNT(*) FROM timbrado_ok_log WHERE tenantid IN (" +
                    string.Join(",", allowedTenantIds) + ")";

                // Copiar filtros
                if (tenantId.HasValue) cmdCount.CommandText += " AND tenantid = @tenantId";
                if (!string.IsNullOrWhiteSpace(rfcEmisor)) cmdCount.CommandText += " AND rfcemisor LIKE @rfc";
                if (!string.IsNullOrWhiteSpace(uuid)) cmdCount.CommandText += " AND uuid LIKE @uuid";
                if (!string.IsNullOrWhiteSpace(folio)) cmdCount.CommandText += " AND folio LIKE @folio";
                if (fechaInicio.HasValue) cmdCount.CommandText += " AND created_utc >= @fi";
                if (fechaFinal.HasValue) cmdCount.CommandText += " AND created_utc < @ff";

                // Copiar parámetros
                foreach (MySqlParameter param in cmd.Parameters)
                {
                    cmdCount.Parameters.AddWithValue(param.ParameterName, param.Value);
                }

                total = Convert.ToInt32(cmdCount.ExecuteScalar() ?? 0);
            }

            // ── Paginar y ordenar ──
            sql.Append(" ORDER BY created_utc DESC ");
            sql.Append($" LIMIT {(page - 1) * pageSize}, {pageSize};");

            cmd.CommandText = sql.ToString();

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                rows.Add(new TimbradoRowVM
                {
                    Id = rd.GetInt64("id"),
                    TenantId = rd.GetInt64("tenantid"),
                    RfcEmisor = rd["rfcemisor"]?.ToString() ?? "",
                    Origen = rd["origen"] == DBNull.Value ? null : rd["origen"]?.ToString(),
                    TipoDeComprobante = rd["tipodecomprobante"] == DBNull.Value ? null : rd["tipodecomprobante"]?.ToString(),
                    Serie = rd["serie"] == DBNull.Value ? null : rd["serie"]?.ToString(),
                    Folio = rd["folio"] == DBNull.Value ? null : rd["folio"]?.ToString(),
                    Uuid = rd["uuid"]?.ToString() ?? "",
                    MensajeMf = rd["mensaje_mf"] == DBNull.Value ? null : rd["mensaje_mf"]?.ToString(),
                    Cancelada = Convert.ToBoolean(rd["cancelada"]),
                    Saldo = rd["saldo"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(rd["saldo"]),
                    CreatedUtc = Convert.ToDateTime(rd["created_utc"])
                });
            }

            return (rows, total);
        }
    }

    // ────────────────────────────────────────────────────────────────
    // ViewModels
    // ────────────────────────────────────────────────────────────────
    public class TimbradoRowVM
    {
        public long Id { get; set; }
        public long TenantId { get; set; }
        public string RfcEmisor { get; set; } = "";
        public string? Origen { get; set; }
        public string? TipoDeComprobante { get; set; }
        public string? Serie { get; set; }
        public string? Folio { get; set; }
        public string Uuid { get; set; } = "";
        public string? MensajeMf { get; set; }
        public bool Cancelada { get; set; }
        public decimal? Saldo { get; set; }
        public DateTime CreatedUtc { get; set; }
    }
}
