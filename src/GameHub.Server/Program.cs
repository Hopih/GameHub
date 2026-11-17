using GameHub.Server;
using GameHub.Server.Controllers;
using GameHub.Server.Infrastructure;
using System.Text.Json;

var router = new Router();
var healthController = new HealthController();
var homeController = new HomeController();
string? apiKey = Environment.GetEnvironmentVariable("RAWG_API_KEY");
string settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.Local.json");

if (string.IsNullOrWhiteSpace(apiKey) && File.Exists(settingsPath))
{
    using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
    apiKey = settings.RootElement.GetProperty("RawgApiKey").GetString();
}

if (string.IsNullOrWhiteSpace(apiKey))
{
    throw new InvalidOperationException("Задайте RAWG_API_KEY или RawgApiKey в appsettings.Local.json.");
}

using var httpClient = new HttpClient();
var rawgClient = new RawgClient(httpClient, apiKey);
var gamesController = new GamesController(rawgClient);
router.Register("GET", "/health", healthController.CheckHealthAsync);
router.Register("GET", "/", homeController.IndexAsync);
router.Register("GET","/games", gamesController.GetAllAsync);
router.Register("GET", "/games/featured", gamesController.GetFeaturedAsync);

var staticFiles = new StaticFileHandler(Path.Combine(AppContext.BaseDirectory, "wwwroot"));
var server = new HttpServer(router, staticFiles);
string prefix = args.Length > 0 ? args[0] : "http://localhost:8080/";
await server.RunAsync(prefix);
