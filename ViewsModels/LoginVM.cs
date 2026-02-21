
using System.ComponentModel.DataAnnotations;
namespace Vigma.TimbradoGateway.ViewsModels
{


    public class LoginVM
    {
        [Required(ErrorMessage = "Usuario requerido")]
        public string Usuario { get; set; } = "";

        [Required(ErrorMessage = "Contraseña requerida")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";

        public bool Recordarme { get; set; }
        public string? ReturnUrl { get; set; }
    }
}
