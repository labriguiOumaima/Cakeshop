using Microsoft.AspNetCore.Mvc;
using project.Models;
using Services;
using System.Text.Json;
using Neo4j.Driver;

namespace project.Controllers
{
    public class CommandeController : Controller
    {
        private readonly Neo4jService _neo4jService;
        private readonly ILogger<CommandeController> _logger;

        public CommandeController(Neo4jService neo4jService, ILogger<CommandeController> logger)
        {
            _neo4jService = neo4jService;
            _logger = logger;
        }

        // POST: Commande/Valider
        [HttpPost]
        public async Task<IActionResult> Valider()
        {
            try
            {
                var clientEmail = HttpContext.Session.GetString("ClientEmail");
                if (string.IsNullOrEmpty(clientEmail))
                {
                    TempData["Error"] = "Vous devez être connecté pour valider une commande";
                    return RedirectToAction("Index", "Panier");
                }

                var session = _neo4jService.GetAsyncSession();

                // Récupérer le client
                var clientQuery = @"MATCH (c:Client {email: $email}) RETURN c.id as ClientId";
                var clientResult = await session.RunAsync(clientQuery, new { email = clientEmail });

                if (!await clientResult.FetchAsync())
                {
                    await session.DisposeAsync();
                    TempData["Error"] = "Client introuvable";
                    return RedirectToAction("Index", "Panier");
                }

                var clientId = clientResult.Current["ClientId"].As<int>();

                // Récupérer le panier depuis Neo4j
                var panierQuery = @"
                    MATCH (p:Panier {clientId: $clientId})-[r:CONTIENT]->(g:Gateau)
                    RETURN g.id as GateauId,
                           g.nom as NomGateau,
                           g.prix as Prix,
                           g.categorie as Categorie,
                           r.nombrePersonnes as NombrePersonnes,
                           r.quantite as Quantite,
                           r.prixBase as PrixBase";

                var panierResult = await session.RunAsync(panierQuery, new { clientId });
                var articles = new List<dynamic>();
                decimal totalCommande = 0;
                bool hasCustomItems = false;

                await foreach (var record in panierResult)
                {
                    var categorie = record["Categorie"].As<string>();
                    var isCustom = categorie == "Custom";

                    if (isCustom)
                    {
                        hasCustomItems = true;
                    }
                    else
                    {
                        var nombrePersonnes = record["NombrePersonnes"].As<int>();
                        var prixBase = record["PrixBase"].As<decimal>();
                        var quantite = record["Quantite"].As<int>();

                        var ratios = new Dictionary<int, decimal>
                        {
                            { 6, 1m }, { 10, 1.6m }, { 15, 2.4m }, { 20, 3.2m }, { 30, 4.8m }
                        };

                        var prixUnitaire = prixBase * (ratios.ContainsKey(nombrePersonnes) ? ratios[nombrePersonnes] : nombrePersonnes / 6m);
                        var prixTotal = prixUnitaire * quantite;

                        totalCommande += prixTotal;

                        articles.Add(new
                        {
                            GateauId = record["GateauId"].As<int>(),
                            NomGateau = record["NomGateau"].As<string>(),
                            NombrePersonnes = nombrePersonnes,
                            Quantite = quantite,
                            PrixUnitaire = prixUnitaire,
                            Categorie = categorie,
                            IsCustom = false
                        });
                    }
                }

                if (!articles.Any() && !hasCustomItems)
                {
                    await session.DisposeAsync();
                    TempData["Error"] = "Votre panier est vide";
                    return RedirectToAction("Index", "Panier");
                }

                int commandeId = 0;

                // Créer la commande SEULEMENT s'il y a des articles normaux
                if (articles.Any())
                {
                    // Générer un ID unique pour la commande
                    var commandeIdQuery = @"
                        OPTIONAL MATCH (cmd:Commande)
                        WITH MAX(cmd.id) as maxId
                        RETURN CASE WHEN maxId IS NULL THEN 1 ELSE maxId + 1 END as NextId";

                    var commandeIdResult = await session.RunAsync(commandeIdQuery);
                    await commandeIdResult.FetchAsync();
                    commandeId = commandeIdResult.Current["NextId"].As<int>();

                    // Créer la commande avec statut "En cours"
                    var statut = "En cours";
                    var now = DateTime.Now;

                    var createCommandeQuery = @"
                        MATCH (c:Client {id: $clientId})
                        CREATE (cmd:Commande {
                            id: $commandeId,
                            date: $dateCommande,
                            total: $total,
                            statut: $statut,
                            hasCustomItems: $hasCustomItems
                        })
                        CREATE (c)-[:A_PASSE]->(cmd)
                        RETURN cmd.id as CommandeId";

                    await session.RunAsync(createCommandeQuery, new
                    {
                        clientId = clientId,
                        commandeId = commandeId,
                        dateCommande = now,
                        total = totalCommande,
                        statut = statut,
                        hasCustomItems = hasCustomItems
                    });

                    // Créer les articles de la commande (NORMAUX UNIQUEMENT)
                    foreach (var article in articles)
                    {
                        var createArticleQuery = @"
                            MATCH (cmd:Commande {id: $commandeId})
                            MATCH (g:Gateau {id: $gateauId})
                            CREATE (cmd)-[:CONTIENT {
                                quantite: $quantite,
                                nombrePersonnes: $nombrePersonnes,
                                prixUnitaire: $prixUnitaire,
                                categorie: $categorie
                            }]->(g)";

                        await session.RunAsync(createArticleQuery, new
                        {
                            commandeId = commandeId,
                            gateauId = article.GateauId,
                            quantite = article.Quantite,
                            nombrePersonnes = article.NombrePersonnes,
                            prixUnitaire = article.PrixUnitaire,
                            categorie = article.Categorie
                        });
                    }

                    _logger.LogInformation($"Commande #{commandeId} créée pour {clientEmail}");
                }

                // Vider le panier
                var deleteQuery = @"
                    MATCH (p:Panier {clientId: $clientId})-[r:CONTIENT]->()
                    DELETE r";

                await session.RunAsync(deleteQuery, new { clientId });

                // Mettre à jour le compteur panier
                HttpContext.Session.SetString("PanierCount", "0");

                // Stocker l'info pour la confirmation
                HttpContext.Session.SetString("HasCustomItems", hasCustomItems.ToString());
                if (articles.Any())
                {
                    HttpContext.Session.SetString("CommandeId", commandeId.ToString());
                    await session.DisposeAsync();
                    return RedirectToAction("Confirmation", new { id = commandeId });
                }
                else
                {
                    // Seulement du custom - créer une fausse commande pour la confirmation
                    // Générer un ID fictif
                    var fakeCommandeIdQuery = @"
                        OPTIONAL MATCH (cmd:Commande)
                        WITH MAX(cmd.id) as maxId
                        RETURN CASE WHEN maxId IS NULL THEN 1 ELSE maxId + 1 END as NextId";

                    var fakeCommandeIdResult = await session.RunAsync(fakeCommandeIdQuery);
                    await fakeCommandeIdResult.FetchAsync();
                    commandeId = fakeCommandeIdResult.Current["NextId"].As<int>();

                    HttpContext.Session.SetString("CommandeId", commandeId.ToString());
                    await session.DisposeAsync();
                    return RedirectToAction("Confirmation", new { id = commandeId, onlyCustom = true });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur validation commande: {ex.Message}");
                _logger.LogError($"Stack trace: {ex.StackTrace}");
                TempData["Error"] = "Erreur lors de la validation de la commande";
                return RedirectToAction("Index", "Panier");
            }
        }

        // GET: Commande/Confirmation/{id}
        public async Task<IActionResult> Confirmation(int id, bool onlyCustom = false)
        {
            try
            {
                var clientEmail = HttpContext.Session.GetString("ClientEmail");
                if (string.IsNullOrEmpty(clientEmail))
                {
                    return RedirectToAction("Index", "Gateau");
                }

                var session = _neo4jService.GetAsyncSession();

                var commande = new Commande();

                if (onlyCustom)
                {
                    // C'est une demande custom seulement
                    var clientQuery = @"MATCH (c:Client {email: $email}) 
                        RETURN c.nom as ClientNom, c.prenom as ClientPrenom, 
                               c.email as ClientEmail, c.telephone as ClientTelephone";

                    var clientResult = await session.RunAsync(clientQuery, new { email = clientEmail });

                    if (!await clientResult.FetchAsync())
                    {
                        await session.DisposeAsync();
                        TempData["Error"] = "Client introuvable";
                        return RedirectToAction("Index", "Gateau");
                    }

                    var record = clientResult.Current;
                    commande = new Commande
                    {
                        Id = id,
                        DateCommande = DateTime.Now,
                        MontantTotal = 0,
                        Statut = "Demande custom",
                        ClientNom = record["ClientNom"].As<string>(),
                        ClientPrenom = record["ClientPrenom"].As<string>(),
                        ClientEmail = record["ClientEmail"].As<string>(),
                        ClientTelephone = record["ClientTelephone"].As<string>()
                    };
                }
                else
                {
                    // Récupérer la commande normale
                    var commandeQuery = @"
                        MATCH (c:Client {email: $email})-[:A_PASSE]->(cmd:Commande {id: $commandeId})
                        RETURN cmd.id as Id, 
                               cmd.date as DateCommande, 
                               cmd.total as MontantTotal, 
                               cmd.statut as Statut,
                               c.nom as ClientNom,
                               c.prenom as ClientPrenom,
                               c.email as ClientEmail,
                               c.telephone as ClientTelephone";

                    var commandeResult = await session.RunAsync(commandeQuery, new
                    {
                        email = clientEmail,
                        commandeId = id
                    });

                    if (!await commandeResult.FetchAsync())
                    {
                        await session.DisposeAsync();
                        TempData["Error"] = "Commande introuvable";
                        return RedirectToAction("Index", "Gateau");
                    }

                    var record = commandeResult.Current;

                    commande = new Commande
                    {
                        Id = record["Id"].As<int>(),
                        DateCommande = record["DateCommande"].As<DateTime>(),
                        MontantTotal = record["MontantTotal"].As<decimal>(),
                        Statut = record["Statut"].As<string>(),
                        ClientNom = record["ClientNom"].As<string>(),
                        ClientPrenom = record["ClientPrenom"].As<string>(),
                        ClientEmail = record["ClientEmail"].As<string>(),
                        ClientTelephone = record["ClientTelephone"].As<string>()
                    };

                    var articlesQuery = @"
                        MATCH (cmd:Commande {id: $commandeId})-[r:CONTIENT]->(g:Gateau)
                        RETURN g.id as GateauId,
                               g.nom as NomGateau,
                               r.quantite as Quantite,
                               r.nombrePersonnes as NombrePersonnes,
                               r.prixUnitaire as PrixUnitaire,
                               r.categorie as Categorie";

                    var articlesResult = await session.RunAsync(articlesQuery, new { commandeId = id });

                    await foreach (var a in articlesResult)
                    {
                        commande.Articles.Add(new ArticleCommande
                        {
                            GateauId = a["GateauId"].As<int>(),
                            NomGateau = a["NomGateau"].As<string>(),
                            Quantite = a["Quantite"].As<int>(),
                            NombrePersonnes = a["NombrePersonnes"].As<int>(),
                            PrixUnitaire = a["PrixUnitaire"].As<decimal>(),
                            Categorie = a["Categorie"].As<string>()
                        });
                    }
                }

                await session.DisposeAsync();

                ViewBag.OnlyCustom = onlyCustom;

                return View(commande);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur confirmation commande: {ex.Message}");
                TempData["Error"] = "Erreur lors de la récupération de la commande";
                return RedirectToAction("Index", "Gateau");
            }
        }
    }
}