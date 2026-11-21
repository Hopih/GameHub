using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameHub.Server.Infrastructure;
using Npgsql;
using NpgsqlTypes;

namespace GameHub.Server.Controllers;

public sealed class FavoritesController(Database database)
{
    private const int MaxGameJsonBytes = 16_384;

    public async Task ListAsync(HttpListenerContext context, Guid userId, ClaimsIdentity identity)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            SELECT game FROM public.user_favorites
            WHERE user_id = @userId ORDER BY added_at DESC, rawg_game_id;
            """, connection);
        command.Parameters.AddWithValue("userId", userId);

        var games = new List<JsonElement>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            games.Add(JsonSerializer.Deserialize<JsonElement>(reader.GetString(0)));

        await ResponseWriter.SendAsync(
            context.Response, JsonSerializer.Serialize(games, JsonSerializerOptions.Web),
            HttpStatusCode.OK, "application/json; charset=utf-8");
    }

    public async Task SaveAsync(HttpListenerContext context, Guid userId, ClaimsIdentity identity)
    {
        FavoriteGame? game = null;
        var buffer = new byte[MaxGameJsonBytes + 1];
        var bytesRead = await context.Request.InputStream.ReadAtLeastAsync(buffer, buffer.Length, false);
        if (bytesRead <= MaxGameJsonBytes)
        {
            try
            {
                game = JsonSerializer.Deserialize<FavoriteGame>(buffer.AsSpan(0, bytesRead), JsonSerializerOptions.Web);
            }
            catch (JsonException)
            {
                // Неподходящее тело запроса обрабатывается как 400 ниже.
            }
        }

        if (game is null || game.Id <= 0 || string.IsNullOrWhiteSpace(game.Name))
        {
            await ResponseWriter.SendAsync(
                context.Response, "Укажите игру из каталога", HttpStatusCode.BadRequest);
            return;
        }

        // На карточках используются только жанры с названием и RAWG slug.
        game = game with
        {
            Genres = game.Genres?.OfType<FavoriteGenre>()
                .Where(genre => !string.IsNullOrWhiteSpace(genre.Slug) && !string.IsNullOrWhiteSpace(genre.Name))
                .ToArray() ?? []
        };

        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO public.user_favorites (user_id, rawg_game_id, game)
            VALUES (@userId, @gameId, @game)
            ON CONFLICT (user_id, rawg_game_id) DO UPDATE SET game = excluded.game;
            """, connection);
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("gameId", game.Id);
        command.Parameters.AddWithValue("game", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(game, JsonSerializerOptions.Web));
        await command.ExecuteNonQueryAsync();
        SendNoContent(context.Response);
    }

    public async Task DeleteAsync(HttpListenerContext context, Guid userId, ClaimsIdentity identity)
    {
        if (!int.TryParse(context.Request.QueryString["id"], out var gameId) || gameId <= 0)
        {
            await ResponseWriter.SendAsync(
                context.Response, "Укажите ID игры", HttpStatusCode.BadRequest);
            return;
        }

        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            DELETE FROM public.user_favorites WHERE user_id = @userId AND rawg_game_id = @gameId;
            """, connection);
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("gameId", gameId);
        await command.ExecuteNonQueryAsync();
        SendNoContent(context.Response);
    }

    public async Task ClearAsync(HttpListenerContext context, Guid userId, ClaimsIdentity identity)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            DELETE FROM public.user_favorites WHERE user_id = @userId;
            """, connection);
        command.Parameters.AddWithValue("userId", userId);
        await command.ExecuteNonQueryAsync();
        SendNoContent(context.Response);       
    }

    private static void SendNoContent(HttpListenerResponse response)
    {
        response.StatusCode = (int)HttpStatusCode.NoContent;
        response.Close();
    }

    private sealed record FavoriteGame(
        int Id,
        string? Name,
        string? Slug,
        [property: JsonPropertyName("background_image")] string? BackgroundImage,
        int? Metacritic,
        string? Released,
        int? Added,
        FavoriteGenre?[]? Genres);

    private sealed record FavoriteGenre(string? Slug, string? Name);
}
