namespace project.Models
{
    public class SpongeFilling
    {
        public int Id { get; set; }
        public string Nom { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal PrixExtra { get; set; }  
        public string Categorie { get; set; } = "";
        public string ImagePath { get; set; } = "";
    }
}