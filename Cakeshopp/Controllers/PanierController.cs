using Microsoft.AspNetCore.Mvc;
using project.Models;
using Services;
using System.Text.Json;
using Neo4j.Driver;

namespace project.Controllers
{
    public class PanierController : Controller
    {
        private readonly Neo4jService _neo4jService;
        private readonly ILogger<PanierController> _logger;

        public PanierController(Neo4jService neo4jService, ILogger<PanierController> logger)
        {
            _neo4jService = neo4jService;
            _logger = logger;
        }

        // GET: Panier/Index - Afficher le panier DEPUIS NEO4J
        public async Task<IActionResult> Index()
        {
            var panier = new List<ArticlePanier>();
            var hasCustom = false;

            try
            {
                var clientEmail = HttpContext.Session.GetString("ClientEmail");
                if (string.IsNullOrEmpty(clientEmail))
                {
                    return View(panier); // Panier vide si pas connecté
                }

                var session = _neo4jService.GetAsyncSession();

                // Récupérer le client
                var clientQuery = @"MATCH (c:Client {email: $email}) RETURN c.id as ClientId";
                var clientResult = await session.RunAsync(clientQuery, new { email = clientEmail });

                if (!await clientResult.FetchAsync())
                {
                    await session.DisposeAsync();
                    return View(panier);
                }

                var clientId = clientResult.Current["ClientId"].As<int>();

                // Récupérer le panier depuis Neo4j
                var panierQuery = @"
                    MATCH (p:Panier {clientId: $clientId})-[r:CONTIENT]->(g:Gateau)
                    RETURN g.id as GateauId,
                           g.nom as NomGateau,
                           g.prix as PrixGateau,
                           g.categorie as Categorie,
                           g.description as Description,
                           g.imagePath as ImagePath,
                           r.nombrePersonnes as NombrePersonnes,
                           r.quantite as Quantite,
                           r.prixBase as PrixBase";

                var result = await session.RunAsync(panierQuery, new { clientId });

                await foreach (var record in result)
                {
                    var nombrePersonnes = record["NombrePersonnes"].As<int>();
                    var prixBase = record["PrixBase"].As<decimal>();
                    var categorie = record["Categorie"].As<string>();
                    var description = record["Description"].As<string>() ?? "";
                    var isCustom = (categorie == "Custom");

                    if (isCustom)
                    {
                        hasCustom = true;
                    }

                    var imagePath = record["ImagePath"].As<string>() ?? "";

                    // Si custom, récupérer l'image du filling par son nom (stocké dans description)
                    if (isCustom && !string.IsNullOrEmpty(description))
                    {
                        var fillingImageQuery = @"
                            MATCH (s:SpongeFilling {nom: $fillingNom})
                            RETURN COALESCE(s.ImagePath, '') as ImagePath";

                        var fillingImageResult = await session.RunAsync(fillingImageQuery, new { fillingNom = description });
                        if (await fillingImageResult.FetchAsync())
                        {
                            imagePath = fillingImageResult.Current["ImagePath"].As<string>() ?? "";
                        }
                    }

                    var ratios = new Dictionary<int, decimal>
                    {
                        { 6, 1m },
                        { 10, 1.6m },
                        { 15, 2.4m },
                        { 20, 3.2m },
                        { 30, 4.8m }
                    };

                    // Si custom, prix = 0
                    var prixUnitaire = isCustom ? 0 : prixBase * (ratios.ContainsKey(nombrePersonnes) ? ratios[nombrePersonnes] : nombrePersonnes / 6m);

                    panier.Add(new ArticlePanier
                    {
                        GateauId = record["GateauId"].As<int>(),
                        NomGateau = record["NomGateau"].As<string>(),
                        NombrePersonnes = nombrePersonnes,
                        Quantite = record["Quantite"].As<int>(),
                        PrixBase = prixBase,
                        PrixUnitaire = prixUnitaire,
                        ImagePath = imagePath,
                        IsCustom = isCustom,
                        Gateau = new Gateau
                        {
                            Id = record["GateauId"].As<int>(),
                            Nom = record["NomGateau"].As<string>(),
                            Prix = record["PrixGateau"].As<decimal>(),
                            Categorie = categorie,
                            Description = description,
                            ImagePath = imagePath
                        }
                    });
                }

                await session.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur chargement panier: {ex.Message}");
            }

            // Passer le flag hasCustom à la vue
            ViewBag.HasCustom = hasCustom;
            return View(panier);
        }

        // POST: Panier/Ajouter - Ajouter au panier DANS NEO4J
        [HttpPost]
        public async Task<IActionResult> Ajouter(int gateauId, int quantite, int nombrePersonnes)
        {
            try
            {
                var clientEmail = HttpContext.Session.GetString("ClientEmail");
                if (string.IsNullOrEmpty(clientEmail))
                {
                    TempData["Error"] = "Vous devez être connecté pour ajouter au panier";
                    return RedirectToAction("Details", "Gateau", new { id = gateauId });
                }

                var session = _neo4jService.GetAsyncSession();

                // Récupérer le client
                var clientQuery = @"MATCH (c:Client {email: $email}) RETURN c.id as ClientId";
                var clientResult = await session.RunAsync(clientQuery, new { email = clientEmail });

                if (!await clientResult.FetchAsync())
                {
                    await session.DisposeAsync();
                    TempData["Error"] = "Client introuvable";
                    return RedirectToAction("Details", "Gateau", new { id = gateauId });
                }

                var clientId = clientResult.Current["ClientId"].As<int>();

                // Récupérer le gâteau
                var gateauQuery = @"
                    MATCH (g:Gateau)
                    WHERE g.id = $gateauId
                    RETURN g.nom as Nom, g.prix as Prix, g.imagePath as ImagePath";

                var gateauResult = await session.RunAsync(gateauQuery, new { gateauId });

                if (!await gateauResult.FetchAsync())
                {
                    await session.DisposeAsync();
                    TempData["Error"] = "Gâteau introuvable";
                    return RedirectToAction("Details", "Gateau", new { id = gateauId });
                }

                var record = gateauResult.Current;
                var nomGateau = record["Nom"].As<string>();
                var prixBase = record["Prix"].As<decimal>();

                // Ajouter au panier dans Neo4j
                var panierQuery = @"
                    MATCH (c:Client {id: $clientId})
                    MERGE (p:Panier {clientId: $clientId})
                    WITH c, p
                    MATCH (g:Gateau {id: $gateauId})
                    MERGE (p)-[r:CONTIENT {nombrePersonnes: $nombrePersonnes}]->(g)
                    ON CREATE SET r.quantite = $quantite, r.prixBase = $prixBase
                    ON MATCH SET r.quantite = r.quantite + $quantite
                    RETURN r.quantite as Quantite";

                await session.RunAsync(panierQuery, new
                {
                    clientId = clientId,
                    gateauId = gateauId,
                    nombrePersonnes = nombrePersonnes,
                    quantite = quantite,
                    prixBase = prixBase
                });

                // Compter les articles du panier
                var countQuery = @"
                    MATCH (p:Panier {clientId: $clientId})-[r:CONTIENT]->(g:Gateau)
                    RETURN sum(r.quantite) as Total";

                var countResult = await session.RunAsync(countQuery, new { clientId });
                await countResult.FetchAsync();
                var totalArticles = countResult.Current["Total"].As<int>();

                // Mettre à jour le compteur dans la session
                HttpContext.Session.SetString("PanierCount", totalArticles.ToString());

                await session.DisposeAsync();

                TempData["Success"] = "Ajouté au panier";
                TempData["GateauNom"] = nomGateau;

                _logger.LogInformation($"Gâteau {nomGateau} ajouté au panier. Total articles: {totalArticles}");

                return RedirectToAction("Details", "Gateau", new { id = gateauId });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur ajout panier: {ex.Message}");
                TempData["Error"] = "Erreur lors de l'ajout au panier";
                return RedirectToAction("Details", "Gateau", new { id = gateauId });
            }
        }

        // POST: Panier/Retirer - Retirer du panier DANS NEO4J
        [HttpPost]
        public async Task<IActionResult> Retirer(int gateauId, int nombrePersonnes)
        {
            try
            {
                var clientEmail = HttpContext.Session.GetString("ClientEmail");
                if (string.IsNullOrEmpty(clientEmail))
                {
                    return RedirectToAction(nameof(Index));
                }

                var session = _neo4jService.GetAsyncSession();

                // Récupérer le client
                var clientQuery = @"MATCH (c:Client {email: $email}) RETURN c.id as ClientId";
                var clientResult = await session.RunAsync(clientQuery, new { email = clientEmail });

                if (!await clientResult.FetchAsync())
                {
                    await session.DisposeAsync();
                    return RedirectToAction(nameof(Index));
                }

                var clientId = clientResult.Current["ClientId"].As<int>();

                // Supprimer du panier
                var deleteQuery = @"
                    MATCH (p:Panier {clientId: $clientId})-[r:CONTIENT {nombrePersonnes: $nombrePersonnes}]->(g:Gateau {id: $gateauId})
                    DELETE r";

                await session.RunAsync(deleteQuery, new { clientId, gateauId, nombrePersonnes });

                // Compter les articles restants
                var countQuery = @"
                    MATCH (p:Panier {clientId: $clientId})-[r:CONTIENT]->(g:Gateau)
                    RETURN sum(r.quantite) as Total";

                var countResult = await session.RunAsync(countQuery, new { clientId });

                int totalArticles = 0;
                if (await countResult.FetchAsync())
                {
                    totalArticles = countResult.Current["Total"].As<int>();
                }

                HttpContext.Session.SetString("PanierCount", totalArticles.ToString());

                await session.DisposeAsync();

                TempData["Success"] = "Article retiré du panier";

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur retrait panier: {ex.Message}");
                TempData["Error"] = "Erreur lors du retrait";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Panier/MettreAJourAjax
        [HttpPost]
        public async Task<IActionResult> MettreAJourAjax([FromBody] UpdatePanierRequest request)
        {
            try
            {
                var clientEmail = HttpContext.Session.GetString("ClientEmail");
                if (string.IsNullOrEmpty(clientEmail))
                {
                    return Json(new { success = false });
                }

                var session = _neo4jService.GetAsyncSession();

                // Récupérer le client
                var clientQuery = @"MATCH (c:Client {email: $email}) RETURN c.id as ClientId";
                var clientResult = await session.RunAsync(clientQuery, new { email = clientEmail });

                if (!await clientResult.FetchAsync())
                {
                    await session.DisposeAsync();
                    return Json(new { success = false });
                }

                var clientId = clientResult.Current["ClientId"].As<int>();

                // Supprimer l'ancienne relation
                var deleteQuery = @"
                    MATCH (p:Panier {clientId: $clientId})-[r:CONTIENT {nombrePersonnes: $ancienNombrePersonnes}]->(g:Gateau {id: $gateauId})
                    DELETE r";

                await session.RunAsync(deleteQuery, new
                {
                    clientId,
                    gateauId = request.GateauId,
                    ancienNombrePersonnes = request.AncienNombrePersonnes
                });

                // Récupérer le prix du gâteau
                var gateauQuery = @"MATCH (g:Gateau {id: $gateauId}) RETURN g.prix as Prix";
                var gateauResult = await session.RunAsync(gateauQuery, new { gateauId = request.GateauId });

                if (await gateauResult.FetchAsync())
                {
                    var prixBase = gateauResult.Current["Prix"].As<decimal>();

                    // Créer/Mettre à jour la nouvelle relation
                    var updateQuery = @"
                        MATCH (p:Panier {clientId: $clientId})
                        MATCH (g:Gateau {id: $gateauId})
                        MERGE (p)-[r:CONTIENT {nombrePersonnes: $nombrePersonnes}]->(g)
                        ON CREATE SET r.quantite = $quantite, r.prixBase = $prixBase
                        ON MATCH SET r.quantite = $quantite";

                    await session.RunAsync(updateQuery, new
                    {
                        clientId,
                        gateauId = request.GateauId,
                        nombrePersonnes = request.NombrePersonnes,
                        quantite = request.Quantite,
                        prixBase
                    });
                }

                // Compter les articles
                var countQuery = @"
                    MATCH (p:Panier {clientId: $clientId})-[r:CONTIENT]->(g:Gateau)
                    RETURN sum(r.quantite) as Total";

                var countResult = await session.RunAsync(countQuery, new { clientId });

                int totalArticles = 0;
                if (await countResult.FetchAsync())
                {
                    totalArticles = countResult.Current["Total"].As<int>();
                }

                HttpContext.Session.SetString("PanierCount", totalArticles.ToString());

                await session.DisposeAsync();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur mise à jour AJAX: {ex.Message}");
                return Json(new { success = false });
            }
        }
    }

    // Classe pour la requête AJAX
    public class UpdatePanierRequest
    {
        public int GateauId { get; set; }
        public int AncienNombrePersonnes { get; set; }
        public int NombrePersonnes { get; set; }
        public int Quantite { get; set; }
    }
}
