using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GameHub.Server.Infrastructure;

public sealed class SemanticRecommendations
{
    private const int EmbeddingBatchSize = 16;
    private const int TagLimit = 3;
    private const int TagSearchLimit = 8;
    private const int GameLimit = 12;
    private const double MinBestTagScore = 0.82;
    private const double MinTagScore = 0.80;
    private const double MaxTagScoreGap = 0.027;
    private const double MaxRelatedTagScoreGap = 0.065;

    private readonly HttpClient _httpClient;
    private readonly RawgClient _rawgClient;
    private readonly Uri _embeddingsUrl;
    private readonly Uri _qdrantUrl;
    private readonly RecommendationTag[] _tags;
    private readonly string _tagVersion;
    private readonly SemaphoreSlim _indexLock = new(1);

    public SemanticRecommendations(HttpClient httpClient, RawgClient rawgClient, Uri embeddingsUrl, Uri qdrantUrl)
    {
        _httpClient = httpClient;
        _rawgClient = rawgClient;
        _embeddingsUrl = embeddingsUrl;
        _qdrantUrl = qdrantUrl;
        var path = Path.Combine(AppContext.BaseDirectory, "Infrastructure", "RecommendationTags.json");
        _tags = JsonSerializer.Deserialize<RecommendationTag[]>(File.ReadAllBytes(path), JsonSerializerOptions.Web)
            ?? throw new InvalidDataException("Каталог тегов рекомендаций пуст.");
        _tagVersion = VersionHash(JsonSerializer.Serialize(_tags));
    }

    public async Task<RecommendationResult> FindAsync(string description, CancellationToken cancellationToken)
    {
        var model = await _httpClient.GetFromJsonAsync<JsonElement>(new Uri(_embeddingsUrl, "/info"), cancellationToken);
        var modelVersion = VersionHash(model.GetProperty("model_id").GetString() + "|" + model.GetProperty("model_sha").GetString());
        var collection = $"gamehub_recommendation_tags_{modelVersion}_{_tagVersion}";
        var queryVector = (await EmbedAsync([$"query: {description}"], cancellationToken))[0];
        await EnsureIndexAsync(collection, queryVector.Length, cancellationToken);

        using var response = await _httpClient.PostAsJsonAsync(
            CollectionUri(collection, "/points/query"),
            new { query = queryVector, limit = TagSearchLimit, with_payload = false },
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var search = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var hits = search.GetProperty("result").GetProperty("points").EnumerateArray()
            .Select(hit => new TagMatch(_tags[hit.GetProperty("id").GetInt32()], hit.GetProperty("score").GetDouble()))
            .ToArray();

        if (hits.Length == 0 || hits[0].Score < MinBestTagScore)
            return new RecommendationResult([], []);

        var cutoff = Math.Max(MinTagScore, hits[0].Score - MaxTagScoreGap);
        var matches = hits.Where(hit => hit.Score >= cutoff).Take(TagLimit).ToArray();
        var relatedCutoff = Math.Max(MinTagScore, hits[0].Score - MaxRelatedTagScoreGap);
        var related = hits.Skip(matches.Length).Where(hit => hit.Score >= relatedCutoff).ToArray();
        var pages = await Task.WhenAll(matches.Select(match => FindGamesAsync(match.Tag, cancellationToken)));
        var ranked = pages.SelectMany(page => page)
            .DistinctBy(game => game.GetProperty("id").GetInt32())
            .Select(game => new
            {
                Game = game,
                Tags = matches.Where(match => MatchesTag(game, match.Tag)).ToArray()
            })
            .Where(candidate => candidate.Tags.Length > 0)
            .OrderByDescending(candidate => candidate.Tags.Sum(match => match.Score - MinTagScore))
            .ThenByDescending(candidate => related.Where(match => MatchesTag(candidate.Game, match.Tag))
                .Sum(match => match.Score - relatedCutoff))
            .ThenByDescending(candidate => candidate.Game.GetProperty("added").GetInt32())
            .Take(GameLimit)
            .ToArray();

        return new RecommendationResult(
            matches.Where(match => ranked.Any(candidate => candidate.Tags.Contains(match)))
                .Select(match => match.Tag.Name).ToArray(),
            ranked.Select(candidate => candidate.Game).ToArray());
    }

    private async Task EnsureIndexAsync(string collection, int dimensions, CancellationToken cancellationToken)
    {
        if (await GetPointCountAsync(collection, cancellationToken) == _tags.Length)
            return;

        await _indexLock.WaitAsync(cancellationToken);
        try
        {
            var count = await GetPointCountAsync(collection, cancellationToken);
            if (count == _tags.Length)
                return;

            if (count is null)
            {
                using var create = await _httpClient.PutAsJsonAsync(
                    CollectionUri(collection),
                    new { vectors = new { size = dimensions, distance = "Cosine" } },
                    cancellationToken);
                if (!create.IsSuccessStatusCode && await GetPointCountAsync(collection, cancellationToken) is null)
                    create.EnsureSuccessStatusCode();
            }

            var vectors = new List<float[]>(_tags.Length);
            foreach (var batch in _tags.Chunk(EmbeddingBatchSize))
            {
                vectors.AddRange(await EmbedAsync(
                    batch.Select(tag => $"passage: {tag.Name}. {tag.Description}").ToArray(), cancellationToken));
            }

            using var upsert = await _httpClient.PutAsJsonAsync(
                CollectionUri(collection, "/points?wait=true"),
                new
                {
                    points = _tags.Select((tag, id) => new
                    {
                        id,
                        vector = vectors[id],
                        payload = new { tag.Slug, tag.Name, tag.IsGenre }
                    }).ToArray()
                },
                cancellationToken);
            upsert.EnsureSuccessStatusCode();
        }
        finally
        {
            _indexLock.Release();
        }
    }

    private async Task<int?> GetPointCountAsync(string collection, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(CollectionUri(collection), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return data.GetProperty("result").GetProperty("points_count").GetInt32();
    }

    private async Task<float[][]> EmbedAsync(string[] inputs, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            new Uri(_embeddingsUrl, "/embed"),
            new { inputs },
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var vectors = await response.Content.ReadFromJsonAsync<float[][]>(cancellationToken);
        if (vectors is null || vectors.Length != inputs.Length || vectors.Any(vector => vector.Length != 384))
            throw new InvalidDataException("Модель должна вернуть один вектор из 384 чисел на каждый текст.");
        return vectors;
    }

    private async Task<JsonElement[]> FindGamesAsync(RecommendationTag tag, CancellationToken cancellationToken)
    {
        var json = await _rawgClient.GetGamesAsync(
            pageSize: GameLimit * 2,
            tag: tag.IsGenre ? null : tag.Slug,
            genre: tag.IsGenre ? tag.Slug : null,
            cancellationToken: cancellationToken);
        return JsonSerializer.Deserialize<JsonElement>(json).GetProperty("results").EnumerateArray().ToArray();
    }

    private static bool MatchesTag(JsonElement game, RecommendationTag tag) =>
        game.TryGetProperty(tag.IsGenre ? "genres" : "tags", out var taxonomy)
        && taxonomy.ValueKind == JsonValueKind.Array
        && taxonomy.EnumerateArray().Any(item => item.GetProperty("slug").GetString() == tag.Slug);

    private Uri CollectionUri(string collection, string path = "") =>
        new(_qdrantUrl, $"/collections/{collection}{path}");

    private static string VersionHash(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)).AsSpan(0, 8));

    private sealed record RecommendationTag(string Slug, string Name, string Description, bool IsGenre);
    private sealed record TagMatch(RecommendationTag Tag, double Score);

    public sealed record RecommendationResult(string[] Tags, JsonElement[] Results);
}
