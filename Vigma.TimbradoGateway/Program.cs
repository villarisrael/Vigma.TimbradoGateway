using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using TimbradoGateway.Services;
using Vigma.TimbradoGateway.Infrastructure;
using Vigma.TimbradoGateway.Services;
using Microsoft.AspNetCore.Authentication.Cookies;


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

// Servicios del dominio
builder.Services.AddScoped<ITenantConfigService, TenantConfigService>();
builder.Services.AddScoped<IIniBuilderService, IniBuilderService>();

builder.Services.AddSingleton<OpenSslService>();
builder.Services.AddSingleton<CryptoService>();
builder.Services.AddSingleton<StorageBootstrapper>();
builder.Services.AddScoped<IniToMfRequestMapper>();

builder.Services.AddScoped<ITimbradoService, TimbradoService>();

builder.Services.AddScoped<IIniParserService, IniParserService>();

builder.Services.AddScoped<ITimbradoLogService, TimbradoLogService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(opt =>
    {
        opt.LoginPath = "/Account/Login";
        opt.AccessDeniedPath = "/Account/Denied";
        opt.ExpireTimeSpan = TimeSpan.FromHours(12);
        opt.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var boot = scope.ServiceProvider.GetRequiredService<StorageBootstrapper>();
    boot.EnsureFolders();
}
app.UseAuthentication();
app.UseAuthorization();

app.UseStaticFiles();

app.MapRazorPages();
app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
