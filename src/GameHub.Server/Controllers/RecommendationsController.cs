using System.Net;
using System.Text.Json;
using GameHub.Server.Infrastructure;

namespace GameHub.Server.Controllers;

public sealed class RecommendationsController(SemanticRecommendations recommendations)
{
    public async Task SearchAsync(HttpListenerContext context)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        if (context.Request.ContentLength64 is < 0 or > 8192)
        {
            await ResponseWriter.SendAsync(context.Response, "Слишком большой запрос.", HttpStatusCode.RequestEntityTooLarge);
            return;
        }

        string? description;
        try
        {
            var request = await JsonSerializer.DeserializeAsync<SearchRequest>(
                context.Request.InputStream, JsonSerializerOptions.Web, timeout.Token);
            description = request?.Description?.Trim();
        }
        catch (JsonException)
        {
            await ResponseWriter.SendAsync(context.Response, "Ожидается JSON с описанием игры.", HttpStatusCode.BadRequest);
            return;
        }

        if (string.IsNullOrEmpty(description) || description.Length > 1000)
        {
            await ResponseWriter.SendAsync(context.Response, "Введите описание длиной до 1000 символов.", HttpStatusCode.BadRequest);
            return;
        }

        SemanticRecommendations.RecommendationResult result;
        try
        {
            result = await recommendations.FindAsync(description, timeout.Token);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or InvalidDataException)
        {
            Console.Error.WriteLine($"Сервис рекомендаций недоступен ({error.GetType().Name}).");
            await ResponseWriter.SendAsync(
                context.Response, "Поиск по описанию временно недоступен. Попробуй ещё раз.", HttpStatusCode.ServiceUnavailable);
            return;
        }

        await ResponseWriter.SendAsync(context.Response, JsonSerializer.Serialize(result, JsonSerializerOptions.Web),
            HttpStatusCode.OK, "application/json; charset=utf-8");
    }

    private sealed record SearchRequest(string? Description);
}
