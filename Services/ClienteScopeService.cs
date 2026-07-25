using System.Security.Claims;

namespace Vigma.TimbradoGateway.Services
{
    /// <summary>
    /// Implementación de IClienteScopeService.
    /// Gestiona el scope de tenants permitidos para usuarios cliente.
    /// </summary>
    public class ClienteScopeService : IClienteScopeService
    {
        private const string TENANT_IDS_CLAIM = "TenantIds";
        private const string CLIENTE_ID_CLAIM = "ClienteId";

        /// <summary>
        /// Extrae los IDs de tenants permitidos del claim principal del usuario.
        /// </summary>
        public List<long> GetAllowedTenantIds(ClaimsPrincipal principal)
        {
            if (principal?.Identity?.IsAuthenticated != true)
                return new List<long>();

            var claim = principal.FindFirst(TENANT_IDS_CLAIM);
            if (claim == null || string.IsNullOrWhiteSpace(claim.Value))
                return new List<long>();

            // Parsear "1,2,3,4,5" a List<long>
            try
            {
                return claim.Value
                    .Split(',')
                    .Select(id => id.Trim())
                    .Where(id => !string.IsNullOrWhiteSpace(id) && long.TryParse(id, out _))
                    .Select(id => long.Parse(id))
                    .ToList();
            }
            catch
            {
                return new List<long>();
            }
        }

        /// <summary>
        /// Verifica si un tenant específico está permitido para el usuario actual.
        /// </summary>
        public bool IsAllowed(ClaimsPrincipal principal, long tenantId)
        {
            var allowed = GetAllowedTenantIds(principal);
            return allowed.Contains(tenantId);
        }

        /// <summary>
        /// Obtiene el ClienteId asociado al usuario actual.
        /// </summary>
        public long? GetClienteId(ClaimsPrincipal principal)
        {
            if (principal?.Identity?.IsAuthenticated != true)
                return null;

            var claim = principal.FindFirst(CLIENTE_ID_CLAIM);
            if (claim == null || string.IsNullOrWhiteSpace(claim.Value))
                return null;

            if (long.TryParse(claim.Value, out var clienteId))
                return clienteId;

            return null;
        }

        /// <summary>
        /// Extrae el claim "TenantIds" como string.
        /// </summary>
        public string GetTenantIdsClaim(ClaimsPrincipal principal)
        {
            if (principal?.Identity?.IsAuthenticated != true)
                return string.Empty;

            var claim = principal.FindFirst(TENANT_IDS_CLAIM);
            return claim?.Value ?? string.Empty;
        }
    }
}
