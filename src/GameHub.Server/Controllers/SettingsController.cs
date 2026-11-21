using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameHub.Server.Infrastructure;
using Npgsql;

namespace GameHub.Server.Controllers;

public sealed class SettingsController(Database database)
{
    private const int MaxSettingsJsonBytes = 1024;
    private static readonly RecommendationSettings DefaultSettings = new(Description: true, Personal: false);

    public async Task GetAsync(HttpListenerContext context, Guid userId, ClaimsIdentity identity)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            SELECT by_description, by_personal
            FROM public.user_recommendation_settings
            WHERE user_id = @userId;
            """, connection);
        command.Parameters.AddWithValue("userId", userId);

        await using var reader = await command.ExecuteReaderAsync();
        var settings = await reader.ReadAsync()
            ? new RecommendationSettings(reader.GetBoolean(0), reader.GetBoolean(1))
            : DefaultSettings;

        await ResponseWriter.SendAsync(
            context.Response, JsonSerializer.Serialize(settings, JsonSerializerOptions.Web),
            HttpStatusCode.OK, "application/json; charset=utf-8");
    }

    public async Task SaveAsync(HttpListenerContext context, Guid userId, ClaimsIdentity identity)
    {
        var buffer = new byte[MaxSettingsJsonBytes + 1];
        var bytesRead = await context.Request.InputStream.ReadAtLeastAsync(buffer, buffer.Length, false);
        if (bytesRead > MaxSettingsJsonBytes)
        {
            await ResponseWriter.SendAsync(
                context.Response, "Слишком большой запрос.", HttpStatusCode.RequestEntityTooLarge);
            return;
        }

        RecommendationSettings? settings = null;
        try
        {
            settings = JsonSerializer.Deserialize<RecommendationSettings>(
                buffer.AsSpan(0, bytesRead), JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            // Некорректное тело обрабатывается как 400.
        }

        if (settings is null)
        {
            await ResponseWriter.SendAsync(
                context.Response, "Укажите логические значения description и personal.", HttpStatusCode.BadRequest);
            return;
        }

        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO public.user_recommendation_settings (user_id, by_description, by_personal)
            VALUES (@userId, @description, @personal)
            ON CONFLICT (user_id) DO UPDATE
            SET by_description = excluded.by_description, by_personal = excluded.by_personal;
            """, connection);
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("description", settings.Description);
        command.Parameters.AddWithValue("personal", settings.Personal);
        await command.ExecuteNonQueryAsync();

        context.Response.StatusCode = (int)HttpStatusCode.NoContent;
        context.Response.Close();
    }

    private sealed record RecommendationSettings(
        [property: JsonRequired] bool Description,
        [property: JsonRequired] bool Personal);
}
