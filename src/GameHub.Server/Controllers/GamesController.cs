using System.Net;
using System.Text.Json;
using GameHub.Server.Infrastructure;

namespace GameHub.Server.Controllers;

public class GamesController
{
    private readonly RawgClient _rawgClient;
    
    public GamesController(RawgClient rawgClient)
    {
        _rawgClient = rawgClient;
    }
    
    public async Task GetAllAsync(HttpListenerContext context)
    {
        string rawgJson = await _rawgClient.GetGamesAsync();
        using var document = JsonDocument.Parse(rawgJson);

        var response = new
        {
            count = document.RootElement.GetProperty("count").GetInt32(),
            results = document.RootElement.GetProperty("results")
        };

        string responseJson = JsonSerializer.Serialize(response);
        await ResponseWriter.SendAsync(
            context.Response,
            responseJson,
            HttpStatusCode.OK,
            "application/json; charset=utf-8");
    }

    public async Task GetFeaturedAsync(HttpListenerContext context)
    {
        // Редакционная подборка: данные и изображения берём из RAWG.
        string[] slugs = ["elden-ring", "hollow-knight"];
        string[] rawgGames = await Task.WhenAll(slugs.Select(slug => _rawgClient.GetGameAsync(slug)));
        var games = rawgGames.Select(json =>
        {
            using var document = JsonDocument.Parse(json);
            var game = document.RootElement;
            return new
            {
                id = game.GetProperty("id").GetInt32(),
                name = game.GetProperty("name").GetString(),
                slug = game.GetProperty("slug").GetString(),
                background_image = game.GetProperty("background_image").GetString()
            };
        }).ToArray();

        await ResponseWriter.SendAsync(
            context.Response,
            JsonSerializer.Serialize(new { results = games }),
            HttpStatusCode.OK,
            "application/json; charset=utf-8");
    }
}
