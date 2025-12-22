namespace project.Models
{
    public class Utilisateur
    {
        public int Id { get; set; }
        public string Nom { get; set; }
        public string Prenom { get; set; }
        public string Email { get; set; }
        public string Mdp { get; set; }
        public string Telephone { get; set; }
        public string Adresse { get; set; }
        public string Role { get; set; } = "client";
    }
}
