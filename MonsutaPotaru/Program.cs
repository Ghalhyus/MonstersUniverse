using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MonsutaPotaru.Api;
using MonsutaPotaru.Components;
using MonsutaPotaru.Components.Account;
using MonsutaPotaru.Data;
using MonsutaPotaru.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

// Chemin SQLite relatif => relatif au dossier de l'application (et non au dossier courant).
var sqlite = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString);
if (!Path.IsPathRooted(sqlite.DataSource))
    sqlite.DataSource = Path.Combine(builder.Environment.ContentRootPath, sqlite.DataSource);
Directory.CreateDirectory(Path.GetDirectoryName(sqlite.DataSource)!);
connectionString = sqlite.ToString();
// La fabrique enregistre aussi ApplicationDbContext en "scoped" (utilisé par Identity).
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// Pseudo, connexion par l'adresse mail, messages en français : AccountService. Ni mails, ni double authentification :
// pas de fournisseurs de jetons.
builder.Services.AddIdentityCore<ApplicationUser>(AccountService.ConfigureIdentity)
    .AddRoles<IdentityRole>() // Standard, Administrateur (SiteRoles) : rôles dans le cookie de connexion
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddErrorDescriber<FrenchIdentityErrorDescriber>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<AdminService>();

// Clés qui chiffrent les cookies (connexion, formulaires) : gardées avec les données (volume Docker),
// sinon chaque mise à jour du conteneur en crée de nouvelles et déconnecte tout le monde.
var keysPath = Path.Combine(builder.Environment.ContentRootPath, builder.Configuration["Bank:KeysPath"] ?? "Data/keys");
builder.Services.AddDataProtection()
    // Ancien nom du site, gardé exprès : les cookies de connexion sont protégés sous ce nom, le changer déconnecterait
    // tout le monde.
    .SetApplicationName("PokeBank")
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath));

builder.Services.AddSingleton<PokemonInfo>();
builder.Services.AddScoped<BankService>();
builder.Services.AddScoped<StoredSaveService>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<ParkService>();
builder.Services.AddScoped<LivingDexService>();
builder.Services.AddScoped<WalletState>();
builder.Services.AddScoped<SaveSession>();
builder.Services.AddScoped<SyncService>();
builder.Services.AddScoped<PlayerService>();
builder.Services.AddSyncRateLimits(); // essais de codes de liaison 3DS

var app = builder.Build();

// Crée / met à jour la base SQLite au démarrage.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
    await AdminService.InitializeAsync(db, app.Configuration, app.Logger); // code d'invitation en base, premier administrateur
    await BankService.FillSortNamesAsync(db); // nom de tri des Pokémon enregistrés avant son ajout
    await BankService.FillSpriteDetailsAsync(db); // sexe et objet tenu (image) des Pokémon enregistrés avant leur ajout
    await BankService.FillFilterDetailsAsync(db); // types, nature, IV... (filtres de « Ma collection ») des Pokémon plus anciens
    await PokemonFingerprint.FillAsync(db); // empreinte (doublons) des Pokémon plus anciens
}
PokemonSprites.Load(app.Environment.WebRootPath); // images des Pokémon et de leurs formes

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

// Images (Pokémon, objets, sprites Donjon Mystère) : ni dans le dépôt ni dans la publication. En local, déposées par
// outils\recuperer-images.cmd ; en production, dossier du serveur (~/monsuta-potaru-images) monté à la place de wwwroot/images.
// Servies depuis le disque : MapStaticAssets ne connaît que les fichiers présents à la compilation.
var images = Path.Combine(app.Environment.WebRootPath, "images");
Directory.CreateDirectory(images); // absentes (nouveau poste) : le site affiche les images de remplacement
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(images),
    RequestPath = "/images",
});


app.UseRateLimiter();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

// API du homebrew 3DS PokeBank Sync (envoi des sauvegardes depuis la console, jeton au lieu du cookie de connexion).
app.MapSyncEndpoints();

app.Run();
