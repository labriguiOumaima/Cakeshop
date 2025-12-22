using Microsoft.AspNetCore.Mvc;
using project.Models;
using Services;
using Neo4j.Driver;

namespace project.Controllers
{
    public class MesCommandesController : Controller
    {
        private readonly Neo4jService _neo4jService;
        private readonly ILogger<MesCommandesController> _logger;

        public MesCommandesController(Neo4jService neo4jService, ILogger<MesCommandesController> logger)
        {
            _neo4jService = neo4jService;
            _logger = logger;
        }

        // Vérification que l'utilisateur est connecté
        private bool IsClientConnected()
        {
            var clientEmail = HttpContext.Session.GetString("ClientEmail");
            return !string.IsNullOrEmpty(clientEmail);
        }

        // GET: MesCommandes/Index
        public async Task<IActionResult> Index()
        {
            if (!IsClientConnected())
            {
                TempData["Error"] = "Vous devez être connecté pour voir vos commandes";
                return RedirectToAction("Index", "Gateau");
            }

            try
            {
                var clientEmail = HttpContext.Session.GetString("ClientEmail");
                var session = _neo4jService.GetAsyncSession();

                // Récupérer toutes les commandes du client
                var query = @"
                    MATCH (c:Client {email: $email})-[:A_PASSE]->(cmd:Commande)
                    RETURN cmd.id as Id,
                           cmd.total as Total,
                           cmd.statut as Statut,
                           cmd.date as Date
                    ORDER BY cmd.date DESC";

                var result = await session.RunAsync(query, new { email = clientEmail });
                var commandes = new List<dynamic>();

                await foreach (var record in result)
                {
                    var dateValue = record["Date"];
                    DateTime date;
                    if (dateValue is Neo4j.Driver.ZonedDateTime zonedDateTime)
                    {
                        date = zonedDateTime.ToDateTimeOffset().DateTime;
                    }
                    else
                    {
                        date = dateValue.As<DateTime>();
                    }

                    commandes.Add(new
                    {
                        Id = record["Id"].As<int>(),
                        Total = record["Total"].As<decimal>(),
                        Statut = record["Statut"].As<string>(),
                        Date = date
                    });
                }

                await session.DisposeAsync();

                ViewBag.Commandes = commandes ?? new List<dynamic>();
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur MesCommandes: {ex.Message}");
                TempData["Error"] = "Erreur lors du chargement de vos commandes";
                return RedirectToAction("Index", "Gateau");
            }
        }

        // GET: MesCommandes/Details/{id}
        public async Task<IActionResult> Details(int id)
        {
            if (!IsClientConnected())
            {
                return RedirectToAction("Index", "Gateau");
            }

            try
            {
                var clientEmail = HttpContext.Session.GetString("ClientEmail");
                var session = _neo4jService.GetAsyncSession();

                // Vérifier que la commande appartient au client connecté
                var verifyQuery = @"
                    MATCH (c:Client {email: $email})-[:A_PASSE]->(cmd:Commande {id: $commandeId})
                    RETURN cmd.id";

                var verifyResult = await session.RunAsync(verifyQuery, new { email = clientEmail, commandeId = id });
                if (!await verifyResult.FetchAsync())
                {
                    await session.DisposeAsync();
                    TempData["Error"] = "Commande introuvable ou accès refusé";
                    return RedirectToAction("Index");
                }

                // Récupérer les détails de la commande
                var commandeQuery = @"
                    MATCH (c:Client {email: $email})-[:A_PASSE]->(cmd:Commande {id: $commandeId})
                    RETURN cmd.id as Id,
                           cmd.date as Date,
                           cmd.total as Total,
                           cmd.statut as Statut,
                           c.nom as ClientNom,
                           c.prenom as ClientPrenom,
                           c.email as ClientEmail,
                           c.telephone as ClientTelephone";

                var commandeResult = await session.RunAsync(commandeQuery, new { email = clientEmail, commandeId = id });
                await commandeResult.FetchAsync();
                var cmd = commandeResult.Current;

                var dateValue = cmd["Date"];
                DateTime date;
                if (dateValue is Neo4j.Driver.ZonedDateTime zonedDateTime)
                {
                    date = zonedDateTime.ToDateTimeOffset().DateTime;
                }
                else
                {
                    date = dateValue.As<DateTime>();
                }

                var commande = new Commande
                {
                    Id = cmd["Id"].As<int>(),
                    DateCommande = date,
                    MontantTotal = cmd["Total"].As<decimal>(),
                    Statut = cmd["Statut"].As<string>(),
                    ClientNom = cmd["ClientNom"].As<string>(),
                    ClientPrenom = cmd["ClientPrenom"].As<string>(),
                    ClientEmail = cmd["ClientEmail"].As<string>(),
                    ClientTelephone = cmd["ClientTelephone"].As<string>()
                };

                // Récupérer les articles de la commande
                var articlesQuery = @"
                    MATCH (cmd:Commande {id: $commandeId})-[r:CONTIENT]->(g:Gateau)
                    RETURN g.nom as NomGateau,
                           r.quantite as Quantite,
                           r.nombrePersonnes as NombrePersonnes,
                           r.prixUnitaire as PrixUnitaire";

                var articlesResult = await session.RunAsync(articlesQuery, new { commandeId = id });

                await foreach (var a in articlesResult)
                {
                    commande.Articles.Add(new ArticleCommande
                    {
                        NomGateau = a["NomGateau"].As<string>(),
                        Quantite = a["Quantite"].As<int>(),
                        NombrePersonnes = a["NombrePersonnes"].As<int>(),
                        PrixUnitaire = a["PrixUnitaire"].As<decimal>()
                    });
                }

                await session.DisposeAsync();

                return View(commande);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur Details MesCommandes: {ex.Message}");
                TempData["Error"] = "Erreur lors du chargement des détails";
                return RedirectToAction("Index");
            }
        }
    }
}