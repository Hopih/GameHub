using System.Net;
using System.Text.Json;
using GameHub.Server.Infrastructure;

namespace GameHub.Server.Controllers;

internal sealed class SteamNewsController(SteamClient steam)
{
    public Task SearchAsync(HttpListenerContext context)
    {
        var title = context.Request.QueryString["q"]?.Trim();
        if (title is null || title.Length is 0 or > 100)
            return ResponseWriter.SendAsync(context.Response, "Введите название игры длиной до 100 символов.", HttpStatusCode.BadRequest);

        return SendResultsAsync(context.Response, token => steam.SearchAsync(title, token));
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
}
