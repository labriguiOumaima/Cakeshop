using System.ComponentModel.DataAnnotations;

namespace project.Models
{
    // Modèle pour la CONNEXION
    public class LoginModel
    {
        [Required(ErrorMessage = "L'email est requis")]
        [EmailAddress(ErrorMessage = "Email invalide")]
        public string Email { get; set; }

        [Required(ErrorMessage = "Le mot de passe est requis")]
        public string Mdp { get; set; }
    }

    // Modèle pour l'INSCRIPTION
    public class RegisterModel
    {
        [Required(ErrorMessage = "Le nom est requis")]
        public string Nom { get; set; }

        [Required(ErrorMessage = "Le prénom est requis")]
        public string Prenom { get; set; }

        [Required(ErrorMessage = "L'email est requis")]
        [EmailAddress(ErrorMessage = "Email invalide")]
        public string Email { get; set; }

        [Required(ErrorMessage = "Le mot de passe est requis")]
        [MinLength(6, ErrorMessage = "Le mot de passe doit contenir au moins 6 caractères")]
        public string Mdp { get; set; }

        [Required(ErrorMessage = "Le téléphone est requis")]
        [RegularExpression(@"^[0-9]{10}$", ErrorMessage = "Le téléphone doit contenir exactement 10 chiffres")]
        public string Telephone { get; set; }
    }
}