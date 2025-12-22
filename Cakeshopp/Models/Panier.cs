namespace project.Models
{
    public class Panier
    {
        public int Id { get; set; }
        public int ClientId { get; set; }
        public int GateauId { get; set; }
        public int Quantite { get; set; }
        public DateTime DateAjout { get; set; }
        public string ImagePath { get; set; }  
    }
}
