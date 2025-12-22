using Microsoft.AspNetCore.Mvc;
using project.Models;
using Services;
using Neo4j.Driver;
using System.Security.Cryptography;
using System.Text;

namespace project.Controllers
{
    public class AuthController : Controller
    {
        private readonly Neo4jService _neo4jService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(Neo4jService neo4jService, ILogger<AuthController> logger)
        {
            _neo4jService = neo4jService;
            _logger = logger;
        }

        private string HashPassword(string password)
        {
            using (var sha256 = SHA256.Create())
            {
                var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                return Convert.ToBase64String(hashedBytes);
            }
        }

        // POST: Auth/Register
        [HttpPost]
        public async Task<IActionResult> Register([FromBody] RegisterModel model)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                    return BadRequest(string.Join(", ", errors));
                }

                var session = _neo4jService.GetAsyncSession();

                // Vérifier si l'email existe déjà
                var checkQuery = "MATCH (c:Client {email: $email}) RETURN c";
                var checkResult = await session.RunAsync(checkQuery, new { email = model.Email });

                if (await checkResult.FetchAsync())
                {
                    await session.DisposeAsync();
                    return BadRequest("Un compte avec cet email existe déjà");
                }

                // Générer un ID unique pour le nouveau client
                var getMaxIdQuery = @"
                    OPTIONAL MATCH (c:Client)
                    WITH MAX(c.id) as maxId
                    RETURN CASE WHEN maxId IS NULL THEN 1 ELSE maxId + 1 END as NextId";

                var maxIdResult = await session.RunAsync(getMaxIdQuery);
                await maxIdResult.FetchAsync();
                var newClientId = maxIdResult.Current["NextId"].As<int>();

                // Créer le nouveau client avec mot de passe hashé
                var hashedPassword = HashPassword(model.Mdp);

                var query = @"
                    CREATE (c:Client {
                        id: $id,
                        nom: $nom,
                        prenom: $prenom,
                        email: $email,
                        mdp: $mdp,
                        telephone: $telephone,
                        adresse: $adresse,
                        role: 'client',
                        dateInscription: datetime()
                    })
                    RETURN c.id as ClientId, c.nom as Nom, c.prenom as Prenom, c.email as Email, c.role as Role";

                var result = await session.RunAsync(query, new
                {
                    id = newClientId,
                    nom = model.Nom,
                    prenom = model.Prenom,
                    email = model.Email,
                    mdp = hashedPassword,
                    telephone = model.Telephone,
                    adresse = ""
                });

                if (await result.FetchAsync())
                {
                    var record = result.Current;
                    var clientId = record["ClientId"].As<int>();
                    var role = record["Role"].As<string>();

                    // Enregistrer l'utilisateur dans la session
                    HttpContext.Session.SetInt32("ClientId", clientId);
                    HttpContext.Session.SetString("ClientEmail", model.Email);
                    HttpContext.Session.SetString("ClientNom", model.Nom);
                    HttpContext.Session.SetString("ClientPrenom", model.Prenom);
                    HttpContext.Session.SetString("ClientRole", role);

                    _logger.LogInformation($"Nouveau client créé : {model.Email} avec ID {clientId}");
                }

                await session.DisposeAsync();
                return Ok(new { message = "Inscription réussie" });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur inscription: {ex.Message}");
                return BadRequest($"Erreur: {ex.Message}");
            }
        }

        // POST: Auth/Login
        [HttpPost]
        public async Task<IActionResult> Login([FromBody] LoginModel model)
        {
            try
            {
                _logger.LogInformation($"=== TENTATIVE LOGIN ===");
                _logger.LogInformation($"Email reçu: {model?.Email}");
                _logger.LogInformation($"Mdp reçu: {(model?.Mdp != null ? "***" : "NULL")}");

                if (!ModelState.IsValid)
                {
                    var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                    var errorMsg = string.Join(", ", errors);
                    _logger.LogError($"ModelState invalide: {errorMsg}");
                    return BadRequest(errorMsg);
                }

                var session = _neo4jService.GetAsyncSession();

                var hashedPassword = HashPassword(model.Mdp);
                _logger.LogInformation($"Password hashé: {hashedPassword}");

                // D'abord, trouver le client par email uniquement
                var query = @"
                    MATCH (c:Client {email: $email})
                    RETURN c.id as ClientId, 
                           c.nom as Nom, 
                           c.prenom as Prenom, 
                           c.email as Email,
                           c.role as Role,
                           c.mdp as MdpHash
                    LIMIT 1";

                var result = await session.RunAsync(query, new
                {
                    email = model.Email
                });

                if (!await result.FetchAsync())
                {
                    _logger.LogWarning($"Aucun client trouvé pour: {model.Email}");
                    await session.DisposeAsync();
                    return BadRequest("Email ou mot de passe incorrect");
                }

                var record = result.Current;
                var storedPasswordHash = record["MdpHash"].As<string?>() ?? "";

                _logger.LogInformation($"Password hash stocké dans DB: {storedPasswordHash}");
                _logger.LogInformation($"Password hash calculé: {hashedPassword}");
                _logger.LogInformation($"Les hash correspondent: {storedPasswordHash == hashedPassword}");

                // Vérifier si le mot de passe correspond
                if (storedPasswordHash != hashedPassword)
                {
                    _logger.LogWarning($"Mot de passe incorrect pour: {model.Email}");
                    await session.DisposeAsync();
                    return BadRequest("Email ou mot de passe incorrect");
                }

                var clientId = record["ClientId"].As<int>();
                var nom = record["Nom"].As<string>();
                var prenom = record["Prenom"].As<string>();
                var role = record["Role"].As<string?>() ?? "client";

                _logger.LogInformation($"Client trouvé: {nom} {prenom} - Role: {role}");

                // Enregistrer l'utilisateur dans la session
                HttpContext.Session.SetInt32("ClientId", clientId);
                HttpContext.Session.SetString("ClientEmail", model.Email);
                HttpContext.Session.SetString("ClientNom", nom);
                HttpContext.Session.SetString("ClientPrenom", prenom);
                HttpContext.Session.SetString("ClientRole", role);

                _logger.LogInformation($"Connexion réussie : {model.Email} (Role: {role})");

                await session.DisposeAsync();

                // Rediriger vers le dashboard si admin
                if (role == "admin")
                {
                    _logger.LogInformation($"Redirection vers /Admin/Dashboard");
                    return Ok(new { message = "Connexion réussie", redirect = "/Admin/Dashboard" });
                }

                return Ok(new { message = "Connexion réussie" });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur connexion: {ex.Message}");
                _logger.LogError($"Stack trace: {ex.StackTrace}");
                return BadRequest($"Erreur: {ex.Message}");
            }
        }

        // POST: Auth/Logout
        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            try
            {
                var clientId = HttpContext.Session.GetInt32("ClientId");

                // Vider le panier si l'utilisateur était connecté
                if (clientId.HasValue)
                {
                    var session = _neo4jService.GetAsyncSession();

                    var deleteQuery = @"
                        MATCH (p:Panier {clientId: $clientId})
                        DETACH DELETE p";

                    await session.RunAsync(deleteQuery, new { clientId = clientId.Value });
                    await session.DisposeAsync();

                    _logger.LogInformation($"Panier vidé pour le client {clientId}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur lors du vidage du panier: {ex.Message}");
            }

            HttpContext.Session.Clear();
            _logger.LogInformation("Utilisateur déconnecté");
            return RedirectToAction("Index", "Gateau");
        }
    }
}