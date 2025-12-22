using Microsoft.AspNetCore.Mvc;
using project.Models;
using Services;
using Neo4j.Driver;

namespace project.Controllers
{
    public class GateauController : Controller
    {
        private readonly Neo4jService _neo4jService;
        private readonly ILogger<GateauController> _logger;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public GateauController(Neo4jService neo4jService, ILogger<GateauController> logger, IWebHostEnvironment webHostEnvironment)
        {
            _neo4jService = neo4jService;
            _logger = logger;
            _webHostEnvironment = webHostEnvironment;
        }

        // GET: Gateau/Index - Page d'ACCUEIL (4 gâteaux)
        public async Task<IActionResult> Index()
        {
            var gateaux = new List<Gateau>();

            try
            {
                var session = _neo4jService.GetAsyncSession();

                // Exclure les articles custom de l'accueil
                var query = @"
                    MATCH (g:Gateau)
                    WHERE g.categorie <> 'Custom'
                    RETURN 
                        g.id as Id,
                        g.nom as Nom,
                        g.description as Description,
                        g.prix as Prix,
                        COALESCE(g.stock, 1) as Stock,
                        g.categorie as Categorie,
                        COALESCE(g.ImagePath, g.imagePath, '') as ImagePath
                    ORDER BY g.nom
                    LIMIT 4";

                var result = await session.RunAsync(query);

                await foreach (var record in result)
                {
                    var stock = record["Stock"].As<int>();
                    if (stock > 0)
                    {
                        gateaux.Add(new Gateau
                        {
                            Id = record["Id"].As<int>(),
                            Nom = record["Nom"].As<string>() ?? "",
                            Description = record["Description"].As<string>() ?? "",
                            Prix = record["Prix"].As<decimal>(),
                            Stock = stock,
                            Categorie = record["Categorie"].As<string>() ?? "",
                            ImagePath = record["ImagePath"].As<string>() ?? ""
                        });
                    }
                }

                await session.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur: {ex.Message}");
            }

            return View(gateaux);
        }

        // GET: Gateau/Catalogue - TOUS les gâteaux (SAUF Custom)
        public async Task<IActionResult> Catalogue()
        {
            var gateaux = new List<Gateau>();

            try
            {
                var session = _neo4jService.GetAsyncSession();

                // IMPORTANT : Exclure les gâteaux custom du catalogue
                var query = @"
                    MATCH (g:Gateau)
                    WHERE g.categorie <> 'Custom'
                    RETURN 
                        g.id as Id,
                        g.nom as Nom,
                        g.description as Description,
                        g.prix as Prix,
                        COALESCE(g.stock, 1) as Stock,
                        g.categorie as Categorie,
                        COALESCE(g.ImagePath, g.imagePath, '') as ImagePath
                    ORDER BY g.nom";

                var result = await session.RunAsync(query);

                await foreach (var record in result)
                {
                    var stock = record["Stock"].As<int>();
                    if (stock > 0)
                    {
                        gateaux.Add(new Gateau
                        {
                            Id = record["Id"].As<int>(),
                            Nom = record["Nom"].As<string>() ?? "",
                            Description = record["Description"].As<string>() ?? "",
                            Prix = record["Prix"].As<decimal>(),
                            Stock = stock,
                            Categorie = record["Categorie"].As<string>() ?? "",
                            ImagePath = record["ImagePath"].As<string>() ?? ""
                        });
                    }
                }

                await session.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur: {ex.Message}");
            }

            return View(gateaux);
        }

        // GET: Gateau/Details/5
        public async Task<IActionResult> Details(int id)
        {
            try
            {
                var session = _neo4jService.GetAsyncSession();

                var query = @"
                    MATCH (g:Gateau)
                    WHERE g.id = $id
                    RETURN 
                        g.id as Id,
                        g.nom as Nom,
                        g.description as Description,
                        g.prix as Prix,
                        COALESCE(g.stock, 0) as Stock,
                        g.categorie as Categorie,
                        COALESCE(g.ImagePath, g.imagePath, '') as ImagePath
                    LIMIT 1";

                var result = await session.RunAsync(query, new { id });

                if (!await result.FetchAsync())
                {
                    await session.DisposeAsync();
                    return NotFound();
                }

                var record = result.Current;
                var gateau = new Gateau
                {
                    Id = record["Id"].As<int>(),
                    Nom = record["Nom"].As<string>() ?? "",
                    Description = record["Description"].As<string>() ?? "",
                    Prix = record["Prix"].As<decimal>(),
                    Stock = record["Stock"].As<int>(),
                    Categorie = record["Categorie"].As<string>() ?? "",
                    ImagePath = record["ImagePath"].As<string>() ?? ""
                };

                await session.DisposeAsync();
                return View(gateau);
            }
            catch
            {
                return NotFound();
            }
        }

        // POST: Gateau/Create - Créer un nouveau gâteau avec image
        [HttpPost]
        public async Task<IActionResult> Create(Gateau gateau, IFormFile imageFile)
        {
            try
            {
                string imagePath = "";

                // Sauvegarder l'image si elle existe
                if (imageFile != null && imageFile.Length > 0)
                {
                    // Génère un nom unique pour l'image
                    string fileName = Guid.NewGuid().ToString() + Path.GetExtension(imageFile.FileName);

                    // Chemin du dossier wwwroot/images/gateaux
                    string uploadPath = Path.Combine(_webHostEnvironment.WebRootPath, "images", "gateaux");

                    // Crée le dossier s'il n'existe pas
                    Directory.CreateDirectory(uploadPath);

                    // Chemin complet du fichier
                    string filePath = Path.Combine(uploadPath, fileName);

                    // Sauvegarde le fichier
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await imageFile.CopyToAsync(stream);
                    }

                    imagePath = fileName;
                }

                // Sauvegarde dans Neo4j
                var session = _neo4jService.GetAsyncSession();

                var query = @"
                    CREATE (g:Gateau {
                        id: $id,
                        nom: $nom,
                        description: $description,
                        prix: $prix,
                        stock: $stock,
                        categorie: $categorie,
                        ImagePath: $imagePath,
                        imagePath: $imagePath
                    })
                    RETURN g";

                await session.RunAsync(query, new
                {
                    id = gateau.Id,
                    nom = gateau.Nom,
                    description = gateau.Description,
                    prix = gateau.Prix,
                    stock = gateau.Stock,
                    categorie = gateau.Categorie,
                    imagePath = imagePath
                });

                await session.DisposeAsync();

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur lors de la création: {ex.Message}");
                return View(gateau);
            }
        }

        // POST: Gateau/Update - Modifier un gâteau avec nouvelle image
        [HttpPost]
        public async Task<IActionResult> Update(int id, Gateau gateau, IFormFile imageFile)
        {
            try
            {
                var session = _neo4jService.GetAsyncSession();

                // Récupère l'ancienne image
                var getQuery = @"
                    MATCH (g:Gateau)
                    WHERE g.id = $id
                    RETURN COALESCE(g.ImagePath, g.imagePath, '') as ImagePath";

                var getResult = await session.RunAsync(getQuery, new { id });
                string oldImagePath = "";

                if (await getResult.FetchAsync())
                {
                    oldImagePath = getResult.Current["ImagePath"].As<string>() ?? "";
                }

                string newImagePath = oldImagePath;

                // Si une nouvelle image est uploadée
                if (imageFile != null && imageFile.Length > 0)
                {
                    // Supprime l'ancienne image
                    if (!string.IsNullOrEmpty(oldImagePath))
                    {
                        string oldFilePath = Path.Combine(_webHostEnvironment.WebRootPath, "images", "gateaux", oldImagePath);
                        if (System.IO.File.Exists(oldFilePath))
                        {
                            System.IO.File.Delete(oldFilePath);
                        }
                    }

                    // Crée une nouvelle image
                    string fileName = Guid.NewGuid().ToString() + Path.GetExtension(imageFile.FileName);
                    string uploadPath = Path.Combine(_webHostEnvironment.WebRootPath, "images", "gateaux");
                    Directory.CreateDirectory(uploadPath);

                    string filePath = Path.Combine(uploadPath, fileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await imageFile.CopyToAsync(stream);
                    }

                    newImagePath = fileName;
                }

                // Met à jour dans Neo4j
                var updateQuery = @"
                    MATCH (g:Gateau)
                    WHERE g.id = $id
                    SET 
                        g.nom = $nom,
                        g.description = $description,
                        g.prix = $prix,
                        g.stock = $stock,
                        g.categorie = $categorie,
                        g.ImagePath = $imagePath,
                        g.imagePath = $imagePath
                    RETURN g";

                await session.RunAsync(updateQuery, new
                {
                    id = id,
                    nom = gateau.Nom,
                    description = gateau.Description,
                    prix = gateau.Prix,
                    stock = gateau.Stock,
                    categorie = gateau.Categorie,
                    imagePath = newImagePath
                });

                await session.DisposeAsync();

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur lors de la mise à jour: {ex.Message}");
                return View(gateau);
            }
        }

        // POST: Gateau/Delete - Supprimer un gâteau et son image
        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var session = _neo4jService.GetAsyncSession();

                // Récupère le chemin de l'image
                var getQuery = @"
                    MATCH (g:Gateau)
                    WHERE g.id = $id
                    RETURN COALESCE(g.ImagePath, g.imagePath, '') as ImagePath";

                var getResult = await session.RunAsync(getQuery, new { id });

                if (await getResult.FetchAsync())
                {
                    string imagePath = getResult.Current["ImagePath"].As<string>() ?? "";

                    // Supprime le fichier image
                    if (!string.IsNullOrEmpty(imagePath))
                    {
                        string filePath = Path.Combine(_webHostEnvironment.WebRootPath, "images", "gateaux", imagePath);
                        if (System.IO.File.Exists(filePath))
                        {
                            System.IO.File.Delete(filePath);
                        }
                    }
                }

                // Supprime le nœud dans Neo4j
                var deleteQuery = @"
                    MATCH (g:Gateau)
                    WHERE g.id = $id
                    DELETE g";

                await session.RunAsync(deleteQuery, new { id });
                await session.DisposeAsync();

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur lors de la suppression: {ex.Message}");
                return RedirectToAction(nameof(Index));
            }
        }
    }
}