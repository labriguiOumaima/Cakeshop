using Microsoft.AspNetCore.Mvc;
using project.Models;
using Services;
using Neo4j.Driver;

namespace project.Controllers
{
    public class AdminController : Controller
    {
        private readonly Neo4jService _neo4jService;
        private readonly ILogger<AdminController> _logger;
        private readonly IWebHostEnvironment _hostingEnvironment;

        public AdminController(Neo4jService neo4jService, ILogger<AdminController> logger, IWebHostEnvironment hostingEnvironment)
        {
            _neo4jService = neo4jService;
            _logger = logger;
            _hostingEnvironment = hostingEnvironment;
        }

        // Vérification admin pour toutes les actions
        private bool IsAdmin()
        {
            var clientEmail = HttpContext.Session.GetString("ClientEmail");
            var clientRole = HttpContext.Session.GetString("ClientRole");

            return !string.IsNullOrEmpty(clientEmail) && clientRole == "admin";
        }

        // GET: Admin/Dashboard
        public async Task<IActionResult> Dashboard()
        {
            if (!IsAdmin())
            {
                TempData["Error"] = "Accès refusé. Vous devez être administrateur.";
                return RedirectToAction("Index", "Gateau");
            }

            try
            {
                var session = _neo4jService.GetAsyncSession();

                // 1. Total Commandes
                var totalCommandesQuery = @"
                    MATCH (cmd:Commande)
                    RETURN count(cmd) as Total";
                var totalCommandesResult = await session.RunAsync(totalCommandesQuery);
                await totalCommandesResult.FetchAsync();
                var totalCommandes = totalCommandesResult.Current["Total"].As<int>();

                // 2. Total Revenus
                var totalRevenusQuery = @"
                    MATCH (cmd:Commande)
                    RETURN sum(cmd.total) as Total";
                var totalRevenusResult = await session.RunAsync(totalRevenusQuery);
                await totalRevenusResult.FetchAsync();
                var totalRevenus = totalRevenusResult.Current["Total"].As<decimal>();

                // 3. Total Clients
                var totalClientsQuery = @"
                    MATCH (c:Client)
                    WHERE c.role <> 'admin'
                    RETURN count(c) as Total";
                var totalClientsResult = await session.RunAsync(totalClientsQuery);
                await totalClientsResult.FetchAsync();
                var totalClients = totalClientsResult.Current["Total"].As<int>();

                // 4. Demandes Custom en attente
                var demandesEnAttenteQuery = @"
                    MATCH (d:DemandeCustom)
                    WHERE d.statut = 'En attente de prix'
                    RETURN count(d) as Total";
                var demandesEnAttenteResult = await session.RunAsync(demandesEnAttenteQuery);
                await demandesEnAttenteResult.FetchAsync();
                var demandesEnAttente = demandesEnAttenteResult.Current["Total"].As<int>();

                // 5. Commandes aujourd'hui
                var commandesAujourdhuiQuery = @"
                    MATCH (cmd:Commande)
                    WHERE date(cmd.date) = date()
                    RETURN count(cmd) as Total";
                var commandesAujourdhuiResult = await session.RunAsync(commandesAujourdhuiQuery);
                await commandesAujourdhuiResult.FetchAsync();
                var commandesAujourdhui = commandesAujourdhuiResult.Current["Total"].As<int>();

                // 6. Revenus aujourd'hui
                var revenusAujourdhuiQuery = @"
                    MATCH (cmd:Commande)
                    WHERE date(cmd.date) = date()
                    RETURN sum(cmd.total) as Total";
                var revenusAujourdhuiResult = await session.RunAsync(revenusAujourdhuiQuery);
                await revenusAujourdhuiResult.FetchAsync();
                var revenusAujourdhui = revenusAujourdhuiResult.Current?["Total"]?.As<decimal?>() ?? 0;

                // 7. Gâteaux les plus vendus
                var topGateauxQuery = @"
                    MATCH (cmd:Commande)-[r:CONTIENT]->(g:Gateau)
                    WHERE g.categorie <> 'Custom'
                    RETURN g.nom as Nom, sum(r.quantite) as Quantite
                    ORDER BY Quantite DESC
                    LIMIT 5";
                var topGateauxResult = await session.RunAsync(topGateauxQuery);
                var topGateaux = new List<dynamic>();
                await foreach (var record in topGateauxResult)
                {
                    topGateaux.Add(new
                    {
                        Nom = record["Nom"].As<string>(),
                        Quantite = record["Quantite"].As<int>()
                    });
                }

                // 8. Dernières commandes
                var dernieresCommandesQuery = @"
                    MATCH (c:Client)-[:A_PASSE]->(cmd:Commande)
                    RETURN cmd.id as Id,
                           c.nom as ClientNom,
                           c.prenom as ClientPrenom,
                           cmd.total as Total,
                           cmd.statut as Statut,
                           cmd.date as Date
                    ORDER BY cmd.date DESC
                    LIMIT 5";
                var dernieresCommandesResult = await session.RunAsync(dernieresCommandesQuery);
                var dernieresCommandes = new List<dynamic>();
                await foreach (var record in dernieresCommandesResult)
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

                    dernieresCommandes.Add(new
                    {
                        Id = record["Id"].As<int>(),
                        ClientNom = record["ClientNom"].As<string>(),
                        ClientPrenom = record["ClientPrenom"].As<string>(),
                        Total = record["Total"].As<decimal>(),
                        Statut = record["Statut"].As<string>(),
                        Date = date
                    });
                }

                await session.DisposeAsync();

                // Passer les données à la vue via ViewBag
                ViewBag.TotalCommandes = totalCommandes;
                ViewBag.TotalRevenus = totalRevenus;
                ViewBag.TotalClients = totalClients;
                ViewBag.DemandesEnAttente = demandesEnAttente;
                ViewBag.CommandesAujourdhui = commandesAujourdhui;
                ViewBag.RevenusAujourdhui = revenusAujourdhui;
                ViewBag.TopGateaux = topGateaux;
                ViewBag.DernieresCommandes = dernieresCommandes;

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur dashboard: {ex.Message}");
                TempData["Error"] = "Erreur lors du chargement du dashboard";
                return RedirectToAction("Index", "Gateau");
            }
        }

        // GET: Admin/Gateaux
        public async Task<IActionResult> Gateaux()
        {
            if (!IsAdmin())
            {
                TempData["Error"] = "Accès refusé";
                return RedirectToAction("Index", "Gateau");
            }

            try
            {
                var session = _neo4jService.GetAsyncSession();

                // Récupérer les gâteaux
                var query = @"
                    MATCH (g:Gateau)
                    WHERE g.categorie <> 'Custom'
                    RETURN g.id as Id,
                           g.nom as Nom,
                           g.description as Description,
                           g.prix as Prix,
                           COALESCE(g.stock, 0) as Stock,
                           g.categorie as Categorie,
                           COALESCE(g.ImagePath, g.imagePath, '') as ImagePath
                    ORDER BY g.nom";

                var result = await session.RunAsync(query);
                var gateaux = new List<Gateau>();

                await foreach (var record in result)
                {
                    gateaux.Add(new Gateau
                    {
                        Id = record["Id"].As<int>(),
                        Nom = record["Nom"].As<string>() ?? "",
                        Description = record["Description"].As<string>() ?? "",
                        Prix = record["Prix"].As<decimal>(),
                        Stock = record["Stock"].As<int>(),
                        Categorie = record["Categorie"].As<string>() ?? "",
                        ImagePath = record["ImagePath"].As<string>()
                    });
                }

                // Récupérer les catégories existantes (CustomCategory + catégories uniques des gâteaux)
                var categories = new HashSet<string>();

                // Récupérer les catégories CustomCategory
                var customCategoriesQuery = @"
                    MATCH (cat:CustomCategory)
                    RETURN DISTINCT cat.nom as Nom
                    ORDER BY cat.nom";

                var customCategoriesResult = await session.RunAsync(customCategoriesQuery);
                await foreach (var record in customCategoriesResult)
                {
                    var nom = record["Nom"].As<string>();
                    if (!string.IsNullOrEmpty(nom))
                    {
                        categories.Add(nom);
                    }
                }

                // Récupérer les catégories uniques des gâteaux
                var gateauxCategoriesQuery = @"
                    MATCH (g:Gateau)
                    WHERE g.categorie IS NOT NULL AND g.categorie <> 'Custom'
                    RETURN DISTINCT g.categorie as Nom";

                var gateauxCategoriesResult = await session.RunAsync(gateauxCategoriesQuery);
                await foreach (var record in gateauxCategoriesResult)
                {
                    var nom = record["Nom"].As<string>();
                    if (!string.IsNullOrEmpty(nom))
                    {
                        categories.Add(nom);
                    }
                }

                await session.DisposeAsync();

                ViewBag.Categories = categories.OrderBy(c => c).ToList();
                return View(gateaux);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur Gateaux: {ex.Message}");
                TempData["Error"] = "Erreur lors du chargement";
                return RedirectToAction("Dashboard");
            }
        }

        // POST: Admin/CreerGateau - Modifié pour gérer l'upload
        [HttpPost]
        public async Task<IActionResult> CreerGateau()
        {
            if (!IsAdmin()) return Unauthorized();

            try
            {
                var nom = Request.Form["nom"].ToString();
                var description = Request.Form["description"].ToString();
                var prix = decimal.Parse(Request.Form["prix"].ToString() ?? "0");
                var stock = int.Parse(Request.Form["stock"].ToString() ?? "0");
                var categorie = Request.Form["categorie"].ToString();
                var imageFile = Request.Form.Files.Count > 0 ? Request.Form.Files["imageFile"] : null;

                string imagePath = "";

                // Sauvegarder l'image si elle existe
                if (imageFile != null && imageFile.Length > 0)
                {
                    try
                    {
                        // Vérifier le type de fichier
                        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                        var fileExtension = Path.GetExtension(imageFile.FileName).ToLower();

                        if (!allowedExtensions.Contains(fileExtension))
                        {
                            return BadRequest(new { error = "Type de fichier non autorisé. Accepté: JPG, PNG, GIF, WebP" });
                        }

                        // Vérifier la taille (max 5MB)
                        if (imageFile.Length > 5 * 1024 * 1024)
                        {
                            return BadRequest(new { error = "L'image dépasse 5MB" });
                        }

                        var fileName = Path.GetFileNameWithoutExtension(imageFile.FileName);
                        var uniqueFileName = $"{fileName}_{Guid.NewGuid()}{fileExtension}";
                        var uploadDir = Path.Combine(_hostingEnvironment.WebRootPath, "images", "gateaux");

                        // Créer le répertoire s'il n'existe pas
                        if (!Directory.Exists(uploadDir))
                        {
                            Directory.CreateDirectory(uploadDir);
                        }

                        var uploadPath = Path.Combine(uploadDir, uniqueFileName);

                        using (var stream = new FileStream(uploadPath, FileMode.Create))
                        {
                            await imageFile.CopyToAsync(stream);
                        }

                        imagePath = uniqueFileName;
                        _logger.LogInformation($"Image sauvegardée: {imagePath}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Erreur sauvegarde image: {ex.Message}");
                        _logger.LogError($"Stack trace: {ex.StackTrace}");
                        return BadRequest(new { error = $"Erreur lors de la sauvegarde de l'image: {ex.Message}" });
                    }
                }

                var session = _neo4jService.GetAsyncSession();

                // Générer un ID unique
                var getMaxIdQuery = @"
                    OPTIONAL MATCH (g:Gateau)
                    WITH MAX(g.id) as maxId
                    RETURN CASE WHEN maxId IS NULL THEN 1 ELSE maxId + 1 END as NextId";
                var maxIdResult = await session.RunAsync(getMaxIdQuery);
                await maxIdResult.FetchAsync();
                var newId = maxIdResult.Current["NextId"].As<int>();

                var createQuery = @"
                    CREATE (g:Gateau {
                        id: $id,
                        nom: $nom,
                        description: $description,
                        prix: $prix,
                        stock: $stock,
                        categorie: $categorie,
                        ImagePath: $ImagePath,
                        imagePath: $ImagePath
                    })
                    RETURN g.id as Id";

                await session.RunAsync(createQuery, new
                {
                    id = newId,
                    nom = nom,
                    description = description,
                    prix = prix,
                    stock = stock,
                    categorie = categorie,
                    ImagePath = imagePath
                });

                await session.DisposeAsync();
                _logger.LogInformation($"Gâteau créé: {nom}");
                return Ok(new { message = "Gâteau créé avec succès", id = newId });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur CreerGateau: {ex.Message}");
                _logger.LogError($"Stack trace: {ex.StackTrace}");
                return BadRequest(new { error = $"Erreur lors de la création: {ex.Message}" });
            }
        }

        // POST: Admin/ModifierGateau - Modifié pour gérer l'upload
        [HttpPost]
        public async Task<IActionResult> ModifierGateau()
        {
            if (!IsAdmin()) return Unauthorized();

            try
            {
                var id = int.Parse(Request.Form["id"].ToString() ?? "0");
                var nom = Request.Form["nom"].ToString();
                var description = Request.Form["description"].ToString();
                var prix = decimal.Parse(Request.Form["prix"].ToString() ?? "0");
                var stock = int.Parse(Request.Form["stock"].ToString() ?? "0");
                var categorie = Request.Form["categorie"].ToString();
                var imageFile = Request.Form.Files.Count > 0 ? Request.Form.Files["imageFile"] : null;

                string imagePath = Request.Form["ImagePath"].ToString() ?? "";

                // Sauvegarder la nouvelle image si elle existe
                if (imageFile != null && imageFile.Length > 0)
                {
                    try
                    {
                        // Vérifier le type de fichier
                        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                        var fileExtension = Path.GetExtension(imageFile.FileName).ToLower();

                        if (!allowedExtensions.Contains(fileExtension))
                        {
                            return BadRequest(new { error = "Type de fichier non autorisé. Accepté: JPG, PNG, GIF, WebP" });
                        }

                        // Vérifier la taille (max 5MB)
                        if (imageFile.Length > 5 * 1024 * 1024)
                        {
                            return BadRequest(new { error = "L'image dépasse 5MB" });
                        }

                        // Supprimer l'ancienne image si elle existe
                        if (!string.IsNullOrEmpty(imagePath))
                        {
                            var oldImagePath = Path.Combine(_hostingEnvironment.WebRootPath, "images", "gateaux", imagePath);
                            if (System.IO.File.Exists(oldImagePath))
                            {
                                try
                                {
                                    System.IO.File.Delete(oldImagePath);
                                    _logger.LogInformation($"Ancienne image supprimée: {imagePath}");
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning($"Impossible de supprimer l'ancienne image: {ex.Message}");
                                }
                            }
                        }

                        // Sauvegarder la nouvelle image
                        var fileName = Path.GetFileNameWithoutExtension(imageFile.FileName);
                        var uniqueFileName = $"{fileName}_{Guid.NewGuid()}{fileExtension}";
                        var uploadDir = Path.Combine(_hostingEnvironment.WebRootPath, "images", "gateaux");

                        // Créer le répertoire s'il n'existe pas
                        if (!Directory.Exists(uploadDir))
                        {
                            Directory.CreateDirectory(uploadDir);
                        }

                        var uploadPath = Path.Combine(uploadDir, uniqueFileName);

                        using (var stream = new FileStream(uploadPath, FileMode.Create))
                        {
                            await imageFile.CopyToAsync(stream);
                        }

                        imagePath = uniqueFileName;
                        _logger.LogInformation($"Nouvelle image sauvegardée: {imagePath}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Erreur sauvegarde image: {ex.Message}");
                        _logger.LogError($"Stack trace: {ex.StackTrace}");
                        return BadRequest(new { error = $"Erreur lors de la sauvegarde de l'image: {ex.Message}" });
                    }
                }

                var session = _neo4jService.GetAsyncSession();
                var updateQuery = @"
                    MATCH (g:Gateau {id: $id})
                    SET g.nom = $nom,
                        g.description = $description,
                        g.prix = $prix,
                        g.stock = $stock,
                        g.categorie = $categorie,
                        g.ImagePath = $ImagePath,
                        g.imagePath = $ImagePath
                    RETURN g.id as Id";

                await session.RunAsync(updateQuery, new
                {
                    id = id,
                    nom = nom,
                    description = description,
                    prix = prix,
                    stock = stock,
                    categorie = categorie,
                    ImagePath = imagePath
                });

                await session.DisposeAsync();
                _logger.LogInformation($"Gâteau modifié: {nom}");
                return Ok(new { message = "Gâteau modifié avec succès" });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur ModifierGateau: {ex.Message}");
                _logger.LogError($"Stack trace: {ex.StackTrace}");
                return BadRequest(new { error = $"Erreur lors de la modification: {ex.Message}" });
            }
        }

        // POST: Admin/SupprimerGateau
        [HttpPost]
        public async Task<IActionResult> SupprimerGateau([FromBody] dynamic data)
        {
            if (!IsAdmin()) return Unauthorized();

            try
            {
                // Convertir dynamic en JsonElement pour accéder aux propriétés
                var jsonElement = System.Text.Json.JsonSerializer.SerializeToElement(data);
                int gateauId = jsonElement.GetProperty("gateauId").GetInt32();
                var session = _neo4jService.GetAsyncSession();
                var deleteQuery = @"
                    MATCH (g:Gateau {id: $gateauId})
                    DETACH DELETE g";

                await session.RunAsync(deleteQuery, new { gateauId });
                await session.DisposeAsync();

                _logger.LogInformation($"Gâteau #{gateauId} supprimé");
                return Ok(new { message = "Gâteau supprimé avec succès" });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur SupprimerGateau: {ex.Message}");
                return BadRequest("Erreur lors de la suppression");
            }
        }

        // GET: Admin/Commandes
        public async Task<IActionResult> Commandes()
        {
            if (!IsAdmin())
            {
                TempData["Error"] = "Accès refusé";
                return RedirectToAction("Index", "Gateau");
            }

            try
            {
                var session = _neo4jService.GetAsyncSession();

                var query = @"
                    MATCH (c:Client)-[:A_PASSE]->(cmd:Commande)
                    RETURN cmd.id as Id,
                           cmd.total as Total,
                           cmd.statut as Statut,
                           cmd.date as Date,
                           c.nom as ClientNom,
                           c.prenom as ClientPrenom,
                           c.email as ClientEmail,
                           c.telephone as ClientTelephone
                    ORDER BY cmd.date DESC";

                var result = await session.RunAsync(query);
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
                        Date = date,
                        ClientNom = record["ClientNom"].As<string>(),
                        ClientPrenom = record["ClientPrenom"].As<string>(),
                        ClientEmail = record["ClientEmail"].As<string>(),
                        ClientTelephone = record["ClientTelephone"].As<string>()
                    });
                }

                await session.DisposeAsync();

                ViewBag.Commandes = commandes ?? new List<dynamic>();
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur Commandes: {ex.Message}");
                TempData["Error"] = "Erreur lors du chargement";
                return RedirectToAction("Dashboard");
            }
        }

        // POST: Admin/ModifierStatutCommande
        [HttpPost]
        public async Task<IActionResult> ModifierStatutCommande([FromBody] dynamic data)
        {
            if (!IsAdmin()) return Unauthorized();

            try
            {
                // Convertir dynamic en JsonElement pour accéder aux propriétés
                var jsonElement = System.Text.Json.JsonSerializer.SerializeToElement(data);
                int commandeId = jsonElement.GetProperty("commandeId").GetInt32();
                string statut = jsonElement.GetProperty("statut").GetString() ?? "";

                var session = _neo4jService.GetAsyncSession();

                var updateQuery = @"
                    MATCH (cmd:Commande {id: $commandeId})
                    SET cmd.statut = $statut
                    RETURN cmd.id";

                await session.RunAsync(updateQuery, new { commandeId, statut });
                await session.DisposeAsync();

                _logger.LogInformation($"Statut commande #{commandeId} → {statut}");
                return Ok(new { message = "Statut mis à jour" });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur ModifierStatutCommande: {ex.Message}");
                return BadRequest("Erreur");
            }
        }

        // GET: Admin/DetailsCommande
        public async Task<IActionResult> DetailsCommande(int id)
        {
            if (!IsAdmin()) return Unauthorized();

            try
            {
                var session = _neo4jService.GetAsyncSession();

                // Infos commande
                var commandeQuery = @"
                    MATCH (c:Client)-[:A_PASSE]->(cmd:Commande {id: $commandeId})
                    RETURN cmd.id as Id,
                           cmd.total as Total,
                           cmd.statut as Statut,
                           cmd.date as Date,
                           c.nom as ClientNom,
                           c.prenom as ClientPrenom,
                           c.email as ClientEmail,
                           c.telephone as ClientTelephone,
                           c.adresse as ClientAdresse";

                var commandeResult = await session.RunAsync(commandeQuery, new { commandeId = id });
                await commandeResult.FetchAsync();
                var cmd = commandeResult.Current;

                // Convertir la date si nécessaire
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

                // Articles de la commande
                var articlesQuery = @"
                    MATCH (cmd:Commande {id: $commandeId})-[r:CONTIENT]->(g:Gateau)
                    RETURN g.nom as NomGateau,
                           g.categorie as Categorie,
                           r.quantite as Quantite,
                           r.nombrePersonnes as NombrePersonnes,
                           r.prixUnitaire as PrixUnitaire";

                var articlesResult = await session.RunAsync(articlesQuery, new { commandeId = id });
                var articles = new List<dynamic>();

                await foreach (var record in articlesResult)
                {
                    articles.Add(new
                    {
                        NomGateau = record["NomGateau"].As<string>(),
                        Categorie = record["Categorie"].As<string>(),
                        Quantite = record["Quantite"].As<int>(),
                        NombrePersonnes = record["NombrePersonnes"].As<int>(),
                        PrixUnitaire = record["PrixUnitaire"].As<decimal>()
                    });
                }

                await session.DisposeAsync();

                // Créer le HTML
                var html = $@"
                    <div style='margin-bottom: 2rem;'>
                        <h6 style='color: var(--gold); font-size: 1.2rem; margin-bottom: 1rem;'>Informations Client</h6>
                        <p><strong>Nom :</strong> {cmd["ClientPrenom"].As<string>()} {cmd["ClientNom"].As<string>()}</p>
                        <p><strong>Email :</strong> {cmd["ClientEmail"].As<string>()}</p>
                        <p><strong>Téléphone :</strong> {cmd["ClientTelephone"].As<string>()}</p>
                        <p><strong>Adresse :</strong> {cmd["ClientAdresse"].As<string>()}</p>
                    </div>
                    <div style='margin-bottom: 2rem;'>
                        <h6 style='color: var(--gold); font-size: 1.2rem; margin-bottom: 1rem;'>Informations Commande</h6>
                        <p><strong>Numéro :</strong> #{cmd["Id"].As<int>()}</p>
                        <p><strong>Date :</strong> {date.ToString("dd/MM/yyyy HH:mm")}</p>
                        <p><strong>Statut :</strong> {cmd["Statut"].As<string>()}</p>
                        <p><strong>Total :</strong> {cmd["Total"].As<decimal>().ToString("N2")} DH</p>
                    </div>
                    <div>
                        <h6 style='color: var(--gold); font-size: 1.2rem; margin-bottom: 1rem;'>Articles</h6>
                        <table class='table-custom'>
                            <thead>
                                <tr>
                                    <th>Gâteau</th>
                                    <th>Portions</th>
                                    <th>Quantité</th>
                                    <th>Prix Unit.</th>
                                    <th>Total</th>
                                </tr>
                            </thead>
                            <tbody>";

                foreach (var article in articles)
                {
                    var categorie = article.Categorie;
                    var portions = categorie == "Custom" ? "N/A" : article.NombrePersonnes.ToString();
                    var prixUnit = article.PrixUnitaire;
                    var total = prixUnit * article.Quantite;

                    html += $@"
                        <tr>
                            <td>{article.NomGateau}</td>
                            <td>{portions}</td>
                            <td>{article.Quantite}</td>
                            <td>{prixUnit.ToString("N2")} DH</td>
                            <td><strong>{total.ToString("N2")} DH</strong></td>
                        </tr>";
                }

                html += @"
                            </tbody>
                        </table>
                    </div>";

                return Content(html, "text/html");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur DetailsCommande: {ex.Message}");
                return Content("<p style='color: #dc3545;'>Erreur lors du chargement des détails</p>", "text/html");
            }
        }

        // POST: Admin/SupprimerCommande
        [HttpPost]
        public async Task<IActionResult> SupprimerCommande([FromBody] dynamic data)
        {
            if (!IsAdmin()) return Unauthorized();

            try
            {
                // Convertir dynamic en JsonElement pour accéder aux propriétés
                var jsonElement = System.Text.Json.JsonSerializer.SerializeToElement(data);
                int commandeId = jsonElement.GetProperty("commandeId").GetInt32();

                var session = _neo4jService.GetAsyncSession();

                var deleteQuery = @"
                    MATCH (cmd:Commande {id: $commandeId})
                    DETACH DELETE cmd";

                await session.RunAsync(deleteQuery, new { commandeId });
                await session.DisposeAsync();

                _logger.LogInformation($"Commande #{commandeId} supprimée");
                return Ok(new { message = "Commande supprimée" });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur SupprimerCommande: {ex.Message}");
                return BadRequest("Erreur");
            }
        }
        // GET: Admin/DemandesCustom
        public async Task<IActionResult> DemandesCustom()
        {
            if (!IsAdmin())
            {
                TempData["Error"] = "Accès refusé";
                return RedirectToAction("Index", "Gateau");
            }

            try
            {
                var session = _neo4jService.GetAsyncSession();

                var query = @"
                    MATCH (c:Client)-[:A_DEMANDE]->(d:DemandeCustom)
                    RETURN d.id as Id,
                           d.categorie as Categorie,
                           d.spongeFilling as SpongeFilling,
                           d.quantite as Quantite,
                           d.message as Message,
                           d.statut as Statut,
                           d.prix as Prix,
                           d.dateCreation as DateCreation,
                           c.nom as ClientNom,
                           c.prenom as ClientPrenom,
                           c.email as ClientEmail,
                           c.telephone as ClientTelephone
                    ORDER BY d.dateCreation DESC";

                var result = await session.RunAsync(query);
                var demandes = new List<dynamic>();

                await foreach (var record in result)
                {
                    var dateValue = record["DateCreation"];
                    DateTime dateCreation;
                    if (dateValue is Neo4j.Driver.ZonedDateTime zonedDateTime)
                    {
                        dateCreation = zonedDateTime.ToDateTimeOffset().DateTime;
                    }
                    else
                    {
                        dateCreation = dateValue.As<DateTime>();
                    }

                    // Récupérer le prix (peut être null ou 0)
                    decimal prix = 0;
                    try
                    {
                        var prixValue = record["Prix"];
                        if (prixValue != null)
                        {
                            prix = prixValue.As<decimal>();
                        }
                    }
                    catch
                    {
                        prix = 0;
                    }

                    demandes.Add(new
                    {
                        Id = record["Id"].As<int>(),
                        Categorie = record["Categorie"].As<string>(),
                        SpongeFilling = record["SpongeFilling"].As<string>(),
                        Quantite = record["Quantite"].As<int>(),
                        Message = record["Message"].As<string>(),
                        Statut = record["Statut"].As<string>(),
                        Prix = prix,
                        DateCreation = dateCreation,
                        ClientNom = record["ClientNom"].As<string>(),
                        ClientPrenom = record["ClientPrenom"].As<string>(),
                        ClientEmail = record["ClientEmail"].As<string>(),
                        ClientTelephone = record["ClientTelephone"].As<string>()
                    });
                }

                await session.DisposeAsync();

                ViewBag.Demandes = demandes ?? new List<dynamic>();
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur DemandesCustom: {ex.Message}");
                TempData["Error"] = "Erreur lors du chargement";
                return RedirectToAction("Dashboard");
            }
        }

        // POST: Admin/DefinirPrixCustom
        [HttpPost]
        public async Task<IActionResult> DefinirPrixCustom([FromBody] dynamic data)
        {
            if (!IsAdmin()) return Unauthorized();

            try
            {
                // Convertir dynamic en JsonElement pour accéder aux propriétés
                var jsonElement = System.Text.Json.JsonSerializer.SerializeToElement(data);
                int demandeId = jsonElement.GetProperty("demandeId").GetInt32();
                decimal prix = jsonElement.GetProperty("prix").GetDecimal();

                var session = _neo4jService.GetAsyncSession();

                // Mettre à jour la demande
                var updateQuery = @"
                    MATCH (d:DemandeCustom {id: $demandeId})
                    SET d.prix = $prix, d.statut = 'EN COURS'
                    RETURN d.id";

                await session.RunAsync(updateQuery, new { demandeId, prix });

                // Mettre à jour le gâteau custom correspondant
                var updateGateauQuery = @"
                    MATCH (g:Gateau {demandeCustomId: $demandeId, categorie: 'Custom'})
                    SET g.prix = $prix
                    RETURN g.id";

                await session.RunAsync(updateGateauQuery, new { demandeId, prix });

                await session.DisposeAsync();

                _logger.LogInformation($"Prix défini pour demande #{demandeId}: {prix} DH");
                return Ok(new { message = "Prix défini" });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur DefinirPrixCustom: {ex.Message}");
                return BadRequest("Erreur");
            }
        }

        // POST: Admin/SupprimerDemandeCustom
        [HttpPost]
        public async Task<IActionResult> SupprimerDemandeCustom([FromBody] dynamic data)
        {
            if (!IsAdmin()) return Unauthorized();

            try
            {
                // Convertir dynamic en JsonElement pour accéder aux propriétés
                var jsonElement = System.Text.Json.JsonSerializer.SerializeToElement(data);
                int demandeId = jsonElement.GetProperty("demandeId").GetInt32();

                var session = _neo4jService.GetAsyncSession();

                // Supprimer le gâteau custom associé
                var deleteGateauQuery = @"
                    MATCH (g:Gateau {demandeCustomId: $demandeId, categorie: 'Custom'})
                    DETACH DELETE g";

                await session.RunAsync(deleteGateauQuery, new { demandeId });

                // Supprimer la demande
                var deleteQuery = @"
                    MATCH (d:DemandeCustom {id: $demandeId})
                    DETACH DELETE d";

                await session.RunAsync(deleteQuery, new { demandeId });

                await session.DisposeAsync();

                _logger.LogInformation($"Demande #{demandeId} supprimée");
                return Ok(new { message = "Demande supprimée" });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur SupprimerDemandeCustom: {ex.Message}");
                return BadRequest("Erreur");
            }
        }

        // GET: Admin/Clients
        public async Task<IActionResult> Clients()
        {
            if (!IsAdmin())
            {
                TempData["Error"] = "Accès refusé";
                return RedirectToAction("Index", "Gateau");
            }

            try
            {
                var session = _neo4jService.GetAsyncSession();
                var query = @"
                    MATCH (c:Client)
                    WHERE c.role <> 'admin' AND c.id IS NOT NULL
                    RETURN c.id as Id,
                           c.nom as Nom,
                           c.prenom as Prenom,
                           c.email as Email,
                           c.telephone as Telephone,
                           c.adresse as Adresse,
                           c.dateInscription as DateInscription
                    ORDER BY c.dateInscription DESC";

                var result = await session.RunAsync(query);
                var clients = new List<dynamic>();

                await foreach (var record in result)
                {
                    try
                    {
                        var idValue = record["Id"];
                        if (idValue == null)
                            continue; // Ignorer les clients sans ID

                        var dateValue = record["DateInscription"];
                        DateTime dateInscription;
                        if (dateValue is Neo4j.Driver.ZonedDateTime zonedDateTime)
                        {
                            dateInscription = zonedDateTime.ToDateTimeOffset().DateTime;
                        }
                        else if (dateValue != null)
                        {
                            try
                            {
                                dateInscription = dateValue.As<DateTime>();
                            }
                            catch
                            {
                                dateInscription = DateTime.Now; // Valeur par défaut si erreur
                            }
                        }
                        else
                        {
                            dateInscription = DateTime.Now; // Valeur par défaut si null
                        }

                        clients.Add(new
                        {
                            Id = idValue.As<int>(),
                            Nom = record["Nom"].As<string>() ?? "",
                            Prenom = record["Prenom"].As<string>() ?? "",
                            Email = record["Email"].As<string>() ?? "",
                            Telephone = record["Telephone"].As<string>() ?? "",
                            Adresse = record["Adresse"].As<string>() ?? "",
                            DateInscription = dateInscription
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Erreur lors du traitement d'un client: {ex.Message}");
                        continue; // Ignorer ce client et continuer avec les autres
                    }
                }

                await session.DisposeAsync();
                ViewBag.Clients = clients ?? new List<dynamic>();
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur Clients: {ex.Message}");
                _logger.LogError($"Stack trace: {ex.StackTrace}");
                TempData["Error"] = "Erreur lors du chargement des clients";
                ViewBag.Clients = new List<dynamic>();
                return View();
            }
        }

        // POST: Admin/ModifierClient
        [HttpPost]
        public async Task<IActionResult> ModifierClient([FromBody] dynamic data)
        {
            if (!IsAdmin()) return Unauthorized();

            try
            {
                // Convertir dynamic en JsonElement pour accéder aux propriétés
                var jsonElement = System.Text.Json.JsonSerializer.SerializeToElement(data);
                int clientId = jsonElement.GetProperty("clientId").GetInt32();
                string nom = jsonElement.GetProperty("nom").GetString() ?? "";
                string prenom = jsonElement.GetProperty("prenom").GetString() ?? "";
                string email = jsonElement.GetProperty("email").GetString() ?? "";
                string telephone = jsonElement.TryGetProperty("telephone", out System.Text.Json.JsonElement telProp) ? telProp.GetString() ?? "" : "";
                string adresse = jsonElement.TryGetProperty("adresse", out System.Text.Json.JsonElement addrProp) ? addrProp.GetString() ?? "" : "";

                if (string.IsNullOrEmpty(nom) || string.IsNullOrEmpty(prenom) || string.IsNullOrEmpty(email))
                {
                    return BadRequest("Les champs nom, prénom et email sont obligatoires");
                }

                var session = _neo4jService.GetAsyncSession();
                var updateQuery = @"
                    MATCH (c:Client {id: $clientId})
                    SET c.nom = $nom,
                        c.prenom = $prenom,
                        c.email = $email,
                        c.telephone = $telephone,
                        c.adresse = $adresse
                    RETURN c.id as Id";

                await session.RunAsync(updateQuery, new { clientId, nom, prenom, email, telephone, adresse });
                await session.DisposeAsync();

                _logger.LogInformation($"Client #{clientId} modifié");
                return Ok(new { message = "Client modifié avec succès" });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur ModifierClient: {ex.Message}");
                return BadRequest("Erreur lors de la modification");
            }
        }

        // POST: Admin/SupprimerClient
        [HttpPost]
        public async Task<IActionResult> SupprimerClient([FromBody] dynamic data)
        {
            if (!IsAdmin()) return Unauthorized();

            try
            {
                // Convertir dynamic en JsonElement pour accéder aux propriétés
                var jsonElement = System.Text.Json.JsonSerializer.SerializeToElement(data);
                int clientId = jsonElement.GetProperty("clientId").GetInt32();
                var session = _neo4jService.GetAsyncSession();
                var deleteQuery = @"
                    MATCH (c:Client {id: $clientId})
                    DETACH DELETE c";

                await session.RunAsync(deleteQuery, new { clientId });
                await session.DisposeAsync();

                _logger.LogInformation($"Client #{clientId} supprimé");
                return Ok(new { message = "Client supprimé avec succès" });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur SupprimerClient: {ex.Message}");
                return BadRequest("Erreur lors de la suppression");
            }
        }

        // POST: Admin/CreerClient
        [HttpPost]
        public async Task<IActionResult> CreerClient([FromBody] dynamic data)
        {
            if (!IsAdmin()) return Unauthorized();

            try
            {
                // Convertir dynamic en JsonElement pour accéder aux propriétés
                var jsonElement = System.Text.Json.JsonSerializer.SerializeToElement(data);
                string nom = jsonElement.GetProperty("nom").GetString() ?? "";
                string prenom = jsonElement.GetProperty("prenom").GetString() ?? "";
                string email = jsonElement.GetProperty("email").GetString() ?? "";
                string telephone = jsonElement.TryGetProperty("telephone", out System.Text.Json.JsonElement telProp) ? telProp.GetString() ?? "" : "";
                string adresse = jsonElement.TryGetProperty("adresse", out System.Text.Json.JsonElement addrProp) ? addrProp.GetString() ?? "" : "";

                var session = _neo4jService.GetAsyncSession();

                // Vérifier si l'email existe déjà
                var checkEmailQuery = @"MATCH (c:Client {email: $email}) RETURN c.id as Id";
                var checkResult = await session.RunAsync(checkEmailQuery, new { email });
                if (await checkResult.FetchAsync())
                {
                    await session.DisposeAsync();
                    return BadRequest("Un client avec cet email existe déjà");
                }

                // Générer un ID unique
                var getMaxIdQuery = @"
                    OPTIONAL MATCH (c:Client)
                    WITH MAX(c.id) as maxId
                    RETURN CASE WHEN maxId IS NULL THEN 1 ELSE maxId + 1 END as NextId";

                var maxIdResult = await session.RunAsync(getMaxIdQuery);
                await maxIdResult.FetchAsync();
                var newClientId = maxIdResult.Current["NextId"].As<int>();

                // Créer le nouveau client
                var createQuery = @"
                    CREATE (c:Client {
                        id: $id,
                        nom: $nom,
                        prenom: $prenom,
                        email: $email,
                        telephone: $telephone,
                        adresse: $adresse,
                        role: 'client',
                        dateInscription: datetime(),
                        mdp: ''
                    })
                    RETURN c.id as Id";

                await session.RunAsync(createQuery, new
                {
                    id = newClientId,
                    nom,
                    prenom,
                    email,
                    telephone,
                    adresse
                });
                await session.DisposeAsync();

                _logger.LogInformation($"Nouveau client créé : {email} avec ID {newClientId}");
                return Ok(new { message = "Client créé avec succès" });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur CreerClient: {ex.Message}");
                return BadRequest("Erreur lors de la création");
            }
        }

        // GET: Admin/Categories
        public async Task<IActionResult> Categories()
        {
            if (!IsAdmin())
            {
                TempData["Error"] = "Accès refusé";
                return RedirectToAction("Index", "Gateau");
            }

            try
            {
                var session = _neo4jService.GetAsyncSession();
                var query = @"
                    MATCH (cat:CustomCategory)
                    RETURN cat.id as Id,
                           cat.nom as Nom,
                           cat.description as Description,
                           COALESCE(cat.ImagePath, '') as ImagePath
                    ORDER BY cat.nom";

                var result = await session.RunAsync(query);
                var categories = new List<CustomCategory>();

                await foreach (var record in result)
                {
                    categories.Add(new CustomCategory
                    {
                        Id = record["Id"].As<int>(),
                        Nom = record["Nom"].As<string>() ?? "",
                        Description = record["Description"].As<string>() ?? "",
                        ImagePath = record["ImagePath"].As<string>() ?? ""
                    });
                }

                await session.DisposeAsync();
                return View(categories);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur Categories: {ex.Message}");
                TempData["Error"] = "Erreur lors du chargement";
                return RedirectToAction("Dashboard");
            }
        }

        // POST: Admin/CreerCategorie - avec upload d'image
        [HttpPost]
        public async Task<IActionResult> CreerCategorie()
        {
            if (!IsAdmin()) return Unauthorized();

            try
            {
                var nom = Request.Form["nom"].ToString();
                var description = Request.Form["description"].ToString() ?? "";
                var existingPath = Request.Form["ImagePath"].ToString() ?? "";
                var imageFile = Request.Form.Files.Count > 0 ? Request.Form.Files["imageFile"] : null;

                string imagePath = existingPath;

                // Upload de l'image si fournie
                if (imageFile != null && imageFile.Length > 0)
                {
                    var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                    var ext = Path.GetExtension(imageFile.FileName).ToLower();
                    if (!allowedExtensions.Contains(ext))
                    {
                        return BadRequest("Type de fichier non autorisé pour l'image de catégorie.");
                    }

                    if (imageFile.Length > 5 * 1024 * 1024)
                    {
                        return BadRequest("L'image dépasse 5MB.");
                    }

                    var fileName = Path.GetFileNameWithoutExtension(imageFile.FileName);
                    var uniqueFileName = $"{fileName}_{Guid.NewGuid()}{ext}";
                    var uploadDir = Path.Combine(_hostingEnvironment.WebRootPath, "images", "categories");
                    if (!Directory.Exists(uploadDir))
                        Directory.CreateDirectory(uploadDir);

                    var uploadPath = Path.Combine(uploadDir, uniqueFileName);
                    using (var stream = new FileStream(uploadPath, FileMode.Create))
                    {
                        await imageFile.CopyToAsync(stream);
                    }

                    imagePath = "/images/categories/" + uniqueFileName;
                }

                var session = _neo4jService.GetAsyncSession();

                var getMaxIdQuery = @"
                    OPTIONAL MATCH (cat:CustomCategory)
                    WITH MAX(cat.id) as maxId
                    RETURN CASE WHEN maxId IS NULL THEN 1 ELSE maxId + 1 END as NextId";
                var maxIdResult = await session.RunAsync(getMaxIdQuery);
                await maxIdResult.FetchAsync();
                var newId = maxIdResult.Current["NextId"].As<int>();

                var createQuery = @"
                    CREATE (cat:CustomCategory {
                        id: $id,
                        nom: $nom,
                        description: $description,
                        ImagePath: $ImagePath
                    })
                    RETURN cat.id as Id";

                await session.RunAsync(createQuery, new
                {
                    id = newId,
                    nom = nom,
                    description = description,
                    ImagePath = imagePath
                });

                await session.DisposeAsync();
                _logger.LogInformation($"Catégorie créée: {nom}");
                return Ok(new { message = "Catégorie créée avec succès", id = newId });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur CreerCategorie: {ex.Message}");
                return BadRequest("Erreur lors de la création");
            }
        }

        // POST: Admin/ModifierCategorie - avec upload d'image
        [HttpPost]
        public async Task<IActionResult> ModifierCategorie()
        {
            if (!IsAdmin()) return Unauthorized();

            try
            {
                var id = int.Parse(Request.Form["id"].ToString() ?? "0");
                var nom = Request.Form["nom"].ToString();
                var description = Request.Form["description"].ToString() ?? "";
                var existingPath = Request.Form["ImagePath"].ToString() ?? "";
                var imageFile = Request.Form.Files.Count > 0 ? Request.Form.Files["imageFile"] : null;

                string imagePath = existingPath;

                var session = _neo4jService.GetAsyncSession();

                // Récupérer l'ancienne image si besoin
                if (imageFile != null && imageFile.Length > 0)
                {
                    try
                    {
                        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                        var ext = Path.GetExtension(imageFile.FileName).ToLower();
                        if (!allowedExtensions.Contains(ext))
                        {
                            await session.DisposeAsync();
                            return BadRequest("Type de fichier non autorisé pour l'image de catégorie.");
                        }

                        if (imageFile.Length > 5 * 1024 * 1024)
                        {
                            await session.DisposeAsync();
                            return BadRequest("L'image dépasse 5MB.");
                        }

                        // Supprimer ancienne image (si locale)
                        if (!string.IsNullOrEmpty(existingPath) && existingPath.StartsWith("/images/categories/"))
                        {
                            var oldPhysical = Path.Combine(_hostingEnvironment.WebRootPath, existingPath.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));
                            if (System.IO.File.Exists(oldPhysical))
                            {
                                System.IO.File.Delete(oldPhysical);
                            }
                        }

                        var fileName = Path.GetFileNameWithoutExtension(imageFile.FileName);
                        var uniqueFileName = $"{fileName}_{Guid.NewGuid()}{ext}";
                        var uploadDir = Path.Combine(_hostingEnvironment.WebRootPath, "images", "categories");
                        if (!Directory.Exists(uploadDir))
                            Directory.CreateDirectory(uploadDir);

                        var uploadPath = Path.Combine(uploadDir, uniqueFileName);
                        using (var stream = new FileStream(uploadPath, FileMode.Create))
                        {
                            await imageFile.CopyToAsync(stream);
                        }

                        imagePath = "/images/categories/" + uniqueFileName;
                    }
                    catch (Exception exImg)
                    {
                        _logger.LogError($"Erreur upload image catégorie: {exImg.Message}");
                    }
                }

                var updateQuery = @"
                    MATCH (cat:CustomCategory {id: $id})
                    SET cat.nom = $nom,
                        cat.description = $description,
                        cat.ImagePath = $ImagePath
                    RETURN cat.id as Id";

                await session.RunAsync(updateQuery, new
                {
                    id,
                    nom,
                    description,
                    ImagePath = imagePath
                });

                await session.DisposeAsync();
                _logger.LogInformation($"Catégorie modifiée: {nom}");
                return Ok(new { message = "Catégorie modifiée avec succès" });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur ModifierCategorie: {ex.Message}");
                return BadRequest("Erreur lors de la modification");
            }
        }

        // POST: Admin/SupprimerCategorie
        [HttpPost]
        public async Task<IActionResult> SupprimerCategorie([FromBody] dynamic data)
        {
            if (!IsAdmin()) return Unauthorized();

            try
            {
                // Convertir dynamic en JsonElement pour accéder aux propriétés
                var jsonElement = System.Text.Json.JsonSerializer.SerializeToElement(data);
                int categorieId = jsonElement.GetProperty("categorieId").GetInt32();
                var session = _neo4jService.GetAsyncSession();
                var deleteQuery = @"
                    MATCH (cat:CustomCategory {id: $categorieId})
                    DETACH DELETE cat";

                await session.RunAsync(deleteQuery, new { categorieId });
                await session.DisposeAsync();

                _logger.LogInformation($"Catégorie #{categorieId} supprimée");
                return Ok(new { message = "Catégorie supprimée avec succès" });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur SupprimerCategorie: {ex.Message}");
                return BadRequest("Erreur lors de la suppression");
            }
        }

        // GET: Admin/Fillings
        public async Task<IActionResult> Fillings()
        {
            if (!IsAdmin())
            {
                TempData["Error"] = "Accès refusé";
                return RedirectToAction("Index", "Gateau");
            }

            try
            {
                var session = _neo4jService.GetAsyncSession();
                var query = @"
                    MATCH (f:SpongeFilling)
                    RETURN f.id as Id,
                           f.nom as Nom,
                           f.description as Description,
                           f.prixExtra as PrixExtra,
                           f.categorie as Categorie,
                           COALESCE(f.ImagePath, '') as ImagePath
                    ORDER BY f.nom";

                var result = await session.RunAsync(query);
                var fillings = new List<SpongeFilling>();

                await foreach (var record in result)
                {
                    fillings.Add(new SpongeFilling
                    {
                        Id = record["Id"].As<int>(),
                        Nom = record["Nom"].As<string>() ?? "",
                        Description = record["Description"].As<string>() ?? "",
                        PrixExtra = record["PrixExtra"].As<decimal>(),
                        Categorie = record["Categorie"].As<string>() ?? "",
                        ImagePath = record["ImagePath"].As<string>() ?? ""
                    });
                }

                await session.DisposeAsync();
                return View(fillings);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur Fillings: {ex.Message}");
                TempData["Error"] = "Erreur lors du chargement";
                return RedirectToAction("Dashboard");
            }
        }

        // POST: Admin/CreerFilling - avec upload d'image
        [HttpPost]
        public async Task<IActionResult> CreerFilling()
        {
            if (!IsAdmin()) return Unauthorized();

            try
            {
                var nom = Request.Form["nom"].ToString();
                var description = Request.Form["description"].ToString() ?? "";
                var prixExtra = decimal.Parse(Request.Form["prixExtra"].ToString() ?? "0");
                var categorie = Request.Form["categorie"].ToString() ?? "";
                var existingPath = Request.Form["ImagePath"].ToString() ?? "";
                var imageFile = Request.Form.Files.Count > 0 ? Request.Form.Files["imageFile"] : null;

                string imagePath = existingPath;

                if (imageFile != null && imageFile.Length > 0)
                {
                    var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                    var ext = Path.GetExtension(imageFile.FileName).ToLower();
                    if (!allowedExtensions.Contains(ext))
                    {
                        return BadRequest("Type de fichier non autorisé pour l'image de filling.");
                    }

                    if (imageFile.Length > 5 * 1024 * 1024)
                    {
                        return BadRequest("L'image dépasse 5MB.");
                    }

                    var fileName = Path.GetFileNameWithoutExtension(imageFile.FileName);
                    var uniqueFileName = $"{fileName}_{Guid.NewGuid()}{ext}";
                    var uploadDir = Path.Combine(_hostingEnvironment.WebRootPath, "images", "fillings");
                    if (!Directory.Exists(uploadDir))
                        Directory.CreateDirectory(uploadDir);

                    var uploadPath = Path.Combine(uploadDir, uniqueFileName);
                    using (var stream = new FileStream(uploadPath, FileMode.Create))
                    {
                        await imageFile.CopyToAsync(stream);
                    }

                    imagePath = "/images/fillings/" + uniqueFileName;
                }

                var session = _neo4jService.GetAsyncSession();

                var getMaxIdQuery = @"
                    OPTIONAL MATCH (f:SpongeFilling)
                    WITH MAX(f.id) as maxId
                    RETURN CASE WHEN maxId IS NULL THEN 1 ELSE maxId + 1 END as NextId";
                var maxIdResult = await session.RunAsync(getMaxIdQuery);
                await maxIdResult.FetchAsync();
                var newId = maxIdResult.Current["NextId"].As<int>();

                var createQuery = @"
                    CREATE (f:SpongeFilling {
                        id: $id,
                        nom: $nom,
                        description: $description,
                        prixExtra: $prixExtra,
                        categorie: $categorie,
                        ImagePath: $ImagePath
                    })
                    RETURN f.id as Id";

                await session.RunAsync(createQuery, new
                {
                    id = newId,
                    nom,
                    description,
                    prixExtra,
                    categorie,
                    ImagePath = imagePath
                });

                await session.DisposeAsync();
                _logger.LogInformation($"Filling créé: {nom}");
                return Ok(new { message = "Filling créé avec succès", id = newId });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur CreerFilling: {ex.Message}");
                return BadRequest("Erreur lors de la création");
            }
        }

        // POST: Admin/ModifierFilling - avec upload d'image
        [HttpPost]
        public async Task<IActionResult> ModifierFilling()
        {
            if (!IsAdmin()) return Unauthorized();

            try
            {
                var id = int.Parse(Request.Form["id"].ToString() ?? "0");
                var nom = Request.Form["nom"].ToString();
                var description = Request.Form["description"].ToString() ?? "";
                var prixExtra = decimal.Parse(Request.Form["prixExtra"].ToString() ?? "0");
                var categorie = Request.Form["categorie"].ToString() ?? "";
                var existingPath = Request.Form["ImagePath"].ToString() ?? "";
                var imageFile = Request.Form.Files.Count > 0 ? Request.Form.Files["imageFile"] : null;

                string imagePath = existingPath;

                var session = _neo4jService.GetAsyncSession();

                if (imageFile != null && imageFile.Length > 0)
                {
                    try
                    {
                        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                        var ext = Path.GetExtension(imageFile.FileName).ToLower();
                        if (!allowedExtensions.Contains(ext))
                        {
                            await session.DisposeAsync();
                            return BadRequest("Type de fichier non autorisé pour l'image de filling.");
                        }

                        if (imageFile.Length > 5 * 1024 * 1024)
                        {
                            await session.DisposeAsync();
                            return BadRequest("L'image dépasse 5MB.");
                        }

                        // Supprimer ancienne image locale
                        if (!string.IsNullOrEmpty(existingPath) && existingPath.StartsWith("/images/fillings/"))
                        {
                            var oldPhysical = Path.Combine(_hostingEnvironment.WebRootPath, existingPath.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));
                            if (System.IO.File.Exists(oldPhysical))
                            {
                                System.IO.File.Delete(oldPhysical);
                            }
                        }

                        var fileName = Path.GetFileNameWithoutExtension(imageFile.FileName);
                        var uniqueFileName = $"{fileName}_{Guid.NewGuid()}{ext}";
                        var uploadDir = Path.Combine(_hostingEnvironment.WebRootPath, "images", "fillings");
                        if (!Directory.Exists(uploadDir))
                            Directory.CreateDirectory(uploadDir);

                        var uploadPath = Path.Combine(uploadDir, uniqueFileName);
                        using (var stream = new FileStream(uploadPath, FileMode.Create))
                        {
                            await imageFile.CopyToAsync(stream);
                        }

                        imagePath = "/images/fillings/" + uniqueFileName;
                    }
                    catch (Exception exImg)
                    {
                        _logger.LogError($"Erreur upload image filling: {exImg.Message}");
                    }
                }

                var updateQuery = @"
                    MATCH (f:SpongeFilling {id: $id})
                    SET f.nom = $nom,
                        f.description = $description,
                        f.prixExtra = $prixExtra,
                        f.categorie = $categorie,
                        f.ImagePath = $ImagePath
                    RETURN f.id as Id";

                await session.RunAsync(updateQuery, new
                {
                    id,
                    nom,
                    description,
                    prixExtra,
                    categorie,
                    ImagePath = imagePath
                });

                await session.DisposeAsync();
                _logger.LogInformation($"Filling modifié: {nom}");
                return Ok(new { message = "Filling modifié avec succès" });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur ModifierFilling: {ex.Message}");
                return BadRequest("Erreur lors de la modification");
            }
        }

        // POST: Admin/SupprimerFilling
        [HttpPost]
        public async Task<IActionResult> SupprimerFilling([FromBody] dynamic data)
        {
            if (!IsAdmin()) return Unauthorized();

            try
            {
                // Convertir dynamic en JsonElement pour accéder aux propriétés
                var jsonElement = System.Text.Json.JsonSerializer.SerializeToElement(data);
                int fillingId = jsonElement.GetProperty("fillingId").GetInt32();
                var session = _neo4jService.GetAsyncSession();
                var deleteQuery = @"
                    MATCH (f:SpongeFilling {id: $fillingId})
                    DETACH DELETE f";

                await session.RunAsync(deleteQuery, new { fillingId });
                await session.DisposeAsync();

                _logger.LogInformation($"Filling #{fillingId} supprimé");
                return Ok(new { message = "Filling supprimé avec succès" });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur SupprimerFilling: {ex.Message}");
                return BadRequest("Erreur lors de la suppression");
            }
        }
    }
}