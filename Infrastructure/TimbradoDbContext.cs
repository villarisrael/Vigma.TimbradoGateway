using Microsoft.EntityFrameworkCore;
using Vigma.TimbradoGateway.Controllers;
using Vigma.TimbradoGateway.Models;
using Vigma.TimbradoGateway.Models.Alertas;
using Vigma.TimbradoGateway.Models.Logs;

namespace Vigma.TimbradoGateway.Infrastructure;

public class TimbradoDbContext : DbContext
{
    public TimbradoDbContext(DbContextOptions<TimbradoDbContext> opt) : base(opt) { }

    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Certificado> Certificados => Set<Certificado>();
    public DbSet<TimbradoOkLog> TimbradoOkLogs => Set<TimbradoOkLog>();
    public DbSet<TimbradoErrorLog> TimbradoErrorLogs => Set<TimbradoErrorLog>();
    public DbSet<UsuarioOficina> UsuariosOficina => Set<UsuarioOficina>();
    public DbSet<CancelacionLog> CancelacionLogs => Set<CancelacionLog>();

    // ── NUEVO: Tablas de Alertas ─────────────────────────────────────────────
    public DbSet<FcmToken> FcmTokens => Set<FcmToken>();
    public DbSet<AlertLog> AlertLogs => Set<AlertLog>();

    // ── NUEVO: Vistas de Alertas ─────────────────────────────────────────────
    public DbSet<VwFcmToken> VwFcmTokens => Set<VwFcmToken>();
    public DbSet<VwAlertLog> VwAlertLogs => Set<VwAlertLog>();
    public DbSet<VwAlertasPorHora> VwAlertasPorHora => Set<VwAlertasPorHora>();
    public DbSet<VwAlertasPorDia> VwAlertasPorDia => Set<VwAlertasPorDia>();
    public DbSet<VwAlertasResumenTenant> VwAlertasResumenTenant => Set<VwAlertasResumenTenant>();
    public DbSet<VwAlertasPorEntidad> VwAlertasPorEntidad => Set<VwAlertasPorEntidad>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── NUEVO: Configuración de Cliente ──────────────────────────────────
        modelBuilder.Entity<Cliente>(e =>
        {
            e.ToTable("clientes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Nombre).HasColumnName("nombre").HasMaxLength(120).IsRequired();
            e.Property(x => x.Rfc).HasColumnName("rfc").HasMaxLength(13);
            e.Property(x => x.LogoPath).HasColumnName("logo_path").HasMaxLength(300);
            e.Property(x => x.Activo).HasColumnName("activo");
            e.Property(x => x.CreadoUtc).HasColumnName("creado_utc");

            // Relaciones
            e.HasMany(x => x.Tenants)
                .WithOne(x => x.Cliente)
                .HasForeignKey(x => x.ClienteId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasMany(x => x.UsuariosOficina)
                .WithOne(x => x.Cliente)
                .HasForeignKey(x => x.ClienteId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Tenant>(e =>
        {
            e.ToTable("tenants");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Nombre).HasColumnName("nombre");
            e.Property(x => x.Activo).HasColumnName("activo");
            e.Property(x => x.ApiKeyHash).HasColumnName("api_key_hash");
            e.Property(x => x.ApiKeyEnc).HasColumnName("api_key_enc");
            e.Property(x => x.ApiKeyLast4).HasColumnName("api_key_last4");
            e.Property(x => x.ApiKeyRotatedUtc).HasColumnName("api_key_rotated_utc");
            e.Property(x => x.PacUsuario).HasColumnName("pac_usuario");
            e.Property(x => x.PacPasswordEnc).HasColumnName("pac_password_enc");
            e.Property(x => x.PacProduccion).HasColumnName("pac_produccion");
            e.Property(x => x.actualizado_utc).HasColumnName("actualizado_utc");
            e.Property(x => x.creado_utc).HasColumnName("creado_utc");
            e.Property(x => x.LogoPath).HasColumnName("logo_path").HasMaxLength(500);
            e.Property(x => x.PacProveedor).HasColumnName("pac_proveedor").HasMaxLength(20);
            e.Property(x => x.PacApikeyFacturalo).HasColumnName("pac_apikey_facturalo").HasMaxLength(64);
            e.Property(x => x.PacApikeyFacturaloTest).HasColumnName("pac_apikey_facturalo_test").HasMaxLength(64);
            e.Property(x => x.ClienteId).HasColumnName("cliente_id"); // NUEVO
            e.Ignore(x => x.PacApikeyFacturaloActiva); // propiedad calculada, no se persiste
        });

        modelBuilder.Entity<Certificado>(e =>
        {
            e.ToTable("certificados");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.RFC).HasColumnName("rfc");
            e.Property(x => x.TipoCarga).HasColumnName("tipo_carga");
            e.Property(x => x.CerPath).HasColumnName("cer_path");
            e.Property(x => x.KeyPath).HasColumnName("key_path");
            e.Property(x => x.PfxPath).HasColumnName("pfx_path");
            e.Property(x => x.CerPemPath).HasColumnName("cer_pem_path");
            e.Property(x => x.KeyPemPath).HasColumnName("key_pem_path");
            e.Property(x => x.KeyPasswordEnc).HasColumnName("key_pass_enc");
            e.Property(x => x.NoCertificado).HasColumnName("no_certificado");
            e.Property(x => x.VigenciaInicio).HasColumnName("vigencia_inicio");
            e.Property(x => x.VigenciaFin).HasColumnName("vigencia_fin");
            e.Property(x => x.Activo).HasColumnName("activo");
            e.Property(x => x.CreadoUtc).HasColumnName("creado_utc");
            e.Property(x => x.ActualizadoUtc).HasColumnName("actualizado_utc");
            e.Property(x => x.ErrorLast).HasColumnName("error_last");
        });

        modelBuilder.Entity<UsuarioOficina>(e =>
        {
            e.ToTable("usuarios_oficina");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Usuario).IsUnique();
            e.Property(x => x.Usuario).HasMaxLength(60).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(255).IsRequired();
            e.Property(x => x.Rol).HasMaxLength(30).IsRequired();
            e.Property(x => x.ClienteId).HasColumnName("cliente_id"); // NUEVO
        });

        // ── NUEVO: Configuración de TimbradoOkLog ──────────────────────────────
        modelBuilder.Entity<TimbradoOkLog>(e =>
        {
            e.ToTable("timbrado_ok_log");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenantid");
            e.Property(x => x.RfcEmisor).HasColumnName("rfcemisor").HasMaxLength(13);
            e.Property(x => x.Origen).HasColumnName("origen").HasMaxLength(20);
            e.Property(x => x.TipoDeComprobante).HasColumnName("tipo_de_comprobante").HasMaxLength(1);
            e.Property(x => x.Serie).HasColumnName("serie").HasMaxLength(25);
            e.Property(x => x.Folio).HasColumnName("folio").HasMaxLength(50);
            e.Property(x => x.Uuid).HasColumnName("uuid").HasMaxLength(36).HasColumnType("varchar(36)"); // ✅ VARCHAR no GUID
            e.Property(x => x.codigo_Mf).HasColumnName("codigo_mf").HasMaxLength(20);
            e.Property(x => x.mensaje_Mf).HasColumnName("mensaje_mf").HasMaxLength(400);
            e.Property(x => x.xmlTimbrado).HasColumnName("xml_timbrado").HasColumnType("LONGTEXT");
            e.Property(x => x.Cancelada).HasColumnName("cancelada");
            e.Property(x => x.Saldo).HasColumnName("saldo").HasColumnType("decimal(18,2)");
            e.Property(x => x.Servidor).HasColumnName("servidor").HasMaxLength(80);
            e.Property(x => x.Ejecucion).HasColumnName("ejecucion").HasColumnType("decimal(10,2)");
            e.Property(x => x.Abortar).HasColumnName("abortar");
            e.Property(x => x.Pac).HasColumnName("pac").HasMaxLength(40);
            e.Property(x => x.MfProduccion).HasColumnName("mf_produccion").HasMaxLength(2);
            e.Property(x => x.VersionKit).HasColumnName("version_kit").HasMaxLength(30);
            e.Property(x => x.DuracionMs).HasColumnName("duracion_ms");
            e.Property(x => x.created_utc).HasColumnName("created_utc");
            e.Property(x => x.Adicionales).HasColumnName("adicionales");
        });

        modelBuilder.Entity<CancelacionLog>(e =>
        {
            e.ToTable("cancelacion_log");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.RfcEmisor).HasColumnName("rfc_emisor");
            e.Property(x => x.Uuid).HasColumnName("uuid");
            e.Property(x => x.Motivo).HasColumnName("motivo");
            e.Property(x => x.UuidSustitucion).HasColumnName("uuid_sustitucion");
            e.Property(x => x.Resultado).HasColumnName("resultado");
            e.Property(x => x.CodigoMf).HasColumnName("codigo_mf");
            e.Property(x => x.MensajeMf).HasColumnName("mensaje_mf");
            e.Property(x => x.JsonEnviado).HasColumnName("json_enviado");
            e.Property(x => x.RawPac).HasColumnName("raw_pac");
            e.Property(x => x.MfProduccion).HasColumnName("mf_produccion");
            e.Property(x => x.DuracionMs).HasColumnName("duracion_ms");
            e.Property(x => x.CreadoUtc).HasColumnName("creado_utc");
        });

        modelBuilder.Entity<EstadisticaDiaria>(entity =>
        {
            entity.HasNoKey();
            entity.ToView("vw_TimbradosVsErrores_30dias");
            entity.Property(e => e.fecha).HasColumnName("fecha");
            entity.Property(e => e.fecha_corta).HasColumnName("fecha_corta");
            entity.Property(e => e.timbrados).HasColumnName("timbrados");
            entity.Property(e => e.errores).HasColumnName("errores");
            entity.Property(e => e.porcentaje_error).HasColumnName("porcentaje_error");
        });

        // ── NUEVO: Configuración vistas de Alertas ───────────────────────────

        modelBuilder.Entity<VwFcmToken>(e =>
        {
            e.HasNoKey();
            e.ToView("vw_fcm_tokens");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.TenantNombre).HasColumnName("tenant_nombre");
            e.Property(x => x.EntidadId).HasColumnName("entidad_id");
            e.Property(x => x.EntidadNombre).HasColumnName("entidad_nombre");
            e.Property(x => x.TokenPreview).HasColumnName("token_preview");
            e.Property(x => x.Activo).HasColumnName("activo");
            e.Property(x => x.CreadoUtc).HasColumnName("creado_utc");
            e.Property(x => x.ActualizadoUtc).HasColumnName("actualizado_utc");
            e.Property(x => x.DiasSinActualizar).HasColumnName("dias_sin_actualizar");
        });

        modelBuilder.Entity<VwAlertLog>(e =>
        {
            e.HasNoKey();
            e.ToView("vw_alert_logs");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.TenantNombre).HasColumnName("tenant_nombre");
            e.Property(x => x.EntidadId).HasColumnName("entidad_id");
            e.Property(x => x.EntidadNombre).HasColumnName("entidad_nombre");
            e.Property(x => x.Origin).HasColumnName("origin");
            e.Property(x => x.Title).HasColumnName("title");
            e.Property(x => x.Message).HasColumnName("message");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.FirebaseMsgId).HasColumnName("firebase_msg_id");
            e.Property(x => x.ErrorDetail).HasColumnName("error_detail");
            e.Property(x => x.SentAt).HasColumnName("sent_at");
           
        });

        modelBuilder.Entity<VwAlertasPorHora>(e =>
        {
            e.HasNoKey();
            e.ToView("vw_alertas_por_hora");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.TenantNombre).HasColumnName("tenant_nombre");
            e.Property(x => x.Fecha).HasColumnName("fecha");
            e.Property(x => x.Hora).HasColumnName("hora");
            e.Property(x => x.FechaHora).HasColumnName("fecha_hora");
            e.Property(x => x.Total).HasColumnName("total");
            e.Property(x => x.Enviados).HasColumnName("enviados");
            e.Property(x => x.Fallidos).HasColumnName("fallidos");
            e.Property(x => x.PctError).HasColumnName("pct_error");
        });

        modelBuilder.Entity<VwAlertasPorDia>(e =>
        {
            e.HasNoKey();
            e.ToView("vw_alertas_por_dia");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.TenantNombre).HasColumnName("tenant_nombre");
            e.Property(x => x.Fecha).HasColumnName("fecha");
            e.Property(x => x.FechaCorta).HasColumnName("fecha_corta");
            e.Property(x => x.Total).HasColumnName("total");
            e.Property(x => x.Enviados).HasColumnName("enviados");
            e.Property(x => x.Fallidos).HasColumnName("fallidos");
            e.Property(x => x.PctError).HasColumnName("pct_error");
        });

        modelBuilder.Entity<VwAlertasResumenTenant>(e =>
        {
            e.HasNoKey();
            e.ToView("vw_alertas_resumen_tenant");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.TenantNombre).HasColumnName("tenant_nombre");
            e.Property(x => x.TotalAlertas).HasColumnName("total_alertas");
            e.Property(x => x.TotalEnviadas).HasColumnName("total_enviadas");
            e.Property(x => x.TotalFallidas).HasColumnName("total_fallidas");
            e.Property(x => x.PctError).HasColumnName("pct_error");
            e.Property(x => x.AlertasHoy).HasColumnName("alertas_hoy");
            e.Property(x => x.EnviadasHoy).HasColumnName("enviadas_hoy");
            e.Property(x => x.FallidasHoy).HasColumnName("fallidas_hoy");
            e.Property(x => x.TokensActivos).HasColumnName("tokens_activos");
            e.Property(x => x.UltimaAlertaUtc).HasColumnName("ultima_alerta_utc");
        });

        modelBuilder.Entity<VwAlertasPorEntidad>(e =>
        {
            e.HasNoKey();
            e.ToView("vw_alertas_por_entidad");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.TenantNombre).HasColumnName("tenant_nombre");
            e.Property(x => x.EntidadId).HasColumnName("entidad_id");
            e.Property(x => x.EntidadNombre).HasColumnName("entidad_nombre");
            e.Property(x => x.Total).HasColumnName("total");
            e.Property(x => x.Enviados).HasColumnName("enviados");
            e.Property(x => x.Fallidos).HasColumnName("fallidos");
            e.Property(x => x.UltimaAlertaUtc).HasColumnName("ultima_alerta_utc");
        });
    }
}
