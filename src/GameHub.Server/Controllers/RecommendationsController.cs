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

        string description;
        string[] tags;
        try
        {
            var request = await JsonSerializer.DeserializeAsync<SearchRequest>(
                context.Request.InputStream, JsonSerializerOptions.Web, timeout.Token);
            description = request?.Description?.Trim() ?? "";
            tags = request?.Tags?.Select(tag => tag?.Trim() ?? "").ToArray() ?? [];
        }
        catch (JsonException)
        {
            await ResponseWriter.SendAsync(context.Response, "Ожидается JSON с описанием игры или списком тегов.", HttpStatusCode.BadRequest);
            return;
        }

        if (description.Length > 1000 || tags.Length > 6 || tags.Any(tag => tag.Length is 0 or > 100)
            || description.Length == 0 && tags.Length == 0)
        {
            await ResponseWriter.SendAsync(context.Response, "Введите описание до 1000 символов или до 6 тегов по 100 символов.", HttpStatusCode.BadRequest);
            return;
        }

        SemanticRecommendations.RecommendationResult result;
        try
        {
            result = await recommendations.FindAsync(description, tags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), timeout.Token);
        }
        catch (GameTagsUnavailableException)
        {
            await ResponseWriter.SendAsync(context.Response, "Теги каталога ещё загружаются. Попробуй чуть позже.", HttpStatusCode.ServiceUnavailable);
            return;
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or InvalidDataException)
        {
            Console.Error.WriteLine($"Сервис рекомендаций недоступен ({error.GetType().Name}).");
            await ResponseWriter.SendAsync(
                context.Response, "Поиск игр временно недоступен. Попробуй ещё раз.", HttpStatusCode.ServiceUnavailable);
            return;
        }

        await ResponseWriter.SendAsync(context.Response, JsonSerializer.Serialize(result, JsonSerializerOptions.Web),
            HttpStatusCode.OK, "application/json; charset=utf-8");
    }

    private sealed record SearchRequest(string? Description, string?[]? Tags);
}
