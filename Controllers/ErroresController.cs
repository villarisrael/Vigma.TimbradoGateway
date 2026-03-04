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
using System.Text.Json;

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
                // Si no es JSON válido, intentar con System.Text.Json
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    return System.Text.Json.JsonSerializer.Serialize(doc.RootElement,
                        new JsonSerializerOptions { WriteIndented = true });
                }
                catch
                {
                    return json; // Devolver el original si no se puede formatear
                }
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
        public IActionResult Estadisticaerrores(int? tenantId, string? rfcEmisor, DateTime? fechaInicio, DateTime? fechaFinal)
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

    }
}
