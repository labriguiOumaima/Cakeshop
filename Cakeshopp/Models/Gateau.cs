namespace project.Models
{
    public class Gateau
    {
        public int Id { get; set; }
        public string Nom { get; set; }
        public string Description { get; set; }
        public decimal Prix { get; set; }
        public int Stock { get; set; }
        public string Categorie { get; set; }
        public string ImagePath { get; set; }
    }
}
