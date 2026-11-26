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
        string pageParameter = context.Request.QueryString["page"] ?? "1";
        if (!int.TryParse(pageParameter, out int page) || page < 1)
        {
            await ResponseWriter.SendAsync(context.Response, "Номер страницы должен быть положительным целым числом.", HttpStatusCode.BadRequest);
            return;
        }

        string ordering = context.Request.QueryString["ordering"] ?? "-added";
        string[] allowedOrderings = ["-added", "-released", "-metacritic", "name"];
        if (!allowedOrderings.Contains(ordering))
        {
            await ResponseWriter.SendAsync(context.Response, "Неизвестная сортировка.", HttpStatusCode.BadRequest);
            return;
        }

        string? search = context.Request.QueryString["search"]?.Trim();
        var games = await GetCatalogPageAsync(page, ordering, search);

        var response = new
        {
            page,
            ordering,
            hasNext = games.GetProperty("next").ValueKind == JsonValueKind.String,
            count = games.GetProperty("count").GetInt32(),
            results = games.GetProperty("results")
        };

        string responseJson = JsonSerializer.Serialize(response);
        await ResponseWriter.SendAsync(
            context.Response,
            responseJson,
            HttpStatusCode.OK,
            "application/json; charset=utf-8");
    }

    private async Task<JsonElement> GetCatalogPageAsync(int page, string ordering, string? search)
    {
        if (!string.IsNullOrEmpty(search))
        {
            try
            {
                var exact = await FetchPageAsync(page, searchExact: true);
                if (exact.GetProperty("count").GetInt32() > 0)
                    return exact;
            }
            catch (HttpRequestException error) when (page > 1 && error.StatusCode == HttpStatusCode.NotFound)
            {
                var firstPage = await FetchPageAsync(1, searchExact: true);
                if (firstPage.GetProperty("count").GetInt32() > 0)
                    throw;
            }
        }

        return await FetchPageAsync(page, searchExact: false);

        async Task<JsonElement> FetchPageAsync(int rawgPage, bool searchExact)
        {
            var json = await _rawgClient.GetGamesAsync(rawgPage, 40, ordering, search, searchExact: searchExact);
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
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
