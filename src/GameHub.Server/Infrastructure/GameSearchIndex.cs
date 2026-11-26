using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameHub.Server.Infrastructure;

internal sealed class GameSearchIndex(HttpClient http, Uri url)
{
    private const double MinimumScore = 0.78;
    private const string RankingTextField = "rerank_text_v2";
    private const string TagSlugsField = "tag_slugs";
    private const string TagSlugsLoadedField = "tag_slugs_loaded";

    public string Scope => EmbeddingClient.VersionHash(url.AbsoluteUri);

    public static string Alias(string version) => $"gamehub_recommendation_games_v1_{version}";

    public async Task<IndexedGame[]> SearchAsync(string version, float[]? vector, int limit, bool includeRankingText,
        CancellationToken token, GameTagFilter[]? tags = null)
    {
        string[] fields = includeRankingText ? ["game", RankingTextField] : ["game"];
        var filter = tags is { Length: > 0 } ? new
        {
            must = tags.Select(tag => new
            {
                key = tag.IsGenre ? "game.genres[].slug" : TagSlugsField,
                match = new { value = tag.Slug }
            }).ToArray()
        } : null;
        using var response = await http.PostAsJsonAsync(CollectionUri(Alias(version), "/points/query"),
            new { query = vector, limit, score_threshold = vector is null ? (double?)null : MinimumScore, with_payload = fields, filter },
            token);
        if (response.StatusCode == HttpStatusCode.NotFound && tags is not { Length: > 0 })
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

    public async Task AddAsync(string collection, GameSearchCard[] cards, float[][] vectors, string[] rankingTexts,
        string[][] tagSlugs, CancellationToken token)
    {
        using var response = await http.PutAsJsonAsync(CollectionUri(collection, "/points?wait=true"), new
        {
            points = cards.Select((game, i) => new
            {
                id = game.Id, vector = vectors[i],
                payload = new Dictionary<string, object>
                {
                    ["game"] = game, [RankingTextField] = rankingTexts[i],
                    [TagSlugsField] = tagSlugs[i], [TagSlugsLoadedField] = true
                }
            }).ToArray()
        }, token);
        response.EnsureSuccessStatusCode();
    }

    public async Task<int[]?> FindGamesWithoutTagsAsync(string collection, int limit, CancellationToken token)
    {
        using var response = await http.PostAsJsonAsync(CollectionUri(collection, "/points/scroll"), new
        {
            limit,
            with_payload = false,
            with_vector = false,
            filter = new { must_not = new[] { new { key = TagSlugsLoadedField, match = new { value = true } } } }
        }, token);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        var result = await ReadResultAsync(response, token);
        return result.GetProperty("points").EnumerateArray().Select(point => point.GetProperty("id").GetInt32()).ToArray();
    }

    public async Task SaveTagsAsync(string collection, IReadOnlyDictionary<int, string[]> tags, CancellationToken token)
    {
        using var response = await http.PostAsJsonAsync(CollectionUri(collection, "/points/batch?wait=true"), new
        {
            operations = tags.Select(entry => new
            {
                set_payload = new
                {
                    points = new[] { entry.Key },
                    payload = new Dictionary<string, object> { [TagSlugsField] = entry.Value, [TagSlugsLoadedField] = true }
                }
            }).ToArray()
        }, token);
        response.EnsureSuccessStatusCode();
    }

    public async Task<string?> GetPublishedCollectionAsync(string version, CancellationToken token)
    {
        using var response = await http.GetAsync(new Uri(url, "/aliases"), token);
        var aliases = (await ReadResultAsync(response, token)).GetProperty("aliases");
        var alias = Alias(version);
        foreach (var entry in aliases.EnumerateArray())
            if (entry.GetProperty("alias_name").GetString() == alias)
                return entry.GetProperty("collection_name").GetString();
        return null;
    }

    public async Task PublishAsync(string collection, string version, CancellationToken token)
    {
        var alias = Alias(version);
        var existing = await GetPublishedCollectionAsync(version, token);
        if (existing == collection)
            return;
        var actions = new List<object>();
        if (existing is not null)
            actions.Add(new { delete_alias = new { alias_name = alias } });
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

internal sealed record GameTagFilter(string Slug, bool IsGenre);

internal sealed record IndexedGame(int Id, JsonElement Game, string? RankingText);

internal sealed record GameRankingDocument(int Id, string Text);
