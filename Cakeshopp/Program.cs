using Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Configuration Neo4j - VERSION SIMPLE
var neo4jConfig = builder.Configuration.GetSection("Neo4j");
builder.Services.AddSingleton(new Neo4jService(
    neo4jConfig["Uri"] ?? "neo4j://127.0.0.1:7687",
    neo4jConfig["Username"] ?? "neo4j",
    neo4jConfig["Password"] ?? "",
    neo4jConfig["Database"] ?? "gateaux"
));

// ===== AJOUT DES SESSIONS =====
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});


var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();


app.UseSession();


app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Gateau}/{action=Index}/{id?}");

app.Run();