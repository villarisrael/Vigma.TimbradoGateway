using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using System.Xml;
using System.Xml.Linq;

namespace Vigma.TimbradoGateway.Pages.Cliente
{
    [Authorize(Roles = "Cliente")]
    public class TimbradoDetalleModel : PageModel
    {
        private readonly string _connectionString;

        public TimbradoDetalleModel(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("MySql") ?? "";
        }

        public TimbradoDetalleVM? Timbrado { get; set; }
        public string? Error { get; set; }

        public void OnGet(long id)
        {
            try
            {
                Timbrado = ObtenerTimbradoPorId(id);
                if (Timbrado == null)
                {
                    Error = "Timbrado no encontrado";
                }
                else if (!string.IsNullOrWhiteSpace(Timbrado.XmlTimbrado))
                {
                    // Formatear XML
                    Timbrado.XmlFormateado = FormatearXml(Timbrado.XmlTimbrado);
                    // Enriquecer con datos del XML
                    EnriquecerConXml(Timbrado);
                }
            }
            catch (Exception ex)
            {
                Error = $"Error al cargar timbrado: {ex.Message}";
            }
        }

        // ────────────────────────────────────────────────────────────────
        // Obtener timbrado por ID
        // ────────────────────────────────────────────────────────────────
        private TimbradoDetalleVM? ObtenerTimbradoPorId(long id)
        {
            using var cn = new MySqlConnection(_connectionString);
            cn.Open();

            using var cmd = cn.CreateCommand();
            cmd.CommandText = @"
                SELECT
                    id,
                    tenantid,
                    rfcemisor,
                    origen,
                    tipodecomprobante,
                    serie,
                    folio,
                    uuid,
                    codigo_mf,
                    mensaje_mf,
                    xmltimbrado,
                    cancelada,
                    saldo,
                    created_utc,
                    adicionales
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
                Origen = rd["origen"] == DBNull.Value ? null : rd["origen"]?.ToString(),
                TipoDeComprobante = rd["tipodecomprobante"] == DBNull.Value ? null : rd["tipodecomprobante"]?.ToString(),
                Serie = rd["serie"] == DBNull.Value ? null : rd["serie"]?.ToString(),
                Folio = rd["folio"] == DBNull.Value ? null : rd["folio"]?.ToString(),
                Uuid = rd["uuid"]?.ToString() ?? "",
                CodigoMf = rd["codigo_mf"] == DBNull.Value ? null : rd["codigo_mf"]?.ToString(),
                MensajeMf = rd["mensaje_mf"] == DBNull.Value ? null : rd["mensaje_mf"]?.ToString(),
                XmlTimbrado = rd["xmltimbrado"] == DBNull.Value ? null : rd["xmltimbrado"]?.ToString(),
                Cancelada = Convert.ToBoolean(rd["cancelada"]),
                Saldo = rd["saldo"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(rd["saldo"]),
                Adicionales = rd["adicionales"] == DBNull.Value ? null : rd["adicionales"]?.ToString(),
                CreatedUtc = Convert.ToDateTime(rd["created_utc"])
            };

            return vm;
        }

        // ────────────────────────────────────────────────────────────────
        // Formatear XML
        // ────────────────────────────────────────────────────────────────
        private static string FormatearXml(string xml)
        {
            try
            {
                var doc = new System.Xml.XmlDocument { PreserveWhitespace = false };
                doc.LoadXml(xml.Trim());

                var sb = new System.Text.StringBuilder();
                using var sw = new System.IO.StringWriter(sb);
                using var xw = new System.Xml.XmlTextWriter(sw)
                {
                    Formatting = System.Xml.Formatting.Indented,
                    Indentation = 2
                };

                doc.WriteTo(xw);
                xw.Flush();

                return sb.ToString();
            }
            catch
            {
                return xml;
            }
        }

        // ────────────────────────────────────────────────────────────────
        // Enriquecer con datos del XML
        // ────────────────────────────────────────────────────────────────
        private static void EnriquecerConXml(TimbradoDetalleVM vm)
        {
            try
            {
                var doc = XDocument.Parse(vm.XmlTimbrado!);

                // Comprobante
                var comp = doc.Descendants()
                              .FirstOrDefault(e => e.Name.LocalName.Equals("Comprobante", StringComparison.OrdinalIgnoreCase));
                if (comp != null)
                {
                    vm.Fecha = comp.Attribute("Fecha")?.Value;
                    vm.Subtotal = comp.Attribute("SubTotal")?.Value;
                    vm.Total = comp.Attribute("Total")?.Value;
                    vm.FormaPago = comp.Attribute("FormaPago")?.Value;
                    vm.MetodoPago = comp.Attribute("MetodoPago")?.Value;
                    vm.Moneda = comp.Attribute("Moneda")?.Value;
                    vm.LugarExpedicion = comp.Attribute("LugarExpedicion")?.Value;
                }

                // Emisor
                var emisor = doc.Descendants()
                                .FirstOrDefault(e => e.Name.LocalName.Equals("Emisor", StringComparison.OrdinalIgnoreCase));
                if (emisor != null)
                {
                    vm.NombreEmisor = emisor.Attribute("Nombre")?.Value;
                    vm.RegimenFiscalEmisor = emisor.Attribute("RegimenFiscal")?.Value;
                }

                // Receptor
                var receptor = doc.Descendants()
                                  .FirstOrDefault(e => e.Name.LocalName.Equals("Receptor", StringComparison.OrdinalIgnoreCase));
                if (receptor != null)
                {
                    vm.RfcReceptor = receptor.Attribute("Rfc")?.Value;
                    vm.NombreReceptor = receptor.Attribute("Nombre")?.Value;
                    vm.UsoCFDI = receptor.Attribute("UsoCFDI")?.Value;
                }

                // TimbreFiscalDigital
                var tfd = doc.Descendants()
                             .FirstOrDefault(e => e.Name.LocalName.Equals("TimbreFiscalDigital", StringComparison.OrdinalIgnoreCase));
                if (tfd != null)
                {
                    vm.FechaTimbrado = tfd.Attribute("FechaTimbrado")?.Value;
                    vm.NoCertificadoSAT = tfd.Attribute("NoCertificadoSAT")?.Value;
                    vm.SelloSAT = tfd.Attribute("SelloSAT")?.Value;
                }
            }
            catch
            {
                // Si el XML está malformado, continuar sin enriquecer
            }
        }
    }

    // ────────────────────────────────────────────────────────────────
    // ViewModel
    // ────────────────────────────────────────────────────────────────
    public class TimbradoDetalleVM
    {
        public long Id { get; set; }
        public long TenantId { get; set; }
        public string RfcEmisor { get; set; } = "";
        public string? Origen { get; set; }
        public string? TipoDeComprobante { get; set; }
        public string? Serie { get; set; }
        public string? Folio { get; set; }
        public string Uuid { get; set; } = "";
        public string? CodigoMf { get; set; }
        public string? MensajeMf { get; set; }
        public string? XmlTimbrado { get; set; }
        public string? XmlFormateado { get; set; }
        public bool Cancelada { get; set; }
        public decimal? Saldo { get; set; }
        public string? Adicionales { get; set; }
        public DateTime CreatedUtc { get; set; }

        // Datos del XML
        public string? Fecha { get; set; }
        public string? Subtotal { get; set; }
        public string? Total { get; set; }
        public string? FormaPago { get; set; }
        public string? MetodoPago { get; set; }
        public string? Moneda { get; set; }
        public string? LugarExpedicion { get; set; }
        public string? NombreEmisor { get; set; }
        public string? RegimenFiscalEmisor { get; set; }
        public string? RfcReceptor { get; set; }
        public string? NombreReceptor { get; set; }
        public string? UsoCFDI { get; set; }
        public string? FechaTimbrado { get; set; }
        public string? NoCertificadoSAT { get; set; }
        public string? SelloSAT { get; set; }
    }
}
