using GameHub.Server;
using GameHub.Server.Controllers;
using GameHub.Server.Infrastructure;
using System.Text.Json;

var router = new Router();
var homeController = new HomeController();
string? apiKey = Environment.GetEnvironmentVariable("RAWG_API_KEY");
string? connectionString = Environment.GetEnvironmentVariable("GAMEHUB_CONNECTION_STRING");
string settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.Local.json");

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
}

if (string.IsNullOrWhiteSpace(apiKey))
{
    throw new InvalidOperationException("Задайте RAWG_API_KEY или RawgApiKey в appsettings.Local.json.");
}

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Задайте GAMEHUB_CONNECTION_STRING или DatabaseConnectionString в appsettings.Local.json.");
}

await using var database = new Database(connectionString);
var healthController = new HealthController(database);
using var httpClient = new HttpClient();
var rawgClient = new RawgClient(httpClient, apiKey);
var gamesController = new GamesController(rawgClient);
router.Register("GET", "/health", healthController.CheckHealthAsync);
router.Register("GET", "/health/db", healthController.CheckDatabaseAsync);
router.Register("GET", "/", homeController.IndexAsync);
router.Register("GET","/games", gamesController.GetAllAsync);
router.Register("GET", "/games/featured", gamesController.GetFeaturedAsync);

var staticFiles = new StaticFileHandler(Path.Combine(AppContext.BaseDirectory, "wwwroot"));
var server = new HttpServer(router, staticFiles);
string prefix = args.Length > 0 ? args[0] : "http://localhost:8080/";
await server.RunAsync(prefix);
