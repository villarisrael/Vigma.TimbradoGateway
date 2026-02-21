using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Vigma.TimbradoGateway.Infrastructure;
using Vigma.TimbradoGateway.Models;
using Vigma.TimbradoGateway.Util;

namespace Vigma.TimbradoGateway.Pages.Monitor.Tenants.Certificados;

public class DetailsModel : PageModel
{
    private readonly TimbradoDbContext _db;

    // Información del Tenant
    public int TenantId { get; set; }
    public string TenantNombre { get; set; } = "";

    // Información del Certificado en BD
    public int CertId { get; set; }
    public string RFC { get; set; } = "";
    public string RazonSocial { get; set; } = "";
    public bool Activo { get; set; }
    public string CerPath { get; set; } = "";
    public string KeyPath { get; set; } = "";

    // Información extraída del archivo .cer
    public string? NoCertificado { get; set; }
    public DateTime? VigenciaInicioUtc { get; set; }
    public DateTime? VigenciaFinUtc { get; set; }
    public string? CertSubject { get; set; }
    public string? CertIssuer { get; set; }
    public bool CertificadoLeido { get; set; }
    public bool EsValido { get; set; }

    // Mensajes de estado
    public string? MensajeError { get; set; }
    public string? MensajeAdvertencia { get; set; }
    public string? MensajeInfo { get; set; }

    public DetailsModel(TimbradoDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> OnGetAsync(int tenantId, int certId)
    {
        TenantId = tenantId;
        CertId = certId;

        // Cargar Tenant
        var tenant = await _db.Tenants.FirstOrDefaultAsync(x => x.Id == tenantId);
        if (tenant == null)
            return NotFound($"Tenant {tenantId} no encontrado");

        TenantNombre = tenant.Nombre;

        // Cargar Certificado de BD
        var certificadoBD = await _db.Certificados
            .FirstOrDefaultAsync(x => x.Id == certId && x.TenantId == tenantId);

        if (certificadoBD == null)
            return NotFound($"Certificado {certId} no encontrado");

        // Datos básicos
        RFC = certificadoBD.RFC ?? "";
        Activo = certificadoBD.Activo;
        CerPath = certificadoBD.CerPath ?? "";
        KeyPath = certificadoBD.KeyPath ?? "";

        // Leer certificado y actualizar BD si es necesario
        if (!string.IsNullOrEmpty(CerPath))
        {
            try
            {
                var certInfo = CertificadoReader.LeerCertificado(CerPath);

                if (certInfo != null)
                {
                    CertificadoLeido = true;

                    // Actualizar propiedades de vista
                    RFC = certInfo.RFC;
                    RazonSocial = certInfo.RazonSocial;
                    NoCertificado = certInfo.NoCertificado;
                    VigenciaInicioUtc = certInfo.VigenciaInicio;
                    VigenciaFinUtc = certInfo.VigenciaFin;
                    CertSubject = certInfo.Subject;
                    CertIssuer = certInfo.Issuer;
                    EsValido = certInfo.EsValido;

                    // Actualizar BD si faltan datos
                    await ActualizarCertificadoEnBD(certificadoBD, certInfo);

                    // Validaciones
                    if (!certInfo.EsValido)
                    {
                        MensajeAdvertencia = "⚠️ El certificado ha expirado o aún no es válido";
                    }

                    if (!string.IsNullOrEmpty(certificadoBD.RFC) &&
                        !certificadoBD.RFC.Equals(certInfo.RFC, StringComparison.OrdinalIgnoreCase))
                    {
                        MensajeAdvertencia = $"⚠️ RFC no coincide. BD: {certificadoBD.RFC}, Certificado: {certInfo.RFC}";
                    }
                }
                else
                {
                    UsarDatosDeBD(certificadoBD);
                    MensajeError = $"No se pudo leer el archivo: {CerPath}";
                }
            }
            catch (Exception ex)
            {
                CertificadoLeido = false;
                MensajeError = $"Error al leer certificado: {ex.Message}";
                UsarDatosDeBD(certificadoBD);
            }
        }
        else
        {
            CertificadoLeido = false;
            MensajeError = "No hay ruta de certificado configurada";
            UsarDatosDeBD(certificadoBD);
        }

        return Page();
    }

    /// <summary>
    /// Actualiza el certificado en la BD con información del archivo .cer si faltan datos
    /// </summary>
    private async Task ActualizarCertificadoEnBD(Certificado certificadoBD, CertificadoInfo certInfo)
    {
        bool necesitaActualizar = false;
        var camposActualizados = new List<string>();

        // Actualizar NoCertificado si está vacío
        if (string.IsNullOrEmpty(certificadoBD.NoCertificado) &&
            !string.IsNullOrEmpty(certInfo.NoCertificado))
        {
            certificadoBD.NoCertificado = certInfo.NoCertificado;
            necesitaActualizar = true;
            camposActualizados.Add("NoCertificado");
        }

        // Actualizar VigenciaInicio si es null
        if (!certificadoBD.VigenciaInicio.HasValue)
        {
            certificadoBD.VigenciaInicio = certInfo.VigenciaInicio;
            necesitaActualizar = true;
            camposActualizados.Add("VigenciaInicio");
        }

        // Actualizar VigenciaFin si es null
        if (!certificadoBD.VigenciaFin.HasValue)
        {
            certificadoBD.VigenciaFin = certInfo.VigenciaFin;
            necesitaActualizar = true;
            camposActualizados.Add("VigenciaFin");
        }

        // Actualizar RFC si está vacío
        if (string.IsNullOrEmpty(certificadoBD.RFC) &&
            !string.IsNullOrEmpty(certInfo.RFC))
        {
            certificadoBD.RFC = certInfo.RFC;
            necesitaActualizar = true;
            camposActualizados.Add("RFC");
        }

        // Guardar cambios si hay actualizaciones
        if (necesitaActualizar)
        {
            try
            {
                certificadoBD.ActualizadoUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync();

                string campos = string.Join(", ", camposActualizados);
                MensajeInfo = $"✓ Campos actualizados en BD: {campos}";

#if DEBUG
                Console.WriteLine("════════════════════════════════════════════════");
                Console.WriteLine("✓ BASE DE DATOS ACTUALIZADA");
                Console.WriteLine($"Campos: {campos}");
                Console.WriteLine($"RFC: {certInfo.RFC}");
                Console.WriteLine($"No. Cert: {certInfo.NoCertificado}");
                Console.WriteLine($"Vigencia: {certInfo.VigenciaInicio:yyyy-MM-dd} - {certInfo.VigenciaFin:yyyy-MM-dd}");
                Console.WriteLine("════════════════════════════════════════════════");
#endif
            }
            catch (Exception ex)
            {
                MensajeAdvertencia = $"⚠️ No se pudo actualizar la BD: {ex.Message}";

#if DEBUG
                Console.WriteLine($"❌ Error al actualizar BD: {ex.Message}");
#endif
            }
        }
    }

    /// <summary>
    /// Usa los datos de la BD cuando no se puede leer el certificado
    /// </summary>
    private void UsarDatosDeBD(Certificado certificadoBD)
    {
        CertificadoLeido = false;
        NoCertificado = certificadoBD.NoCertificado;
        VigenciaInicioUtc = certificadoBD.VigenciaInicio;
        VigenciaFinUtc = certificadoBD.VigenciaFin;
        EsValido = false;
    }
}
