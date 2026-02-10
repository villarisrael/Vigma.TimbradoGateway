using Microsoft.EntityFrameworkCore;
using Vigma.TimbradoGateway.Models;
using Vigma.TimbradoGateway.Models.Logs;

namespace Vigma.TimbradoGateway.Infrastructure;

public class TimbradoDbContext : DbContext
{
    public TimbradoDbContext(DbContextOptions<TimbradoDbContext> opt) : base(opt) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Certificado> Certificados => Set<Certificado>();
    public DbSet<TimbradoOkLog> TimbradoOkLogs => Set<TimbradoOkLog>();
    public DbSet<TimbradoErrorLog> TimbradoErrorLogs => Set<TimbradoErrorLog>();

    public DbSet<UsuarioOficina> UsuariosOficina => Set<UsuarioOficina>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

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
        });

    }

}
