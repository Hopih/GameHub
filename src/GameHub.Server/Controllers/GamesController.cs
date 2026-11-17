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
}
