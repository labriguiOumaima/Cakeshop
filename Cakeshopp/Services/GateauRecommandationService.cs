using Neo4j.Driver;

namespace Services
{
    public class GateauRecommendationService
    {
        private readonly Neo4jService _neo4jService;

        public GateauRecommendationService(Neo4jService neo4jService)
        {
            _neo4jService = neo4jService;
        }

        public async Task<List<RecommendationDto>> GetRecommendations(int gateauId)
        {
            using var session = _neo4jService.GetAsyncSession();

            var result = await session.RunAsync(
                "MATCH (client:Client)-[:A_PASSE]->(commande:Commande)-[:CONTIENT]->(gateau:Gateau {id: $id}) " +
                "MATCH (client)-[:A_PASSE]->(autre_commande:Commande)-[:CONTIENT]->(autre:Gateau) " +
                "WHERE autre.id <> gateau.id " +
                "  AND NOT autre.categorie CONTAINS \"Custom\" " +
                "RETURN autre.id, autre.nom, autre.categorie, COUNT(*) as nb_fois_achete " +
                "ORDER BY nb_fois_achete DESC LIMIT 5",
                new { id = gateauId }
            );

            var recommendations = new List<RecommendationDto>();
            await result.ForEachAsync(record =>
            {
                recommendations.Add(new RecommendationDto
                {
                    Id = record["autre.id"].As<int>(),
                    Nom = record["autre.nom"].As<string>(),
                    Categorie = record["autre.categorie"].As<string>(),
                    NbAchats = record["nb_fois_achete"].As<int>()
                });
            });

            return recommendations;
        }

        public async Task<List<RecommendationDto>> GetRecommendationsByCategory(int gateauId)
        {
            using var session = _neo4jService.GetAsyncSession();

            var result = await session.RunAsync(
                "MATCH (gateau:Gateau {id: $id}) " +
                "MATCH (autre:Gateau) " +
                "WHERE SPLIT(autre.categorie, \" - \")[0] = SPLIT(gateau.categorie, \" - \")[0] " +
                "  AND autre.id <> gateau.id " +
                "  AND NOT autre.categorie CONTAINS \"Custom\" " +
                "RETURN autre.id, autre.nom, autre.categorie, 0 as nb_fois_achete " +
                "LIMIT 5",
                new { id = gateauId }
            );

            var recommendations = new List<RecommendationDto>();
            await result.ForEachAsync(record =>
            {
                recommendations.Add(new RecommendationDto
                {
                    Id = record["autre.id"].As<int>(),
                    Nom = record["autre.nom"].As<string>(),
                    Categorie = record["autre.categorie"].As<string>(),
                    NbAchats = record["nb_fois_achete"].As<int>()
                });
            });

            return recommendations;
        }
    }

    public class RecommendationDto
    {
        public int Id { get; set; }
        public string Nom { get; set; }
        public string Categorie { get; set; }
        public int NbAchats { get; set; }
    }
}