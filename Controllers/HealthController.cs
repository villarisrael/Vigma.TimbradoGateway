using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using System;
using System.Text;
using Vigma.TimbradoGateway.Infrastructure;
using Vigma.TimbradoGateway.ViewModels.Timbrados;

[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    private readonly TimbradoDbContext _db;
    private readonly string _cs;

    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new { ok = true, service = "TimbradoGateway" });
    }

    public HealthController(TimbradoDbContext db, IConfiguration cfg)
    { _db = db;
        _cs = cfg.GetConnectionString("MySql")!;
    }


    [HttpGet("all")]
    public async Task<IActionResult> HealthAll(CancellationToken ct)
    {

        // 1) Estado de tu API (si este método responde, tu API está viva)
        var mine = new
        {
            online = true,
            status = 200,
            url = "/v1/timbrar/health",
            utc = DateTime.UtcNow
        };


        object db;
        try
        {
            // Opcional: confirma conexión (ping)
            var canConnect = await _db.Database.CanConnectAsync(ct);

            var promedio = ObtenerPromedioTimbradosPorHora();

            // Conteo (tu prueba)
            var tenants = await _db.Tenants.CountAsync(ct);

            var ultimos24 = ObtenerTotalTimbrados();
            var errores24 = ObtenerTotalerrores();

          

            decimal rateerrores = 0;

            if (ultimos24 > 0)
            {
                rateerrores = Math.Round(
                    (decimal)errores24 / ultimos24,
                    4 // número de decimales
                );
            }


            db = new
            {
                online = true,
                canConnect,
                tenants,
                promedio,
                ultimos24, errores24, rateerrores
            };
        }
        catch (Exception ex)
        {
            db = new
            {
                online = false,
                error = ex.Message
            };
        }



        // 2) Estado MultiFacturas (externo)
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            using var resp = await http.GetAsync("https://ws.multifacturas.com/api/", ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            var mf = new
            {
                online = true,
                status = (int)resp.StatusCode,
                url = "https://ws.multifacturas.com/api/",
                snippet = body.Length > 200 ? body[..200] : body
            };

            return Ok(new { mine, multifacturas = mf, database = db });
        }
        catch (Exception ex)
        {
            var mf = new
            {
                online = false,
                status = 0,
                url = "https://ws.multifacturas.com/api/",
                error = ex.Message
            };

            return Ok(new { mine, multifacturas = mf, database = db });
        }


    }





    // -------- LISTADO --------
    private  int ObtenerPromedioTimbradosPorHora()
    {
        // Rango: últimas 24 horas (UTC recomendado si tu created_utc es UTC)
        var fechaFinal = DateTime.UtcNow;
        var fechaInicio = fechaFinal.AddDays(-1);

        using var cn = new MySqlConnection(_cs);
        cn.Open();

        using var cmd = cn.CreateCommand();

        cmd.CommandText = @"
        SELECT IFNULL(AVG(x.total_hora), 0) AS promedio_por_hora
        FROM (
            SELECT 
                DATE_FORMAT(created_utc, '%Y-%m-%d %H:00:00') AS hora,
                COUNT(*) AS total_hora
            FROM timbrado_ok_log
            WHERE created_utc >= @fi
              AND created_utc <  @ff
            GROUP BY DATE_FORMAT(created_utc, '%Y-%m-%d %H:00:00')
        ) x;
    ";

        cmd.Parameters.AddWithValue("@fi", fechaInicio);
        cmd.Parameters.AddWithValue("@ff", fechaFinal);

        var obj = cmd.ExecuteScalar();
       // decimal promedio = Convert.ToDecimal(obj);
        int cuantos = Convert.ToInt32(obj);
        return  cuantos;
    }


    private  int ObtenerTotalTimbrados()
    {
        var fechaFinal = DateTime.UtcNow;
        var fechaInicio = fechaFinal.AddDays(-1);

        using var cn = new MySqlConnection(_cs);
        cn.Open();

        using var cmd = cn.CreateCommand();
        cmd.CommandText = @"
        SELECT COUNT(*)
        FROM timbrado_ok_log
        WHERE created_utc >= @fi
          AND created_utc <  @ff;
    ";

        cmd.Parameters.AddWithValue("@fi", fechaInicio);
        cmd.Parameters.AddWithValue("@ff", fechaFinal);

        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private int ObtenerTotalerrores()
    {
        var fechaFinal = DateTime.UtcNow;
        var fechaInicio = fechaFinal.AddDays(-1);

        using var cn = new MySqlConnection(_cs);
        cn.Open();

        using var cmd = cn.CreateCommand();
        cmd.CommandText = @"
        SELECT COUNT(*)
        FROM timbrado_error_log
        WHERE creado_utc >= @fi
          AND creado_utc <  @ff;
    ";

        cmd.Parameters.AddWithValue("@fi", fechaInicio);
        cmd.Parameters.AddWithValue("@ff", fechaFinal);

        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private int ObtenerGrafica30()
    {
        
        using var cn = new MySqlConnection(_cs);
        cn.Open();

        using var cmd = cn.CreateCommand();
        cmd.CommandText = @"SELECT * FROM 
       vw_timbradosvserrores_30dias;
    ";

       
        return Convert.ToInt32(cmd.ExecuteScalar());
    }


}
