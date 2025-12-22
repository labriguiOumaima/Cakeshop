using Microsoft.AspNetCore.Mvc;
using project.Models;
using Services;
using Neo4j.Driver;

namespace project.Controllers
{
    public class CustomController : Controller
    {
        private readonly Neo4jService _neo4jService;
        private readonly ILogger<CustomController> _logger;

        public CustomController(Neo4jService neo4jService, ILogger<CustomController> logger)
        {
            _neo4jService = neo4jService;
            _logger = logger;
        }

        // GET: Custom/Index - Liste des categories depuis Neo4j
        public async Task<IActionResult> Index()
        {
            var categories = new List<CustomCategory>();

            try
            {
                var session = _neo4jService.GetAsyncSession();

                var query = @"
                    MATCH (c:CustomCategory)
                    RETURN c.id as Id, 
                           c.nom as Nom, 
                           c.description as Description, 
                           c.ImagePath as ImagePath
                    ORDER BY c.id";

                var result = await session.RunAsync(query);

                await foreach (var record in result)
                {
                    categories.Add(new CustomCategory
                    {
                        Id = record["Id"].As<int>(),
                        Nom = record["Nom"].As<string>(),
                        Description = record["Description"].As<string>(),
                        ImagePath = record["ImagePath"].As<string>()
                    });
                }

                await session.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur chargement categories custom: {ex.Message}");
            }

            return View(categories);
        }

        // GET: Custom/Personnaliser?categorie=Cake
        public async Task<IActionResult> Personnaliser(string categorie)
        {
            try
            {
                var session = _neo4jService.GetAsyncSession();

                // Vérifier que la categorie existe et récupérer son image
                var categorieQuery = @"
                    MATCH (c:CustomCategory {nom: $categorie})
                    RETURN c.nom as Nom,
                           COALESCE(c.ImagePath, '') as ImagePath";

                var categorieResult = await session.RunAsync(categorieQuery, new { categorie });

                if (!await categorieResult.FetchAsync())
                {
                    await session.DisposeAsync();
                    TempData["Error"] = "Catégorie introuvable";
                    return RedirectToAction("Index");
                }

                var categorieRecord = categorieResult.Current;
                var categorieImagePath = categorieRecord["ImagePath"].As<string>() ?? "";

                // Charger les fillings pour cette catégorie
                var fillingsQuery = @"
                    MATCH (s:SpongeFilling {categorie: $categorie})
                    RETURN s.id as Id,
                           s.nom as Nom,
                           s.description as Description,
                           s.prixExtra as PrixExtra,
                           COALESCE(s.ImagePath, '') as ImagePath
                    ORDER BY s.id";

                var fillingsResult = await session.RunAsync(fillingsQuery, new { categorie });
                var fillings = new List<SpongeFilling>();

                await foreach (var record in fillingsResult)
                {
                    fillings.Add(new SpongeFilling
                    {
                        Id = record["Id"].As<int>(),
                        Nom = record["Nom"].As<string>(),
                        Description = record["Description"].As<string>(),
                        PrixExtra = record["PrixExtra"].As<decimal>(),
                        ImagePath = record["ImagePath"].As<string>() ?? ""
                    });
                }

                await session.DisposeAsync();

                ViewBag.Categorie = categorie;
                ViewBag.CategorieImagePath = categorieImagePath;
                ViewBag.Fillings = fillings;

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur: {ex.Message}");
                return RedirectToAction("Index");
            }
        }

        // GET: Custom/CreateOrder - Créer une demande custom ET l'ajouter au panier
        [HttpPost]
        public async Task<IActionResult> CreateOrder([FromBody] DemandeCustomRequest request)
        {
            try
            {
                var clientEmail = HttpContext.Session.GetString("ClientEmail");
                if (string.IsNullOrEmpty(clientEmail))
                {
                    return Json(new { success = false, message = "Vous devez être connecté pour faire une demande custom" });
                }

                var session = _neo4jService.GetAsyncSession();

                // Récupérer le client
                var clientQuery = @"MATCH (c:Client {email: $email}) RETURN c.id as ClientId";
                var clientResult = await session.RunAsync(clientQuery, new { email = clientEmail });

                if (!await clientResult.FetchAsync())
                {
                    await session.DisposeAsync();
                    return Json(new { success = false, message = "Client introuvable" });
                }

                var clientId = clientResult.Current["ClientId"].As<int>();

                // Générer un ID unique pour la demande
                var demandeIdQuery = @"
                    OPTIONAL MATCH (d:DemandeCustom)
                    WITH MAX(d.id) as maxId
                    RETURN CASE WHEN maxId IS NULL THEN 1 ELSE maxId + 1 END as NextId";

                var demandeIdResult = await session.RunAsync(demandeIdQuery);
                await demandeIdResult.FetchAsync();
                var demandeId = demandeIdResult.Current["NextId"].As<int>();

                // Créer la demande custom
                var createDemandeQuery = @"
                    MATCH (c:Client {id: $clientId})
                    CREATE (d:DemandeCustom {
                        id: $demandeId,
                        categorie: $categorie,
                        spongeFilling: $spongeFilling,
                        quantite: $quantite,
                        message: $message,
                        statut: 'En attente de prix',
                        dateCreation: datetime()
                    })
                    CREATE (c)-[:A_DEMANDE]->(d)
                    RETURN d.id as DemandeId";

                await session.RunAsync(createDemandeQuery, new
                {
                    clientId = clientId,
                    demandeId = demandeId,
                    categorie = request.Categorie,
                    spongeFilling = request.SpongeFilling,
                    quantite = request.Quantite,
                    message = request.Message ?? ""
                });

                // Générer ID gâteau custom
                var gateauCustomIdQuery = @"
                    OPTIONAL MATCH (g:Gateau)
                    WITH MAX(g.id) as maxId
                    RETURN CASE WHEN maxId IS NULL THEN 1 ELSE maxId + 1 END as NextId";

                var gateauIdResult = await session.RunAsync(gateauCustomIdQuery);
                await gateauIdResult.FetchAsync();
                var gateauCustomId = gateauIdResult.Current["NextId"].As<int>();

                // Créer gâteau custom temporaire
                var nomGateau = $"Custom - {request.Categorie}";
                var descriptionGateau = !string.IsNullOrEmpty(request.SpongeFilling) && request.SpongeFilling != "N/A"
                    ? $"{request.SpongeFilling}"
                    : "Gateau personnalise";

                var createGateauQuery = @"
                    CREATE (g:Gateau {
                        id: $gateauId,
                        nom: $nom,
                        description: $description,
                        prix: 0,
                        stock: 1,
                        categorie: 'Custom',
                        isCustom: true,
                        demandeCustomId: $demandeId,
                        ImagePath: '',
                        imagePath: ''
                    })";

                await session.RunAsync(createGateauQuery, new
                {
                    gateauId = gateauCustomId,
                    nom = nomGateau,
                    description = descriptionGateau,
                    demandeId = demandeId
                });

                // Ajouter au panier
                var panierQuery = @"
                    MATCH (c:Client {id: $clientId})
                    MERGE (p:Panier {clientId: $clientId})
                    WITH c, p
                    MATCH (g:Gateau {id: $gateauId})
                    MERGE (p)-[r:CONTIENT {nombrePersonnes: 1}]->(g)
                    ON CREATE SET r.quantite = $quantite, r.prixBase = 0
                    ON MATCH SET r.quantite = r.quantite + $quantite
                    RETURN r.quantite as Quantite";

                await session.RunAsync(panierQuery, new
                {
                    clientId = clientId,
                    gateauId = gateauCustomId,
                    quantite = request.Quantite
                });

                // Compter articles panier
                var countQuery = @"
                    MATCH (p:Panier {clientId: $clientId})-[r:CONTIENT]->(g:Gateau)
                    RETURN sum(r.quantite) as Total";

                var countResult = await session.RunAsync(countQuery, new { clientId });
                await countResult.FetchAsync();
                var totalArticles = countResult.Current["Total"].As<int>();

                // Mettre à jour session
                HttpContext.Session.SetString("PanierCount", totalArticles.ToString());

                await session.DisposeAsync();

                _logger.LogInformation($"Demande custom #{demandeId} créée et ajoutée au panier pour {clientEmail}");

                // Retourne les infos nécessaires au toast et à la mise à jour du panier
                return Json(new
                {
                    success = true,
                    demandeId = demandeId,
                    gateauNom = nomGateau,
                    totalArticles = totalArticles
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur création demande custom: {ex.Message}");
                _logger.LogError($"Stack trace: {ex.StackTrace}");
                return Json(new { success = false, message = "Erreur lors de la création" });
            }
        }

        // GET: Custom/Confirmation/{id}
        public async Task<IActionResult> Confirmation(int id)
        {
            try
            {
                var clientEmail = HttpContext.Session.GetString("ClientEmail");
                if (string.IsNullOrEmpty(clientEmail))
                {
                    return RedirectToAction("Index", "Gateau");
                }

                var session = _neo4jService.GetAsyncSession();

                var query = @"
                    MATCH (c:Client {email: $email})-[:A_DEMANDE]->(d:DemandeCustom {id: $demandeId})
                    RETURN d.id as Id,
                           d.categorie as Categorie,
                           d.spongeFilling as SpongeFilling,
                           d.quantite as Quantite,
                           d.message as Message,
                           d.statut as Statut,
                           d.dateCreation as DateCreation,
                           c.nom as ClientNom,
                           c.prenom as ClientPrenom,
                           c.email as ClientEmail,
                           c.telephone as ClientTelephone";

                var result = await session.RunAsync(query, new
                {
                    email = clientEmail,
                    demandeId = id
                });

                if (!await result.FetchAsync())
                {
                    await session.DisposeAsync();
                    TempData["Error"] = "Demande introuvable";
                    return RedirectToAction("Index", "Gateau");
                }

                var record = result.Current;

                // Convertir ZonedDateTime en DateTime correctement
                var dateCreationZoned = record["DateCreation"].As<ZonedDateTime>();
                var dateCreation = dateCreationZoned.ToDateTimeOffset().DateTime;

                var demande = new DemandeCustom
                {
                    Id = record["Id"].As<int>(),
                    Categorie = record["Categorie"].As<string>(),
                    SpongeFilling = record["SpongeFilling"].As<string>(),
                    Quantite = record["Quantite"].As<int>(),
                    Message = record["Message"].As<string>(),
                    Statut = record["Statut"].As<string>(),
                    DateCreation = dateCreation,
                    ClientNom = record["ClientNom"].As<string>(),
                    ClientPrenom = record["ClientPrenom"].As<string>(),
                    ClientEmail = record["ClientEmail"].As<string>(),
                    ClientTelephone = record["ClientTelephone"].As<string>()
                };

                await session.DisposeAsync();
                return View(demande);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur confirmation demande: {ex.Message}");
                TempData["Error"] = "Erreur lors de la récupération";
                return RedirectToAction("Index", "Gateau");
            }
        }
    }
}