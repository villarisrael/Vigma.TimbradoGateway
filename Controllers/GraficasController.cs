using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using System;
using System.Data;
using System.Text;
using Vigma.TimbradoGateway.Infrastructure;
using Vigma.TimbradoGateway.ViewModels.Timbrados;

namespace Vigma.TimbradoGateway.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GraficasController : ControllerBase
    {
        private readonly TimbradoDbContext _db;
        private readonly string _connectionString;
        private readonly ILogger<GraficasController> _logger;

        public GraficasController(
            TimbradoDbContext db,
            IConfiguration cfg,
            ILogger<GraficasController> logger)
        {
            _db = db;
            _connectionString = cfg.GetConnectionString("MySql")!;
            _logger = logger;
        }

        /// <summary>
        /// Obtiene datos de timbrados vs errores de los últimos 30 días
        /// </summary>
        /// <param name="tenantId">ID del tenant (opcional)</param>
        /// <returns>Datos formateados para gráficos</returns>
        [HttpGet("timbrados-vs-errores")]
        public async Task<IActionResult> GetTimbradosVsErrores()
        {
            try
            {
                // Opción 1: Usando Entity Framework con la vista
                var datos = await GetDatosDesdeVista();

                // Opción 2: Usando procedimiento almacenado (más eficiente)
                // var datos = await GetDatosDesdeProcedimiento(tenantId);

                if (!datos.Any())
                {
                    return Ok(new
                    {
                        fechas = Array.Empty<string>(),
                        timbrados = Array.Empty<int>(),
                        errores = Array.Empty<int>(),
                        mensaje = "No hay datos para el período seleccionado"
                    });
                }

                var resultado = new
                {
                    fechas = datos.Select(d => d.fecha_corta),
                    timbrados = datos.Select(d => d.timbrados),
                    errores = datos.Select(d => d.errores),
                    porcentajes = datos.Select(d => d.porcentaje_error),
                    resumen = new
                    {
                        totalTimbrados = datos.Sum(d => d.timbrados),
                        totalErrores = datos.Sum(d => d.errores),
                        promedioTimbrados = Math.Round(datos.Average(d => d.timbrados), 2),
                        promedioErrores = Math.Round(datos.Average(d => d.errores), 2),
                        porcentajeExito = CalcularPorcentajeExito(datos),
                        diasConDatos = datos.Count(d => d.timbrados > 0 || d.errores > 0),
                        mejorDia = datos.OrderByDescending(d => d.timbrados).FirstOrDefault(),
                        peorDia = datos.OrderByDescending(d => d.errores).FirstOrDefault()
                    },
                    metadata = new
                    {
                        totalDias = datos.Count,
                        fechaInicio = datos.Min(d => d.fecha).ToString("yyyy-MM-dd"),
                        fechaFin = datos.Max(d => d.fecha).ToString("yyyy-MM-dd"),
                       
                    }
                };

                return Ok(resultado);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener datos del gráfico " );
                return StatusCode(500, new { error = "Error interno al procesar la solicitud" });
            }
        }

        /// <summary>
        /// Obtiene datos usando Entity Framework con la vista
        /// </summary>
        private async Task<List<EstadisticaDiaria>> GetDatosDesdeVista()
        {
            // Si la vista no tiene filtro por tenant, necesitamos filtrar después
            var query = _db.Set<EstadisticaDiaria>()
                .FromSqlRaw("SELECT * FROM vw_TimbradosVsErrores_30dias")
                .AsQueryable();

            // Aquí asumimos que la vista tiene una columna tenant_id
            // Si no la tiene, necesitamos modificar la vista o filtrar en otra consulta
         

            return await query
                .OrderBy(e => e.fecha)
                .ToListAsync();
        }

        /// <summary>
        /// Obtiene datos usando procedimiento almacenado (más eficiente)
        /// </summary>
        private async Task<List<EstadisticaDiaria>> GetDatosDesdeProcedimiento(int? tenantId)
        {
            var datos = new List<EstadisticaDiaria>();

            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = new MySqlCommand("sp_EstadisticasGrafico30Dias", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;

                    // Parámetros
                    command.Parameters.AddWithValue("@p_FechaInicio", DBNull.Value);
                    command.Parameters.AddWithValue("@p_FechaFin", DBNull.Value);
                    command.Parameters.AddWithValue("@p_TenantId", tenantId ?? (object)DBNull.Value);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            datos.Add(new EstadisticaDiaria
                            {
                                fecha = reader.GetDateTime("fecha"),
                                fecha_corta = reader.GetString("fecha_corta"),
                               
                                dia_semana = reader.GetString("dia_semana"),
                               
                                timbrados = reader.GetInt32("timbrados"),
                                errores = reader.GetInt32("errores")
                            });
                        }
                    }
                }
            }

            return datos;
        }

        /// <summary>
        /// Obtiene datos usando ADO.NET puro (máximo rendimiento)
        /// </summary>
        [HttpGet("timbrados-vs-errores/raw")]
        public async Task<IActionResult> GetTimbradosVsErroresRaw([FromQuery] int? tenantId = null)
        {
            var datos = new List<EstadisticaDiaria>();

            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                string sql = @"
                    SELECT 
                        fecha,
                        fecha_corta,
                        timbrados,
                        errores,
                        porcentaje_error
                    FROM vw_TimbradosVsErrores_30dias
                    WHERE (@TenantId IS NULL OR tenant_id = @TenantId)
                    ORDER BY fecha";

                using (var command = new MySqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@TenantId", tenantId ?? (object)DBNull.Value);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            datos.Add(new EstadisticaDiaria
                            {
                                fecha = reader.GetDateTime("fecha"),
                                fecha_corta = reader.GetString("fecha_corta"),
                                timbrados = reader.GetInt32("timbrados"),
                                errores = reader.GetInt32("errores"),
                                porcentaje_error = reader.GetDecimal("porcentaje_error")
                            });
                        }
                    }
                }
            }

            return Ok(new
            {
                datos = datos,
                totalTimbrados = datos.Sum(d => d.timbrados),
                totalErrores = datos.Sum(d => d.errores)
            });
        }

        /// <summary>
        /// Obtiene resumen semanal de timbrados vs errores
        /// </summary>
        [HttpGet("timbrados-vs-errores/semanal")]
        public async Task<IActionResult> GetResumenSemanal([FromQuery] int? tenantId = null)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                string sql = @"
                    SELECT 
                        YEAR(fecha) AS anio,
                        WEEK(fecha) AS semana,
                        MIN(fecha) AS fecha_inicio,
                        MAX(fecha) AS fecha_fin,
                        SUM(timbrados) AS total_timbrados,
                        SUM(errores) AS total_errores,
                        CASE 
                            WHEN SUM(timbrados) > 0 
                            THEN ROUND((SUM(errores) * 100.0 / SUM(timbrados)), 2)
                            ELSE 0 
                        END AS porcentaje_error
                    FROM vw_TimbradosVsErrores_30dias
                    WHERE (@TenantId IS NULL OR tenant_id = @TenantId)
                    GROUP BY YEAR(fecha), WEEK(fecha)
                    ORDER BY anio DESC, semana DESC";

                using (var command = new MySqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@TenantId", tenantId ?? (object)DBNull.Value);

                    var resultado = new List<object>();
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            resultado.Add(new
                            {
                                semana = reader.GetInt32("semana"),
                                anio = reader.GetInt32("anio"),
                                fecha_inicio = reader.GetDateTime("fecha_inicio").ToString("dd/MM"),
                                fecha_fin = reader.GetDateTime("fecha_fin").ToString("dd/MM"),
                                periodo = $"Semana {reader.GetInt32("semana")} ({reader.GetDateTime("fecha_inicio"):dd/MM} - {reader.GetDateTime("fecha_fin"):dd/MM})",
                                timbrados = reader.GetInt32("total_timbrados"),
                                errores = reader.GetInt32("total_errores"),
                                porcentaje_error = reader.GetDecimal("porcentaje_error")
                            });
                        }
                    }

                    return Ok(resultado);
                }
            }
        }

        /// <summary>
        /// Exporta datos a CSV
        /// </summary>
        [HttpGet("timbrados-vs-errores/exportar")]
        public async Task<IActionResult> ExportarCSV([FromQuery] int? tenantId = null)
        {
            var datos = await GetDatosDesdeProcedimiento(tenantId);

            var csv = new StringBuilder();
            csv.AppendLine("Fecha,Fecha Corta,Día,Timbrados,Errores,Porcentaje Error");

            foreach (var item in datos)
            {
                csv.AppendLine($"{item.fecha:yyyy-MM-dd},{item.fecha_corta},{item.dia_semana},{item.timbrados},{item.errores},{item.porcentaje_error}");
            }

            byte[] bytes = Encoding.UTF8.GetBytes(csv.ToString());
            return File(bytes, "text/csv", $"timbrados_vs_errores_{DateTime.Now:yyyyMMdd}.csv");
        }

        /// <summary>
        /// Calcula el porcentaje de éxito (timbrados sin errores)
        /// </summary>
        private decimal CalcularPorcentajeExito(List<EstadisticaDiaria> datos)
        {
            var totalTimbrados = datos.Sum(d => d.timbrados);
            var totalErrores = datos.Sum(d => d.errores);

            if (totalTimbrados == 0) return 100;

            return Math.Round(100 - ((totalErrores * 100m) / totalTimbrados), 2);
        }
    }

    /// <summary>
    /// Modelo para los datos de estadística diaria
    /// </summary>
    public class EstadisticaDiaria
    {
        public DateTime fecha { get; set; }
        public string fecha_corta { get; set; } = string.Empty;
 
        public string dia_semana { get; set; } = string.Empty;
     
        public int timbrados { get; set; }
        public int errores { get; set; }
        public decimal porcentaje_error { get; set; }
        

        // Propiedades calculadas
        public string DiaSemanaAbreviado => dia_semana?.Substring(0, 3) ?? "";
        
    }
}