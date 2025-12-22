namespace project.Models
{
    public class DemandeCustom
    {
        public int Id { get; set; }
        public int ClientId { get; set; }
        public string Categorie { get; set; } = ""; // "Cake", "Cupcakes", etc.
        public string SpongeFilling { get; set; } = ""; // "Rainbow Cake +£10.00 extra", "Chocolate Cherry Cake"
      
        public int Quantite { get; set; }
    

        public string Message { get; set; } = ""; // Message spécial / instructions
        public string Statut { get; set; } = "En attente de prix";
        public DateTime DateCreation { get; set; }
        public decimal? Prix { get; set; } // Null au début, rempli par admin

        // Infos client (pour affichage)
        public string? ClientNom { get; set; }
        public string? ClientPrenom { get; set; }
        public string? ClientEmail { get; set; }
        public string? ClientTelephone { get; set; }
    }
}