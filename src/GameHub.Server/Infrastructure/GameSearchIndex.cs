using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameHub.Server.Infrastructure;

internal sealed class GameSearchIndex(HttpClient http, Uri url)
{
    private const double MinimumScore = 0.78;
    private const string RankingTextField = "rerank_text_v2";

    public string Scope => EmbeddingClient.VersionHash(url.AbsoluteUri);

    public static string Alias(string version) => $"gamehub_recommendation_games_v1_{version}";

    public async Task<IndexedGame[]> SearchAsync(string version, float[] vector, int limit, bool includeRankingText, CancellationToken token)
    {
        string[] fields = includeRankingText ? ["game", RankingTextField] : ["game"];
        using var response = await http.PostAsJsonAsync(CollectionUri(Alias(version), "/points/query"),
            new { query = vector, limit, score_threshold = MinimumScore, with_payload = fields }, token);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return [];
        var result = await ReadResultAsync(response, token);
        return result.GetProperty("points").EnumerateArray().Select(point =>
        {
            var payload = point.GetProperty("payload");
            var text = payload.TryGetProperty(RankingTextField, out var field) && field.ValueKind == JsonValueKind.String
                ? field.GetString() : null;
            return new IndexedGame(point.GetProperty("id").GetInt32(), payload.GetProperty("game"), text);
        }).ToArray();
    }

    public async Task SaveRankingTextsAsync(string version, GameRankingDocument[] documents, CancellationToken token)
    {
        using var response = await http.PostAsJsonAsync(CollectionUri(Alias(version), "/points/batch?wait=true"), new
        {
            operations = documents.Select(doc => new
            {
                set_payload = new { points = new[] { doc.Id }, payload = new Dictionary<string, string> { [RankingTextField] = doc.Text } }
            }).ToArray()
        }, token);
        response.EnsureSuccessStatusCode();
    }

    public async Task EnsureCollectionAsync(string collection, CancellationToken token)
    {
        using var existing = await http.GetAsync(CollectionUri(collection), token);
        if (existing.StatusCode != HttpStatusCode.NotFound)
        {
            existing.EnsureSuccessStatusCode();
            return;
        }

        using var created = await http.PutAsJsonAsync(CollectionUri(collection),
            new { vectors = new { size = EmbeddingClient.Dimensions, distance = "Cosine" } }, token);
        created.EnsureSuccessStatusCode();
    }

    public async Task<int> CountAsync(string collection, CancellationToken token)
    {
        using var response = await http.PostAsJsonAsync(CollectionUri(collection, "/points/count"), new { exact = true }, token);
        return (await ReadResultAsync(response, token)).GetProperty("count").GetInt32();
    }

    public async Task<HashSet<int>> FindExistingAsync(string collection, int[] ids, CancellationToken token)
    {
        using var response = await http.PostAsJsonAsync(CollectionUri(collection, "/points"),
            new { ids, with_payload = false, with_vector = false }, token);
        var result = await ReadResultAsync(response, token);
        return result.EnumerateArray().Select(point => point.GetProperty("id").GetInt32()).ToHashSet();
    }

    public async Task AddAsync(string collection, GameSearchCard[] cards, float[][] vectors, string[] rankingTexts, CancellationToken token)
    {
        using var response = await http.PutAsJsonAsync(CollectionUri(collection, "/points?wait=true"), new
        {
            points = cards.Select((game, i) => new
            {
                id = game.Id, vector = vectors[i],
                payload = new Dictionary<string, object> { ["game"] = game, [RankingTextField] = rankingTexts[i] }
            }).ToArray()
        }, token);
        response.EnsureSuccessStatusCode();
    }

    public async Task PublishAsync(string collection, string version, CancellationToken token)
    {
        var alias = Alias(version);
        using var current = await http.GetAsync(new Uri(url, "/aliases"), token);
        var aliases = (await ReadResultAsync(current, token)).GetProperty("aliases");
        var existing = aliases.EnumerateArray().FirstOrDefault(entry => entry.GetProperty("alias_name").GetString() == alias);
        var actions = new List<object>();
        if (existing.ValueKind != JsonValueKind.Undefined)
        {
            if (existing.GetProperty("collection_name").GetString() == collection)
                return;
            actions.Add(new { delete_alias = new { alias_name = alias } });
        }
        actions.Add(new { create_alias = new { collection_name = collection, alias_name = alias } });
        using var response = await http.PostAsJsonAsync(new Uri(url, "/collections/aliases"), new { actions }, token);
        response.EnsureSuccessStatusCode();
    }

    private Uri CollectionUri(string name, string path = "") => new(url, $"/collections/{name}{path}");

    private static async Task<JsonElement> ReadResultAsync(HttpResponseMessage response, CancellationToken token)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(token)).GetProperty("result");
    }
}

internal sealed record GameSearchCard(int Id, string Slug, string Name,
    [property: JsonPropertyName("background_image")] string? BackgroundImage,
    int? Metacritic, int Added, string Released, GameSearchGenre[] Genres);

internal sealed record GameSearchGenre(string Slug, string Name);

internal sealed record IndexedGame(int Id, JsonElement Game, string? RankingText);

internal sealed record GameRankingDocument(int Id, string Text);
