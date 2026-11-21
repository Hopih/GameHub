using GameHub.Server;
using GameHub.Server.Controllers;
using GameHub.Server.Infrastructure;
using GameHub.Server.Auth;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

var router = new Router();
var homeController = new HomeController();
string? apiKey = Environment.GetEnvironmentVariable("RAWG_API_KEY");
string? connectionString = Environment.GetEnvironmentVariable("GAMEHUB_CONNECTION_STRING");
string? localHttpPrefix = null;
string settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.Local.json");
var jwtOptions = new JwtOptions();

if (File.Exists(settingsPath))
{
    using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
    if (string.IsNullOrWhiteSpace(apiKey)
        && settings.RootElement.TryGetProperty("RawgApiKey", out var rawgApiKey))
    {
        apiKey = rawgApiKey.GetString();
    }

    if (string.IsNullOrWhiteSpace(connectionString)
        && settings.RootElement.TryGetProperty("DatabaseConnectionString", out var databaseConnectionString))
    {
        connectionString = databaseConnectionString.GetString();
    }

    if (settings.RootElement.TryGetProperty(JwtOptions.Section, out var jwtSettings))
        jwtOptions = jwtSettings.Deserialize<JwtOptions>(JsonSerializerOptions.Web) ?? jwtOptions;

    if (settings.RootElement.TryGetProperty("HttpPrefix", out var httpPrefix))
        localHttpPrefix = httpPrefix.GetString();
}

if (string.IsNullOrWhiteSpace(apiKey))
{
    throw new InvalidOperationException("Задайте RAWG_API_KEY или RawgApiKey в appsettings.Local.json.");
}

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Задайте GAMEHUB_CONNECTION_STRING или DatabaseConnectionString в appsettings.Local.json.");
}

var jwtKey = Environment.GetEnvironmentVariable("GAMEHUB_JWT_KEY");
if (!string.IsNullOrWhiteSpace(jwtKey))
    jwtOptions.Key = jwtKey;

if (string.IsNullOrWhiteSpace(jwtOptions.Key) || Encoding.UTF8.GetByteCount(jwtOptions.Key) < 32)
    throw new InvalidOperationException("Задайте ключ JWT длиной не менее 32 байт в GAMEHUB_JWT_KEY или Jwt.Key в appsettings.Local.json.");

if (string.IsNullOrWhiteSpace(jwtOptions.Issuer) || string.IsNullOrWhiteSpace(jwtOptions.Audience) ||
    jwtOptions.AccessTokenMinutes <= 0 || jwtOptions.RefreshTokenDays <= 0)
    throw new InvalidOperationException("Проверьте издателя, получателя и сроки действия в разделе Jwt.");

await using var database = new Database(connectionString);
var healthController = new HealthController(database);
var tokenService = new TokenService(Options.Create(jwtOptions));
var sessions = new RefreshSessions(database, tokenService);
var authorizeController = new AuthorizeController(database, tokenService, sessions);
var favoritesController = new FavoritesController(database);
using var httpClient = new HttpClient();
var rawgClient = new RawgClient(httpClient, apiKey);
var gamesController = new GamesController(rawgClient);
var recommendations = new SemanticRecommendations(
    httpClient,
    rawgClient,
    new Uri(Environment.GetEnvironmentVariable("GAMEHUB_EMBEDDINGS_URL") ?? "http://127.0.0.1:18081"),
    new Uri(Environment.GetEnvironmentVariable("GAMEHUB_QDRANT_URL") ?? "http://127.0.0.1:6333"));
var recommendationsController = new RecommendationsController(recommendations);
router.Register("GET", "/health", healthController.CheckHealthAsync);
router.Register("GET", "/health/db", healthController.CheckDatabaseAsync);
router.Register("GET", "/", homeController.IndexAsync);
router.Register("GET", "/library", homeController.LibraryAsync);
router.Register("GET", "/recommendations", homeController.RecommendationsAsync);
router.Register("GET","/games", gamesController.GetAllAsync);
router.Register("GET", "/games/featured", gamesController.GetFeaturedAsync);
router.Register("POST", "/games/recommendations", recommendationsController.SearchAsync);
router.Register("POST", "/register", authorizeController.RegisterAsync);
router.Register("POST", "/login", authorizeController.LoginAsync);
router.Register("POST", "/auth/refresh", authorizeController.RefreshAsync);
router.Register("POST", "/auth/logout", authorizeController.LogoutAsync);
router.RegisterProtected("GET", "/auth/me", tokenService, authorizeController.AuthAsync);
router.RegisterProtected("GET", "/favorites", tokenService, favoritesController.ListAsync);
router.RegisterProtected("PUT", "/favorites", tokenService, favoritesController.SaveAsync);
router.RegisterProtected("DELETE", "/favorites", tokenService, favoritesController.DeleteAsync);
router.RegisterProtected("DELETE","/favorites/clear", tokenService, favoritesController.ClearAsync);

var staticFiles = new StaticFileHandler(Path.Combine(AppContext.BaseDirectory, "wwwroot"));
var server = new HttpServer(router, staticFiles);
string prefix = args.Length > 0 ? args[0] : localHttpPrefix ?? "http://localhost:8080/";
using var cleanupCancellation = new CancellationTokenSource();
var cleanup = sessions.RunCleanupAsync(cleanupCancellation.Token);
try
{
    await server.RunAsync(prefix);
}
finally
{
    cleanupCancellation.Cancel();
    await cleanup;
}
