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
using System.Xml;
using Vigma.TimbradoGateway.ViewModels.Timbrados;
using Formatting = Newtonsoft.Json.Formatting;

namespace Vigma.TimbradoGateway.Controllers
{
    [Authorize]
    public class TimbradosController : Controller
    {
        private readonly string _cs;

        public TimbradosController(IConfiguration cfg)
        {
            _cs = cfg.GetConnectionString("MySql")!;
        }

        [HttpGet]
        public IActionResult Index(long? tenantId, string? rfcEmisor, DateTime? fechaInicio, DateTime? fechaFinal)
        {
            var vm = new TimbradoIndiceVM
            {
                TenantId = tenantId,
                RfcEmisor = rfcEmisor,
                FechaInicio = fechaInicio,
                FechaFinal = fechaFinal
            };

            vm.Tenants = ObtenerTenants(tenantId);
            vm.Rows = ObtenerTimbrados(tenantId, rfcEmisor, fechaInicio, fechaFinal);

            // (Opcional) si tu VM no tiene CanceladasCount, coméntalo o agrégalo al VM:
            // vm.CanceladasCount = vm.Rows?.Count(r => r.Cancelada) ?? 0;

            return View(vm);
        }

        [HttpGet]
        public IActionResult TimbradosDetalle(long id)
        {
            var row = ObtenerTimbradoPorId(id);
            if (row == null) return NotFound();

            // Busca: Views/Timbrados/TimbradosDetalle.cshtml
            return View(row);
        }

        [HttpGet]
        public IActionResult TimbradosAdicionales(long id)
        {
            var row = ObtenerTimbradoPorId(id);
            if (row == null) return NotFound();

            // Busca: Views/Timbrados/TimbradosDetalle.cshtml
            return View(row);
        }

        // -------- TENANTS (combo) --------
        private List<SelectListItem> ObtenerTenants(long? seleccionado)
        {
            var list = new List<SelectListItem>
            {
                new SelectListItem { Text = "Todos", Value = "", Selected = !seleccionado.HasValue }
            };

            using var cn = new MySqlConnection(_cs);
            cn.Open();

            using var cmd = cn.CreateCommand();
            cmd.CommandText = @"
                SELECT DISTINCT l.tenantid, t.nombre
                FROM timbrado_ok_log l
                INNER JOIN tenants t ON l.tenantid = t.id
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

        // -------- LISTADO --------
        private List<TimbradoLogRowVM> ObtenerTimbrados(long? tenantId, string? rfcEmisor, DateTime? fechaInicio, DateTime? fechaFinal)
        {
            var rows = new List<TimbradoLogRowVM>();

            using var cn = new MySqlConnection(_cs);
            cn.Open();

            using var cmd = cn.CreateCommand();

            var sql = new StringBuilder();
            sql.Append(@"
                SELECT
                    id,
                    tenantid,
                    rfcemisor,
                    Origen,
                    tipodecomprobante,
                    serie,
                    folio,
                    uuid,
                    mensaje_mf,
                    cancelada,
                    saldo,
                    created_utc
                FROM timbrado_ok_log
                WHERE 1=1
            ");

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

            if (fechaInicio.HasValue)
            {
                sql.Append(" AND created_utc >= @fi ");
                cmd.Parameters.AddWithValue("@fi", fechaInicio.Value);
            }

            if (fechaFinal.HasValue)
            {
                // incluir todo el día final
                sql.Append(" AND created_utc < @ff ");
                cmd.Parameters.AddWithValue("@ff", fechaFinal.Value.Date.AddDays(1));
            }

            sql.Append(" ORDER BY created_utc DESC LIMIT 200; ");
            cmd.CommandText = sql.ToString();

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                rows.Add(new TimbradoLogRowVM
                {
                    Id = rd.GetInt64("id"),
                    TenantId = rd.GetInt64("tenantid"),
                    RfcEmisor = rd["rfcemisor"]?.ToString() ?? "",
                    Origen = rd["Origen"] == DBNull.Value ? null : rd["Origen"]?.ToString(),
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

            return rows;
        }

        // -------- DETALLE --------
        private TimbradoDetalleVM? ObtenerTimbradoPorId(long id)
        {
            using var cn = new MySqlConnection(_cs);
            cn.Open();

            using var cmd = cn.CreateCommand();
            cmd.CommandText = @"
                SELECT
                    id,
                    tenantid,
                    rfcemisor,
                    Origen,
                    tipodecomprobante,
                    serie,
                    folio,
                    uuid,
                    mensaje_mf,
                    xmltimbrado,
                    cancelada,
                    saldo,
                    created_utc, Adicionales
                FROM timbrado_ok_log
                WHERE id = @id
                LIMIT 1;
            ";
            cmd.Parameters.AddWithValue("@id", id);

            using var rd = cmd.ExecuteReader();
            if (!rd.Read()) return null;

            return new TimbradoDetalleVM
            {
                Id = rd.GetInt64("id"),
                TenantId = rd.GetInt64("tenantid"),
                RfcEmisor = rd["rfcemisor"]?.ToString() ?? "",
                Origen = rd["Origen"] == DBNull.Value ? null : rd["Origen"]?.ToString(),
                TipoDeComprobante = rd["tipodecomprobante"] == DBNull.Value ? null : rd["tipodecomprobante"]?.ToString(),
                Serie = rd["serie"] == DBNull.Value ? null : rd["serie"]?.ToString(),
                Folio = rd["folio"] == DBNull.Value ? null : rd["folio"]?.ToString(),
                Uuid = rd["uuid"]?.ToString() ?? "",
                MensajeMf = rd["mensaje_mf"] == DBNull.Value ? null : rd["mensaje_mf"]?.ToString(),
                XmlTimbrado = rd["xmltimbrado"] == DBNull.Value ? null : rd["xmltimbrado"]?.ToString(),
                Cancelada = Convert.ToBoolean(rd["cancelada"]),
                Saldo = rd["saldo"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(rd["saldo"]),
                Adicionales = rd["Adicionales"] == DBNull.Value ? null : rd["Adicionales"]?.ToString(),
                CreatedUtc = Convert.ToDateTime(rd["created_utc"])
            };
        }

        // -------- XML formateado (AJAX) --------
        [HttpGet]
        public IActionResult GetXmlFormatted(long id)
        {
            var row = ObtenerTimbradoPorId(id);
            if (row == null || string.IsNullOrWhiteSpace(row.XmlTimbrado))
                return Json(new { success = false, error = "XML no encontrado" });

            try
            {
                var xmlFormatted = FormatearXml(row.XmlTimbrado);
                return Json(new { success = true, xmlFormatted });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message, xmlRaw = row.XmlTimbrado });
            }
        }

        /// <summary>
        /// Convierte el XML timbrado a una representación JSON para lectura rápida.
        /// OJO: no es el JSON original del PAC, es una conversión de estructura.
        /// </summary>
        [HttpGet]
        public IActionResult GetXmlAsJson(long id)
        {
            var row = ObtenerTimbradoPorId(id);
            if (row == null || string.IsNullOrWhiteSpace(row.XmlTimbrado))
                return Json(new { success = false, error = "XML no encontrado" });

            try
            {
                // Normaliza y carga
                var xml = row.XmlTimbrado.Trim();

                var doc = new XmlDocument
                {
                    PreserveWhitespace = false
                };
                doc.LoadXml(xml);

                // Convierte a JSON indentado (atributos y nodos)
                var json = JsonConvert.SerializeXmlNode(doc, Newtonsoft.Json.Formatting.Indented, true);

                return Json(new { success = true, jsonFormatted = json });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        private string FormatearXml(string xml)
        {
            xml = xml.Trim();

            var doc = new XmlDocument { PreserveWhitespace = false };
            doc.LoadXml(xml);

            var sb = new StringBuilder();
            using var sw = new System.IO.StringWriter(sb);
            using var xw = new XmlTextWriter(sw)
            {
                Formatting = System.Xml.Formatting.Indented,
                Indentation = 2
            };

            doc.WriteTo(xw);
            xw.Flush();

            return sb.ToString();
        }


        public IActionResult GetAdicionalesFormatted(long id)
        {
            var row = ObtenerTimbradoPorId(id);
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

    }


}
