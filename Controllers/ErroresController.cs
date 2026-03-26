using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Configuration;

using MySqlConnector;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;


using Vigma.TimbradoGateway.ViewModels.Errores;
using Vigma.TimbradoGateway.ViewsModels.Errores;

namespace Vigma.TimbradoGateway.Controllers
{
    [Authorize]
    public class ErroresController : Controller
    {
        private readonly string _cs;

        public ErroresController(IConfiguration cfg)
        {
            // appsettings.json -> "ConnectionStrings": { "MySql": "..." }
            _cs = cfg.GetConnectionString("MySql")!;
        }

        [HttpGet]
        public IActionResult Index(int? tenantId, string? rfcEmisor, DateTime? fechaInicio, DateTime? fechaFinal)
        {
            var vm = new TimbradoErrorIndiceVM
            {
                TenantId = tenantId,
                RfcEmisor = rfcEmisor,
                FechaInicio = fechaInicio,
                FechaFinal = fechaFinal
            };

            vm.Tenants = ObtenerTenants(tenantId);
            vm.Rows = ObtenerErrores(tenantId, rfcEmisor, fechaInicio, fechaFinal);

            return View(vm);
        }

        [HttpGet]
        public IActionResult VistaTimbradoError(long id)
        {
            var row = ObtenerErrorPorId(id);
            if (row == null) return NotFound();

            return View(row);
        }

        [HttpGet]
        public IActionResult VistaAdicionales(long id)
        {
            var row = ObtenerErrorPorId(id);
            if (row == null) return NotFound();

            return View(row);
        }

        private List<SelectListItem> ObtenerTenants(int? seleccionado)
        {
            var list = new List<SelectListItem>
            {
                new SelectListItem { Text = "Todos", Value = "", Selected = !seleccionado.HasValue }
            };

            using var cn = new MySqlConnection(_cs);
            cn.Open();

            using var cmd = cn.CreateCommand();
            cmd.CommandText = @"
                SELECT DISTINCT tenant_id,nombre
 FROM timbrado_error_log l inner join  tenants t on l.tenant_id= t.id 
 ORDER BY t.nombre;
            ";

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var t = rd.GetInt32(0);
                var n = rd.GetString(1);
                list.Add(new SelectListItem
                {
                    Text = n.ToString(),
                    Value = t.ToString(),
                    Selected = seleccionado.HasValue && seleccionado.Value == t
                });
            }

            return list;
        }

        private List<TimbradoErrorLogRowVM> ObtenerErrores(int? tenantId, string? rfcEmisor, DateTime? fechaInicio, DateTime? fechaFinal)
        {
            var rows = new List<TimbradoErrorLogRowVM>();

            using var cn = new MySqlConnection(_cs);
            cn.Open();

            using var cmd = cn.CreateCommand();

            var sql = new StringBuilder();
            sql.Append(@"
                SELECT
                    id,
                    tenant_id,
                    rfc_emisor,
                    codigo_mf_numero,
                    codigo_mf_texto,
                    creado_utc,Adicionales

                FROM timbrado_error_log
                WHERE 1=1
            ");

            if (tenantId.HasValue)
            {
                sql.Append(" AND tenant_id = @tenantId ");
                cmd.Parameters.AddWithValue("@tenantId", tenantId.Value);
            }

            if (!string.IsNullOrWhiteSpace(rfcEmisor))
            {
                sql.Append(" AND rfc_emisor LIKE @rfc ");
                cmd.Parameters.AddWithValue("@rfc", "%" + rfcEmisor.Trim().ToUpperInvariant() + "%");
            }

            if (fechaInicio.HasValue)
            {
                sql.Append(" AND creado_utc >= @fi ");
                cmd.Parameters.AddWithValue("@fi", fechaInicio.Value);
            }

            if (fechaFinal.HasValue)
            {
                // Incluye todo el día final si el datepicker manda solo fecha
                sql.Append(" AND creado_utc < @ff ");
                cmd.Parameters.AddWithValue("@ff", fechaFinal.Value.Date.AddDays(1));
            }

            sql.Append(" ORDER BY creado_utc DESC LIMIT 100; ");
            cmd.CommandText = sql.ToString();

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                rows.Add(new TimbradoErrorLogRowVM
                {
                    Id = rd.GetInt64("id"),
                    TenantId = rd.GetInt32("tenant_id"),
                    RfcEmisor = rd["rfc_emisor"]?.ToString() ?? "",
                    CodigoMfNumero = rd["codigo_mf_numero"] == DBNull.Value ? (int?)null : Convert.ToInt32(rd["codigo_mf_numero"]),
                    CodigoMfTexto = rd["codigo_mf_texto"]?.ToString(),
                    Adicionales = rd["Adicionales"]?.ToString(),
                    CreadoUtc = Convert.ToDateTime(rd["creado_utc"])
                });
            }

            return rows;
        }

        private TimbradoErrorLogRowVM? ObtenerErrorPorId(long id)
        {
            using var cn = new MySqlConnection(_cs);
            cn.Open();

            using var cmd = cn.CreateCommand();
            cmd.CommandText = @"
        SELECT
            id,
            tenant_id,
            rfc_emisor,
            codigo_mf_numero,
            codigo_mf_texto,
            json_enviado,
            creado_utc, Adicionales
        FROM timbrado_error_log
        WHERE id = @id
        LIMIT 1;
    ";
            cmd.Parameters.AddWithValue("@id", id);

            using var rd = cmd.ExecuteReader();
            if (!rd.Read()) return null;

            return new TimbradoErrorLogRowVM
            {
                Id = rd.GetInt64("id"),
                TenantId = rd.GetInt32("tenant_id"),
                RfcEmisor = rd["rfc_emisor"]?.ToString() ?? "",
                CodigoMfNumero = rd["codigo_mf_numero"] == DBNull.Value ? (int?)null : Convert.ToInt32(rd["codigo_mf_numero"]),
                CodigoMfTexto = rd["codigo_mf_texto"]?.ToString(),
                Jsonenviado = rd["json_enviado"]?.ToString(),
                Adicionales = rd["Adicionales"]?.ToString(),
                CreadoUtc = Convert.ToDateTime(rd["creado_utc"])
            };
        }

        [HttpGet]
        public IActionResult GetJsonFormatted(long id)
        {
            var row = ObtenerErrorPorId(id);
            if (row == null || string.IsNullOrEmpty(row.Jsonenviado))
                return Json(new { error = "JSON no encontrado" });

            try
            {
                var formateado = FormatearJson(row.Jsonenviado);
                return Json(new
                {
                    success = true,
                    jsonFormatted = formateado,
                    esValido = true
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    error = ex.Message,
                    jsonRaw = row.Jsonenviado
                });
            }
        }

        public IActionResult GetAdicionalesFormatted(long id)
        {
            var row = ObtenerErrorPorId(id);
            if (row == null || string.IsNullOrEmpty(row.Adicionales))
                return Json(new { error = "Adicionales no encontrados" });

            try
            {
                var formateado = FormatearJson(row.Adicionales);
                return Json(new
                {
                    success = true,
                    jsonFormatted = formateado,
                    esValido = true
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    error = ex.Message,
                    jsonRaw = row.Adicionales
                });
            }
        }


        private string FormatearJson(string json)
        {
            try
            {
                var parsedJson = JToken.Parse(json);
                return parsedJson.ToString(Formatting.Indented);
            }
            catch
            {
                return json; // Devolver el original si no se puede formatear
            }
        }

        private bool EsJsonValido(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;

            json = json.Trim();
            if ((json.StartsWith("{") && json.EndsWith("}")) ||
                (json.StartsWith("[") && json.EndsWith("]")))
            {
                try
                {
                    JToken.Parse(json);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            return false;
        }

        [HttpGet]
        public IActionResult Estadisticaerrores()
        {
            var stats = ObtenerEstadisticas();

            // Pasamos las tres listas Top como JSON para que el JS del cliente
            // pueda cambiar de periodo sin roundtrip al servidor.
            ViewBag.StatsJson = JsonConvert.SerializeObject(new
            {
                total      = stats.TotalHoy,
                categorias = new
                {
                    sat         = stats.CatSat,
                    certificado = stats.CatCertificado,
                    timeout     = stats.CatTimeout,
                    otros       = stats.CatOtros
                },
                topHoy    = stats.TopHoy.Select(x => new { nombre = x.Nombre, meta = x.Meta, errores = x.Errores }),
                topSemana = stats.TopSemana.Select(x => new { nombre = x.Nombre, meta = x.Meta, errores = x.Errores }),
                topMes    = stats.TopMes.Select(x => new { nombre = x.Nombre, meta = x.Meta, errores = x.Errores }),
                // Totales para el badge del selector
                totalSemana = stats.TotalSemana,
                totalMes    = stats.TotalMes
            });

            return View(stats);
        }

        // ────────────────────────────────────────────────────────────────────
        //  Consultas agregadas para Estadisticaerrores
        // ────────────────────────────────────────────────────────────────────
        private EstadisticasErroresVM ObtenerEstadisticas()
        {
            var vm = new EstadisticasErroresVM();

            using var cn = new MySqlConnection(_cs);
            cn.Open();

            // ── 1) Totales por periodo ────────────────────────────────────
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT
                        SUM(CASE WHEN creado_utc >= DATE_SUB(NOW(), INTERVAL 24 HOUR)  THEN 1 ELSE 0 END) AS hoy,
                        SUM(CASE WHEN creado_utc >= DATE_SUB(NOW(), INTERVAL 7  DAY)   THEN 1 ELSE 0 END) AS semana,
                        SUM(CASE WHEN creado_utc >= DATE_SUB(NOW(), INTERVAL 30 DAY)   THEN 1 ELSE 0 END) AS mes
                    FROM timbrado_error_log
                    WHERE creado_utc >= DATE_SUB(NOW(), INTERVAL 30 DAY);";

                using var rd = cmd.ExecuteReader();
                if (rd.Read())
                {
                    vm.TotalHoy    = rd["hoy"]    == DBNull.Value ? 0 : Convert.ToInt32(rd["hoy"]);
                    vm.TotalSemana = rd["semana"]  == DBNull.Value ? 0 : Convert.ToInt32(rd["semana"]);
                    vm.TotalMes    = rd["mes"]     == DBNull.Value ? 0 : Convert.ToInt32(rd["mes"]);
                }
            }

            // ── 2) Categorías últimas 24 h ────────────────────────────────
            // Clasificación por codigo_mf_numero y texto del PAC.
            // Rangos SAT: 301-399 (estructura/schema CFDI)
            // Rangos Cert: 201-299 (certificado/llave)
            // Timeout: texto libre que mencione timeout / connection
            // Otros: el resto
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT
                        SUM(CASE
                            WHEN (codigo_mf_numero BETWEEN 301 AND 399)
                              OR LOWER(codigo_mf_texto) REGEXP 'sat|sello|firma|cfdi40|esquema|schema|xsd|timbre'
                            THEN 1 ELSE 0 END) AS cat_sat,

                        SUM(CASE
                            WHEN (codigo_mf_numero BETWEEN 201 AND 299)
                              OR LOWER(codigo_mf_texto) REGEXP 'certificado|\.cer|\.key|llave|caducado|vigencia|password|contraseña'
                            THEN 1 ELSE 0 END) AS cat_cert,

                        SUM(CASE
                            WHEN LOWER(codigo_mf_texto) REGEXP 'timeout|connection|connect|host|unreachable|refused|network|socket|curl'
                            THEN 1 ELSE 0 END) AS cat_timeout,

                        COUNT(*) AS cat_total
                    FROM timbrado_error_log
                    WHERE creado_utc >= DATE_SUB(NOW(), INTERVAL 24 HOUR);";

                using var rd = cmd.ExecuteReader();
                if (rd.Read())
                {
                    vm.CatSat         = rd["cat_sat"]     == DBNull.Value ? 0 : Convert.ToInt32(rd["cat_sat"]);
                    vm.CatCertificado = rd["cat_cert"]    == DBNull.Value ? 0 : Convert.ToInt32(rd["cat_cert"]);
                    vm.CatTimeout     = rd["cat_timeout"] == DBNull.Value ? 0 : Convert.ToInt32(rd["cat_timeout"]);
                    var total         = rd["cat_total"]   == DBNull.Value ? 0 : Convert.ToInt32(rd["cat_total"]);
                    // Otros = total - categorías identificadas (sin doble contar)
                    vm.CatOtros = Math.Max(0, total - vm.CatSat - vm.CatCertificado - vm.CatTimeout);
                }
            }

            // ── 3) Top 10 tenants — helper local ─────────────────────────
            vm.TopHoy    = ObtenerTopTenants(cn, "DATE_SUB(NOW(), INTERVAL 24 HOUR)");
            vm.TopSemana = ObtenerTopTenants(cn, "DATE_SUB(NOW(), INTERVAL 7 DAY)");
            vm.TopMes    = ObtenerTopTenants(cn, "DATE_SUB(NOW(), INTERVAL 30 DAY)");

            return vm;
        }

        private List<TopErrorItem> ObtenerTopTenants(MySqlConnection cn, string desdeExpr)
        {
            var list = new List<TopErrorItem>();

            using var cmd = cn.CreateCommand();
            // Obtenemos top 10 tenants con más errores + el error más frecuente de cada uno
            cmd.CommandText = $@"
                SELECT
                    COALESCE(t.nombre, e.rfc_emisor, 'Desconocido') AS nombre,
                    COUNT(*)                                          AS total,
                    (
                        SELECT COALESCE(i.codigo_mf_texto, CONCAT('Código ', i.codigo_mf_numero), 'Error sin clasificar')
                        FROM timbrado_error_log i
                        WHERE i.tenant_id = e.tenant_id
                          AND i.creado_utc >= {desdeExpr}
                        GROUP BY COALESCE(i.codigo_mf_texto, i.codigo_mf_numero)
                        ORDER BY COUNT(*) DESC
                        LIMIT 1
                    ) AS error_top
                FROM timbrado_error_log e
                LEFT JOIN tenants t ON e.tenant_id = t.id
                WHERE e.creado_utc >= {desdeExpr}
                GROUP BY e.tenant_id, t.nombre, e.rfc_emisor
                ORDER BY total DESC
                LIMIT 10;";

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                list.Add(new TopErrorItem
                {
                    Nombre  = rd["nombre"]    == DBNull.Value ? "—" : rd["nombre"].ToString()!,
                    Meta    = rd["error_top"] == DBNull.Value ? "—" : rd["error_top"].ToString()!,
                    Errores = Convert.ToInt32(rd["total"])
                });
            }

            return list;
        }

    }
}
