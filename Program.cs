using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using TimbradoGateway.Services;
using Vigma.TimbradoGateway.Infrastructure;
using Vigma.TimbradoGateway.Infrastructure.Repositories;
using Vigma.TimbradoGateway.Services;
using Vigma.TimbradoGateway.Services.Alertas;   // ← NUEVO
using Vigma.TimbradoGateway.Services.Facturalo; // ← NUEVO (JSON->XML CFDI)

var builder = WebApplication.CreateBuilder(args);

// Razor Pages (Monitor)
builder.Services.AddRazorPages();

// API Controllers
builder.Services.AddControllers();

// MySQL (EF Core)
var cs = builder.Configuration.GetConnectionString("MySql");
builder.Services.AddDbContext<TimbradoDbContext>(opt =>
    opt.UseMySql(cs, ServerVersion.AutoDetect(cs)));

// HttpClient para MultiFacturas
builder.Services.AddHttpClient<IMultiFacturasClient, MultiFacturasClient>(http =>
{
    http.Timeout = TimeSpan.FromSeconds(60);
});

builder.Services.AddHttpClient<IMultiFacturasSaldoClient, MultiFacturasSaldoClient>(http =>
{
    http.Timeout = TimeSpan.FromSeconds(60);
});

builder.Services.AddHttpClient<IMultiFacturasApiClient, MultiFacturasApiClient>(http =>
{
    http.BaseAddress = new Uri("https://ws.multifacturas.com/api/");
    http.Timeout = TimeSpan.FromSeconds(60);
});

// FacturaLO PLUS — opciones (URLs Dev/Prod desde appsettings)
builder.Services.Configure<FacturaloOptions>(
    builder.Configuration.GetSection(FacturaloOptions.SectionName));

// FacturaLO PLUS — cliente SOAP (sin BaseAddress; la URL varía según producción/dev)
builder.Services.AddHttpClient<IFacturaloClient, FacturaloClient>(http =>
{
    http.Timeout = TimeSpan.FromSeconds(60);
});

// ── NUEVO: HttpClient para Firebase FCM ─────────────────────────────────────
builder.Services.AddHttpClient<IFcmService, FcmService>(http =>
{
    http.Timeout = TimeSpan.FromSeconds(30);
});

// Servicios del dominio — Timbrado
builder.Services.AddScoped<ITenantConfigService, TenantConfigService>();
builder.Services.AddScoped<IIniBuilderService, IniBuilderService>();
builder.Services.AddSingleton<OpenSslService>();
builder.Services.AddSingleton<CryptoService>();
builder.Services.AddSingleton<StorageBootstrapper>();
builder.Services.AddScoped<IniToMfRequestMapper>();
builder.Services.AddScoped<JsonMfToCfdiXmlBuilder>(); // ← FacturaLO: JSON MF -> CFDI XML 4.0
builder.Services.AddScoped<ITimbradoService, TimbradoService>();
builder.Services.AddScoped<IIniParserService, IniParserService>();
builder.Services.AddScoped<ITimbradoLogService, TimbradoLogService>();

// Cancelación CFDI — cliente SOAP (misma llamada que WSCancelarFactura40 de VB.NET)
builder.Services.AddHttpClient<IMultiFacturasCancelacionSoapClient, MultiFacturasCancelacionSoapClient>(http =>
{
    http.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddScoped<ICancelacionService, CancelacionService>();

// Consulta de Estado SAT (FacturaLO PLUS)
builder.Services.AddScoped<IConsultaEstadoSatService, ConsultaEstadoSatService>();

// PDF de factura genérica desde CFDI 4.0 (iText7)
builder.Services.AddScoped<Vigma.TimbradoGateway.Services.FacturaPdfService>();

// ── NUEVO: Servicios de Alertas ──────────────────────────────────────────────
builder.Services.AddScoped<IAlertTokenService, AlertTokenService>();
builder.Services.AddScoped<IAlertService, AlertService>();
builder.Services.AddScoped<IAlertDashboardService, AlertDashboardService>();

// Autenticación por cookies (Monitor interno)
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(opt =>
    {
        opt.LoginPath = "/Account/Login";
        opt.AccessDeniedPath = "/Account/Denied";
        opt.ExpireTimeSpan = TimeSpan.FromHours(12);
        opt.SlidingExpiration = true;
    });

builder.Services.AddScoped<IRepoUsuariosOficina, RepoUsuariosOficina>();

// ── NUEVO: Servicio para gestionar scope de tenants por cliente ──────────────
builder.Services.AddScoped<IClienteScopeService, ClienteScopeService>();

builder.Services.AddAuthorization();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var boot = scope.ServiceProvider.GetRequiredService<StorageBootstrapper>();
    boot.EnsureFolders();
}

app.UseAuthentication();
app.UseAuthorization();

// ✅ NUEVO: Middleware para redirigir clientes a su dashboard
app.Use(async (context, next) =>
{
    var user = context.User;
    var path = context.Request.Path.ToString().ToLower();

    // Si está autenticado como Cliente y NO está en /Cliente/*, redirigir
    if (user?.Identity?.IsAuthenticated == true &&
        user.IsInRole("Cliente") &&
        !path.StartsWith("/cliente/") &&
        path != "/account/logout")
    {
        context.Response.Redirect("/Cliente/Dashboard");
        return;
    }

    await next();
});

app.UseStaticFiles();
app.MapRazorPages();
app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
