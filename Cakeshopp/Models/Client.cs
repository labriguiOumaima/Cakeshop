namespace project.Models
{
    public class Client : Utilisateur //heritage
    {
        public DateTime DateInscription { get; set; }
        // Son panier
        public List<Panier> Panier { get; set; } = new List<Panier>();
        public List<ArticleCommande> Articles { get; set; } = new List<ArticleCommande>();
    }
}
