namespace Vigma.TimbradoGateway.Models.Facturalo
{
    /// <summary>
    /// Respuesta del servicio FacturaLO PLUS — consultarEstadoSAT.
    /// Refleja el estado del comprobante ante el SAT al momento de la consulta.
    /// </summary>
    public class consultarEstadoSATResponse
    {
        /// <summary>
        /// Código de respuesta del servicio.
        /// </summary>
        public string? CodigoEstatus { get; set; }

        /// <summary>
        /// En caso de éxito en la consulta puede tomar uno de los valores:
        /// <list type="bullet">
        ///   <item><description>Cancelable con aceptación</description></item>
        ///   <item><description>No cancelable</description></item>
        ///   <item><description>Cancelable sin aceptación</description></item>
        /// </list>
        /// </summary>
        public string? EsCancelable { get; set; }

        /// <summary>
        /// En caso de éxito en la consulta puede tomar uno de los valores:
        /// <list type="bullet">
        ///   <item><description>Vigente</description></item>
        ///   <item><description>Cancelado</description></item>
        /// </list>
        /// </summary>
        public string? Estado { get; set; }

        /// <summary>
        /// En caso de éxito en la consulta puede tomar uno de los valores:
        /// <list type="bullet">
        ///   <item><description>(null)</description></item>
        ///   <item><description>En proceso</description></item>
        ///   <item><description>Plazo vencido</description></item>
        ///   <item><description>Solicitud rechazada</description></item>
        ///   <item><description>Cancelado sin aceptación</description></item>
        ///   <item><description>Cancelado con aceptación</description></item>
        /// </list>
        /// </summary>
        public string? EstatusCancelacion { get; set; }
    }
}
