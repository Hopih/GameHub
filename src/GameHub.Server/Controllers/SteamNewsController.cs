using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameHub.Server.Infrastructure;
using Npgsql;

namespace GameHub.Server.Controllers;

internal sealed class SteamNewsController(SteamClient steam , Database database)
{
    public Task SearchAsync(HttpListenerContext context)
    {
        var title = context.Request.QueryString["q"]?.Trim();
        if (title is null || title.Length is 0 or > 100)
            return ResponseWriter.SendAsync(context.Response, "Введите название игры длиной до 100 символов.", HttpStatusCode.BadRequest);

        return SendResultsAsync(context.Response, token => steam.SearchAsync(title, token));
    }

    public async Task SearchRecommendedAsync(HttpListenerContext context , Guid userId , ClaimsIdentity identity)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var command =
            new NpgsqlCommand("""  SELECT game FROM public.user_favorites WHERE user_id = @userId;  """, connection);
        command.Parameters.AddWithValue("userId", userId);
        await using var reader = await command.ExecuteReaderAsync();
        var titles = new List<string>();
        var newsList = new List<SteamPost>();
        
        while (await reader.ReadAsync())
        {
            using var game = JsonDocument.Parse(reader.GetString(0));
            if (game.RootElement.GetProperty("name").GetString() is string title)
                titles.Add(title);
        }
        await SendResultsAsync(context.Response, async token =>
        {
            foreach (var name in titles)
            {
                var search = await steam.SearchAsync(name, token);

                foreach (var match in search)
                {
                    var news = await steam.GetNewsAsync(match.AppId, token);

                    if (news.Length > 0)
                        newsList.Add(news[0]);
                }
            }
            
            return newsList.ToArray();
        });
    }
    
    public Task GetNewsAsync(HttpListenerContext context)
    {
        if (!uint.TryParse(context.Request.QueryString["appid"], out uint appId) || appId == 0)
            return ResponseWriter.SendAsync(context.Response, "Укажите действительный Steam AppID.", HttpStatusCode.BadRequest);

        return SendResultsAsync(context.Response, token => steam.GetNewsAsync(appId, token));
    }

    private static async Task SendResultsAsync<T>(HttpListenerResponse response, Func<CancellationToken, Task<T[]>> load)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        T[] results;
        try
        {
            results = await load(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            await ResponseWriter.SendAsync(response, "Steam не ответил вовремя.", HttpStatusCode.GatewayTimeout);
            return;
        }
        catch (Exception error) when (error is HttpRequestException or JsonException)
        {
            await ResponseWriter.SendAsync(response, "Не удалось получить данные из Steam.", HttpStatusCode.BadGateway);
            return;
        }

        await ResponseWriter.SendAsync(response, JsonSerializer.Serialize(new { results }, JsonSerializerOptions.Web),
            HttpStatusCode.OK, "application/json; charset=utf-8");
    }
    
    private sealed record SearchRecommendationRequest(
        [property: JsonRequired] string[]? Title);
}
