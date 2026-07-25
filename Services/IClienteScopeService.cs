using System.Security.Claims;

namespace Vigma.TimbradoGateway.Services
{
    /// <summary>
    /// Servicio para gestionar el scope de tenants permitidos para un usuario cliente.
    /// Extrae los TenantIds del claim del usuario y proporciona métodos para filtrar consultas.
    /// </summary>
    public interface IClienteScopeService
    {
        /// <summary>
        /// Extrae los IDs de tenants permitidos del claim principal del usuario.
        /// </summary>
        /// <param name="principal">El ClaimsPrincipal del usuario actual</param>
        /// <returns>List de IDs de tenants permitidos, o lista vacía si no hay claim</returns>
        List<long> GetAllowedTenantIds(ClaimsPrincipal principal);

        /// <summary>
        /// Verifica si un tenant específico está permitido para el usuario actual.
        /// </summary>
        /// <param name="principal">El ClaimsPrincipal del usuario actual</param>
        /// <param name="tenantId">ID del tenant a verificar</param>
        /// <returns>true si el tenant está permitido, false en caso contrario</returns>
        bool IsAllowed(ClaimsPrincipal principal, long tenantId);

        /// <summary>
        /// Obtiene el ClienteId asociado al usuario actual.
        /// </summary>
        /// <param name="principal">El ClaimsPrincipal del usuario actual</param>
        /// <returns>ClienteId (long?) si existe, null si no está presente</returns>
        long? GetClienteId(ClaimsPrincipal principal);

        /// <summary>
        /// Extrae el claim "TenantIds" como string.
        /// Ej: "1,2,3,4,5"
        /// </summary>
        /// <param name="principal">El ClaimsPrincipal del usuario actual</param>
        /// <returns>String con los IDs separados por coma, o string vacío</returns>
        string GetTenantIdsClaim(ClaimsPrincipal principal);
    }
}
