using Vigma.TimbradoGateway.Infrastructure;
using Vigma.TimbradoGateway.Models;
using Vigma.TimbradoGateway.Utils;

using Microsoft.EntityFrameworkCore;
using Vigma.TimbradoGateway.Infrastructure;
using Vigma.TimbradoGateway.Models;
using Vigma.TimbradoGateway.Utils;

namespace Vigma.TimbradoGateway.Services;



public interface ITenantConfigService
{
    Task<(Tenant tenant, Certificado cert)> GetByApiKeyAsync(string apiKey, string rfcEmisor);

    Task<Tenant> GetTenantByIdAsync(long tenantId);
}


public class TenantConfigService : ITenantConfigService
{
    private readonly TimbradoDbContext _db;

    public TenantConfigService(TimbradoDbContext db) => _db = db;


    public async Task<Tenant> GetTenantByIdAsync(long tenantId)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId && t.Activo);
        if (tenant == null) throw new Exception("Tenant no encontrado o inactivo.");
        return tenant;
    }

    public async Task<(Tenant tenant, Certificado cert)> GetByApiKeyAsync(string apiKey, string rfcEmisor)
    {
        var keyHash = HashHelper.Sha256(apiKey);

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.ApiKeyHash == keyHash && t.Activo);
        if (tenant == null) throw new Exception("API Key inválida o tenant inactivo.");

        var cert = await _db.Certificados.FirstOrDefaultAsync(c => c.TenantId == tenant.Id && c.RFC == rfcEmisor);
        if (cert == null) throw new Exception("No hay certificado registrado para ese RFC emisor.");

        return (tenant, cert);
    }
}
