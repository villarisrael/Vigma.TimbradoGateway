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
using Vigma.TimbradoGateway.DTOs;
using Vigma.TimbradoGateway.Infrastructure;
using Vigma.TimbradoGateway.Services;
using Vigma.TimbradoGateway.ViewModels.Timbrados;
using Microsoft.AspNetCore.Hosting;
using System.Xml.Linq;
using Formatting = Newtonsoft.Json.Formatting;

namespace Vigma.TimbradoGateway.Controllers
{
    [Authorize]
    public class TimbradosController : Controller
    {
        private readonly string _cs;
        private readonly ICancelacionService _cancelSvc;
        private readonly FacturaPdfService _pdfSvc;
        private readonly IWebHostEnvironment _env;
        private readonly IFacturaloClient _facturalo;

        public TimbradosController(
            IConfiguration cfg,
            ICancelacionService cancelSvc,
            FacturaPdfService pdfSvc,
            IWebHostEnvironment env,
            IFacturaloClient facturalo)
        {
            _cs        = cfg.GetConnectionString("MySql")!;
            _cancelSvc = cancelSvc;
            _pdfSvc    = pdfSvc;
            _env       = env;
            _facturalo = facturalo;
        }

       
        [HttpGet]
        public IActionResult Index(
                    long? tenantId,
                    string? rfcEmisor,
                    string? uuid,
                    string? folio,
                    DateTime? fechaInicio,
                    DateTime? fechaFinal,
                    int page = 1,
                    int pageSize = 50)
                        {
                            page = page < 1 ? 1 : page;
                            pageSize = pageSize <= 0 ? 50 : pageSize;
                            if (pageSize > 200) pageSize = 200; // evita abusos

                            var vm = new TimbradoIndiceVM
                            {
                                TenantId = tenantId,
                                RfcEmisor = rfcEmisor,
                                Uuid = uuid,
                                Folio = folio,
                                FechaInicio = fechaInicio,
                                FechaFinal = fechaFinal,
                                Page = page,
                                PageSize = pageSize
                            };

                            vm.Tenants = ObtenerTenants(tenantId);

                            // ✅ La clave: obtener TOTAL y la página actual
                            var (rows, total) = ObtenerTimbradosPaginado(tenantId, rfcEmisor, uuid, folio, fechaInicio, fechaFinal, page, pageSize);

                            vm.Rows = rows;
                            vm.TotalRows = total;

                            vm.CanceladasCount = vm.Rows?.Count(r => r.Cancelada) ?? 0; // o calcula global si lo prefieres

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
        private List<TimbradoRowVM> ObtenerTimbrados(
            long? tenantId,
            string? rfcEmisor,
            string? uuid,
            string? folio,
            DateTime? fechaInicio,
            DateTime? fechaFinal)
        {
            var rows = new List<TimbradoRowVM>();

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
                // incluir todo el día final
                sql.Append(" AND created_utc < @ff ");
                cmd.Parameters.AddWithValue("@ff", fechaFinal.Value.Date.AddDays(1));
            }

            sql.Append(" ORDER BY created_utc DESC LIMIT 100; ");
            cmd.CommandText = sql.ToString();

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                rows.Add(new TimbradoRowVM
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

            var vm = new TimbradoDetalleVM
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

            if (!string.IsNullOrWhiteSpace(vm.XmlTimbrado))
                EnriquecerConXml(vm);

            return vm;
        }

        /// <summary>
        /// Parsea el XML timbrado y rellena los campos del comprobante en el VM.
        /// Usa XDocument con búsqueda por LocalName para ignorar prefijos de namespace.
        /// </summary>
        private static void EnriquecerConXml(TimbradoDetalleVM vm)
        {
            try
            {
                var doc = XDocument.Parse(vm.XmlTimbrado!);

                // ── Comprobante (nodo raíz) ────────────────────────────────────────
                var comp = doc.Descendants()
                              .FirstOrDefault(e => e.Name.LocalName.Equals("Comprobante", StringComparison.OrdinalIgnoreCase));

                if (comp != null)
                {
                    vm.Fecha              = comp.Attribute("Fecha")?.Value;
                    vm.Subtotal           = comp.Attribute("SubTotal")?.Value;
                    vm.Total              = comp.Attribute("Total")?.Value;
                    vm.FormaPago          = comp.Attribute("FormaPago")?.Value;
                    vm.MetodoPago         = comp.Attribute("MetodoPago")?.Value;
                    vm.Moneda             = comp.Attribute("Moneda")?.Value;
                    vm.TipoCambio         = comp.Attribute("TipoCambio")?.Value;
                    vm.LugarExpedicion    = comp.Attribute("LugarExpedicion")?.Value;
                    vm.NoCertificado      = comp.Attribute("NoCertificado")?.Value;
                    vm.CondicionesDePago  = comp.Attribute("CondicionesDePago")?.Value;
                }

                // ── Emisor ────────────────────────────────────────────────────────
                var emisor = doc.Descendants()
                                .FirstOrDefault(e => e.Name.LocalName.Equals("Emisor", StringComparison.OrdinalIgnoreCase));

                if (emisor != null)
                {
                    vm.NombreEmisor        = emisor.Attribute("Nombre")?.Value;
                    vm.RegimenFiscalEmisor = emisor.Attribute("RegimenFiscal")?.Value;
                }

                // ── Receptor ─────────────────────────────────────────────────────
                var receptor = doc.Descendants()
                                  .FirstOrDefault(e => e.Name.LocalName.Equals("Receptor", StringComparison.OrdinalIgnoreCase));

                if (receptor != null)
                {
                    vm.RfcReceptor              = receptor.Attribute("Rfc")?.Value;
                    vm.NombreReceptor           = receptor.Attribute("Nombre")?.Value;
                    vm.UsoCFDI                  = receptor.Attribute("UsoCFDI")?.Value;
                    vm.DomicilioFiscalReceptor  = receptor.Attribute("DomicilioFiscalReceptor")?.Value;
                    vm.RegimenFiscalReceptor    = receptor.Attribute("RegimenFiscalReceptor")?.Value;
                }

                // ── Sellos del Comprobante ────────────────────────────────────────
                if (comp != null)
                {
                    vm.SelloCFD   = comp.Attribute("Sello")?.Value;
                    vm.Certificado = comp.Attribute("Certificado")?.Value;
                }

                // ── TimbreFiscalDigital ───────────────────────────────────────────
                var tfd = doc.Descendants()
                             .FirstOrDefault(e => e.Name.LocalName.Equals("TimbreFiscalDigital", StringComparison.OrdinalIgnoreCase));

                if (tfd != null)
                {
                    vm.FechaTimbrado    = tfd.Attribute("FechaTimbrado")?.Value;
                    vm.NoCertificadoSAT = tfd.Attribute("NoCertificadoSAT")?.Value;
                    vm.SelloSAT         = tfd.Attribute("SelloSAT")?.Value;

                    // Cadena original del TFD v1.1: ||Version|UUID|FechaTimbrado|RfcProvCertif|NoCertificadoSAT||
                    // Si hay Leyenda (opcional) se inserta antes de NoCertificadoSAT
                    var tfdVersion   = tfd.Attribute("Version")?.Value ?? "";
                    var tfdUuid      = tfd.Attribute("UUID")?.Value ?? "";
                    var tfdFecha     = tfd.Attribute("FechaTimbrado")?.Value ?? "";
                    var tfdRfcProv   = tfd.Attribute("RfcProvCertif")?.Value ?? "";
                    var tfdLeyenda   = tfd.Attribute("Leyenda")?.Value;
                    var tfdNoCert    = tfd.Attribute("NoCertificadoSAT")?.Value ?? "";

                    vm.CadenaOriginalTFD = string.IsNullOrEmpty(tfdLeyenda)
                        ? $"||{tfdVersion}|{tfdUuid}|{tfdFecha}|{tfdRfcProv}|{tfdNoCert}||"
                        : $"||{tfdVersion}|{tfdUuid}|{tfdFecha}|{tfdRfcProv}|{tfdLeyenda}|{tfdNoCert}||";
                }
            }
            catch
            {
                // Si el XML está malformado, simplemente no enriquecemos el VM
            }
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

        // -------- DESCARGAR PDF de factura --------
        [HttpGet]
        public IActionResult DescargarPdf(long id)
        {
            var vm = ObtenerTimbradoPorId(id);
            if (vm == null) return NotFound();

            // Obtiene la ruta relativa del logo del tenant (ej: /logos/tenant_3.png)
            var logoRelPath = ObtenerLogoPath(vm.TenantId);

            byte[] bytes;
            try
            {
                bytes = _pdfSvc.GenerarPdf(vm, logoRelPath);
            }
            catch (Exception ex)
            {
                return BadRequest($"Error al generar el PDF: {ex.Message}");
            }

            var uuid     = string.IsNullOrWhiteSpace(vm.Uuid) ? id.ToString() : vm.Uuid;
            var fileName = $"Factura_{uuid}.pdf";
            return File(bytes, "application/pdf", fileName);
        }

        // -------- Helper: Logo del tenant --------
        private string? ObtenerLogoPath(long tenantId)
        {
            using var cn = new MySqlConnection(_cs);
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT logo_path FROM tenants WHERE id = @id LIMIT 1;";
            cmd.Parameters.AddWithValue("@id", tenantId);
            var result = cmd.ExecuteScalar();
            return result == DBNull.Value || result == null ? null : result.ToString();
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

        // -------- CONSULTAR ESTADO SAT (AJAX) — FacturaLO PLUS --------
        [HttpGet]
        public async Task<IActionResult> ConsultarEstadoSat(long id, CancellationToken ct)
        {
            var row = ObtenerTimbradoPorId(id);
            if (row == null)
                return Json(new { success = false, error = "Registro de timbrado no encontrado." });

            if (string.IsNullOrWhiteSpace(row.Uuid))
                return Json(new { success = false, error = "El registro no tiene UUID." });

            if (string.IsNullOrWhiteSpace(row.XmlTimbrado))
                return Json(new { success = false, error = "El registro no tiene XML timbrado para extraer RFC receptor y Total." });

            // 1) Extraer RfcReceptor y Total del XML
            string? rfcReceptor;
            string? total;
            try
            {
                (rfcReceptor, total) = ExtraerReceptorYTotalDelXml(row.XmlTimbrado);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = "No se pudo leer el XML timbrado: " + ex.Message });
            }

            if (string.IsNullOrWhiteSpace(rfcReceptor) || string.IsNullOrWhiteSpace(total))
                return Json(new { success = false, error = "El XML no contiene RFC receptor o Total." });

            // 2) Cargar apikey de FacturaLO + flag producción del tenant
            string? apikeyFl;
            bool produccion;
            try
            {
                (apikeyFl, produccion) = ObtenerCredencialesFacturaloDelTenant(row.TenantId);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = "No se pudo leer el tenant: " + ex.Message });
            }

            if (string.IsNullOrWhiteSpace(apikeyFl))
                return Json(new
                {
                    success = false,
                    error = $"Este tenant no tiene API Key de FacturaLO PLUS configurada para el ambiente " +
                            $"{(produccion ? "PRODUCCIÓN" : "PRUEBAS")}. Configúrala desde la pantalla de tenant."
                });

            // 3) Consultar el estado SAT vía FacturaLO
            try
            {
                var resp = await _facturalo.ConsultarEstadoSatAsync(
                    apikey:      apikeyFl!,
                    uuid:        row.Uuid!,
                    rfcEmisor:   row.RfcEmisor ?? "",
                    rfcReceptor: rfcReceptor!,
                    total:       total!,
                    produccion:  produccion,
                    ct:          ct);

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        codigoEstatus      = resp.CodigoEstatus,
                        esCancelable       = resp.EsCancelable,
                        estado             = resp.Estado,
                        estatusCancelacion = resp.EstatusCancelacion
                    },
                    contexto = new
                    {
                        uuid        = row.Uuid,
                        rfcEmisor   = row.RfcEmisor,
                        rfcReceptor = rfcReceptor,
                        total       = total,
                        ambiente    = produccion ? "Producción" : "Pruebas"
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = "Error consultando FacturaLO: " + ex.Message });
            }
        }

        // -------- Helpers Estado SAT --------
        private static (string? rfcReceptor, string? total) ExtraerReceptorYTotalDelXml(string xml)
        {
            var doc = XDocument.Parse(xml);

            string? receptor = doc.Descendants()
                                  .FirstOrDefault(e => e.Name.LocalName.Equals("Receptor", StringComparison.OrdinalIgnoreCase))
                                  ?.Attribute("Rfc")?.Value?.Trim();

            string? total = doc.Descendants()
                               .FirstOrDefault(e => e.Name.LocalName.Equals("Comprobante", StringComparison.OrdinalIgnoreCase))
                               ?.Attribute("Total")?.Value?.Trim();

            return (receptor, total);
        }

        private (string? apikey, bool produccion) ObtenerCredencialesFacturaloDelTenant(long tenantId)
        {
            using var cn = new MySqlConnection(_cs);
            cn.Open();

            using var cmd = cn.CreateCommand();
            cmd.CommandText = @"
                SELECT pac_apikey_facturalo, pac_apikey_facturalo_test, pac_produccion
                FROM tenants
                WHERE id = @id AND activo = 1
                LIMIT 1;";
            cmd.Parameters.AddWithValue("@id", tenantId);

            using var rd = cmd.ExecuteReader();
            if (!rd.Read()) return (null, false);

            var apikeyProd = rd["pac_apikey_facturalo"] == DBNull.Value
                ? null
                : rd["pac_apikey_facturalo"]?.ToString();

            var apikeyTest = rd["pac_apikey_facturalo_test"] == DBNull.Value
                ? null
                : rd["pac_apikey_facturalo_test"]?.ToString();

            var produccion = rd["pac_produccion"] != DBNull.Value && Convert.ToBoolean(rd["pac_produccion"]);

            // Selecciona la apikey correspondiente al ambiente activo
            var apikey = produccion ? apikeyProd : apikeyTest;

            return (apikey, produccion);
        }

        // -------- CANCELAR PRUEBA (solo en modo pruebas) --------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelarPrueba(long id, string motivo = "02")
        {
            // Obtener el registro
            var row = ObtenerTimbradoPorId(id);
            if (row == null)
            {
                TempData["Error"] = "Registro no encontrado.";
                return RedirectToAction(nameof(Index));
            }

            // Seguridad: solo si es pruebas y no está cancelado
            if (row.Cancelada)
            {
                TempData["Error"] = "El CFDI ya está marcado como cancelado.";
                return RedirectToAction(nameof(Index));
            }
           

            try
            {
                var req = new CancelacionRequest
                {
                    RfcEmisor = row.RfcEmisor ?? "",
                    Uuid = row.Uuid ?? "",
                    Motivo = motivo
                };

                var resp = await _cancelSvc.CancelarPorTenantIdAsync(row.TenantId, req);

                if (resp.Ok)
                    TempData["Exito"] = $"UUID {row.Uuid} cancelado correctamente. (Log #{resp.LogId})";
                else
                    TempData["Error"] = $"PAC rechazó la cancelación: [{resp.Codigo}] {resp.Mensaje}";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error al cancelar: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        private (List<TimbradoRowVM> rows, int total) ObtenerTimbradosPaginado(
    long? tenantId,
    string? rfcEmisor,
    string? uuid,
    string? folio,
    DateTime? fechaInicio,
    DateTime? fechaFinal,
    int page,
    int pageSize)
        {
            var q = ObtenerTimbrados(tenantId, rfcEmisor, uuid, folio, fechaInicio, fechaFinal);

           

            var total = q.Count();

            var rows = q.Skip((page - 1) * pageSize)
                        .Take(pageSize)
                        .Select(x => new TimbradoRowVM
                        {
                            Id = x.Id,
                            TenantId = x.TenantId,
                            RfcEmisor = x.RfcEmisor,
                            Origen = x.Origen,
                            TipoDeComprobante = x.TipoDeComprobante,
                            Serie = x.Serie,
                            Folio = x.Folio,
                            Uuid = x.Uuid,
                            MensajeMf = x.MensajeMf,
                            Cancelada = x.Cancelada,
                            Saldo = x.Saldo,
                            CreatedUtc = x.CreatedUtc
                        })
                        .ToList();

            return (rows, total);
        }

    }
    
   

}
