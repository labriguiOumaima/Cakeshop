namespace project.Models
{
    public class DemandeCustomRequest
    {
        public string Categorie { get; set; } = "";
        public string SpongeFilling { get; set; } = "";
        public int Quantite { get; set; }
        public string Message { get; set; } = "";
    }
}