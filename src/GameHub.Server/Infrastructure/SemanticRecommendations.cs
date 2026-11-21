using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GameHub.Server.Infrastructure;

public sealed class SemanticRecommendations
{
    private const int PointBatchSize = 128;
    private const int TagLimit = 3;
    private const int TagSearchLimit = 8;
    private const int GameLimit = 12;
    private const double MinBestTagScore = 0.82;
    private const double MinTagScore = 0.80;
    private const double MaxTagScoreGap = 0.027;
    private const double MaxRelatedTagScoreGap = 0.065;
    private const double MinRawgTagScore = 0.85;
    private const double MinNamedRawgTagScore = 0.78;
    private static readonly Regex WordSeparator = new(@"[^\p{L}\p{N}]+", RegexOptions.CultureInvariant);

    private readonly HttpClient _httpClient;
    private readonly RawgClient _rawgClient;
    private readonly EmbeddingClient _embeddings;
    private readonly GameSearchIndex _games;
    private readonly GameReranker? _reranker;
    private readonly Uri _qdrantUrl;
    private readonly RecommendationTag[] _tags;
    private readonly RecommendationTag[] _rawgTags;
    private readonly RawgTag[] _rawgSource;
    private readonly string _tagVersion;
    private readonly string _rawgTagVersion;
    private readonly SemaphoreSlim _indexLock = new(1);
    private readonly SemaphoreSlim _rawgIndexLock = new(1);

    public SemanticRecommendations(HttpClient httpClient, RawgClient rawgClient, Uri embeddingsUrl, Uri qdrantUrl, Uri? rerankerUrl = null)
    {
        _httpClient = httpClient;
        _rawgClient = rawgClient;
        _embeddings = new EmbeddingClient(httpClient, embeddingsUrl);
        _games = new GameSearchIndex(httpClient, qdrantUrl);
        _reranker = rerankerUrl is null ? null : new GameReranker(httpClient, rerankerUrl, rawgClient, _games);
        _qdrantUrl = qdrantUrl;
        var directory = Path.Combine(AppContext.BaseDirectory, "Infrastructure");
        var path = Path.Combine(directory, "RecommendationTags.json");
        _tags = JsonSerializer.Deserialize<RecommendationTag[]>(File.ReadAllBytes(path), JsonSerializerOptions.Web)
            ?? throw new InvalidDataException("Каталог тегов рекомендаций пуст.");
        _tagVersion = EmbeddingClient.VersionHash(JsonSerializer.Serialize(_tags));

        var rawgPath = Path.Combine(directory, "RawgRecommendationTags.json");
        var rawgTags = JsonSerializer.Deserialize<RawgTag[]>(File.ReadAllBytes(rawgPath), JsonSerializerOptions.Web)
            ?? throw new InvalidDataException("Каталог тегов RAWG пуст.");
        var curatedSlugs = _tags.Where(tag => !tag.IsGenre).Select(tag => tag.Slug)
            .ToHashSet(StringComparer.Ordinal);
        _rawgSource = rawgTags.Where(tag => !curatedSlugs.Contains(tag.Slug))
            .DistinctBy(tag => tag.Slug)
            .ToArray();
        _rawgTags = _rawgSource
            .Select(tag => new RecommendationTag(
                tag.Slug,
                tag.RussianName ?? tag.Name,
                tag.RussianName is null ? $"Video games featuring {tag.Name}." : $"Игры на тему «{tag.RussianName}». {tag.Name}.",
                false))
            .ToArray();
        _rawgTagVersion = EmbeddingClient.VersionHash(JsonSerializer.Serialize(_rawgTags));
    }

    public async Task<RecommendationResult> FindAsync(string description, CancellationToken cancellationToken)
    {
        var modelVersion = await _embeddings.GetModelVersionAsync(cancellationToken);
        var queryVector = (await _embeddings.EmbedAsync([$"query: {description}"], cancellationToken))[0];
        var candidateLimit = _reranker is null ? GameLimit : GameReranker.CandidateLimit;
        var indexed = await _games.SearchAsync(modelVersion, queryVector, candidateLimit, _reranker is not null, cancellationToken);
        if (indexed.Length > 0)
        {
            if (_reranker is not null)
                indexed = await _reranker.RankAsync(description, modelVersion, indexed, cancellationToken);
            return new RecommendationResult([], indexed.Take(GameLimit).Select(candidate => candidate.Game).ToArray());
        }

        var collection = $"gamehub_recommendation_tags_{modelVersion}_{_tagVersion}";
        var rawgCollection = $"gamehub_recommendation_rawg_tags_{modelVersion}_{_rawgTagVersion}";
        await EnsureIndexAsync(collection, queryVector.Length, cancellationToken);

        var hits = await FindTagsAsync(collection, _tags, queryVector, cancellationToken);
        var bestScore = hits.FirstOrDefault()?.Score ?? 0;
        TagMatch? rawgMatch = null;
        if (await GetPointCountAsync(rawgCollection, cancellationToken) == _rawgTags.Length)
        {
            rawgMatch = await FindRawgMatchAsync(rawgCollection, description, queryVector, bestScore, cancellationToken);
        }
        else
        {
            _ = BuildRawgIndexAsync(rawgCollection, queryVector.Length);
        }

        if (bestScore < MinBestTagScore && rawgMatch is null)
            return new RecommendationResult([], []);

        var cutoff = Math.Max(MinTagScore, bestScore - MaxTagScoreGap);
        var matches = bestScore < MinBestTagScore ? [] :
            hits.Where(hit => hit.Score >= cutoff).Take(TagLimit - (rawgMatch is null ? 0 : 1)).ToArray();
        var relatedCutoff = Math.Max(MinTagScore, bestScore - MaxRelatedTagScoreGap);
        var related = bestScore < MinBestTagScore ? [] :
            hits.Skip(matches.Length).Where(hit => hit.Score >= relatedCutoff).ToArray();
        if (rawgMatch is not null)
            matches = [rawgMatch, .. matches];
        var pages = await Task.WhenAll(matches.Select(match => FindGamesAsync(match.Tag, cancellationToken)));
        var ranked = pages.SelectMany(page => page)
            .DistinctBy(game => game.GetProperty("id").GetInt32())
            .Select(game => new
            {
                Game = game,
                Tags = matches.Where(match => MatchesTag(game, match.Tag)).ToArray()
            })
            .Where(candidate => candidate.Tags.Length > 0)
            .OrderByDescending(candidate => rawgMatch is not null && candidate.Tags.Contains(rawgMatch))
            .ThenByDescending(candidate => candidate.Tags.Sum(match => match.Score - MinTagScore))
            .ThenByDescending(candidate => related.Where(match => MatchesTag(candidate.Game, match.Tag))
                .Sum(match => match.Score - relatedCutoff))
            .ThenByDescending(candidate => candidate.Game.GetProperty("added").GetInt32())
            .Take(GameLimit)
            .ToArray();

        return new RecommendationResult(
            matches.Where(match => ranked.Any(candidate => candidate.Tags.Contains(match)))
                .Select(match => match.Tag.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            ranked.Select(candidate => candidate.Game).ToArray());
    }

    private async Task<TagMatch[]> FindTagsAsync(
        string collection, RecommendationTag[] tags, float[] queryVector, CancellationToken cancellationToken,
        int[]? ids = null)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            CollectionUri(collection, "/points/query"),
            new
            {
                query = queryVector,
                limit = TagSearchLimit,
                with_payload = false,
                filter = ids is null ? null : new { must = new[] { new { has_id = ids } } }
            },
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var search = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return search.GetProperty("result").GetProperty("points").EnumerateArray()
            .Select(hit => new TagMatch(tags[hit.GetProperty("id").GetInt32()], hit.GetProperty("score").GetDouble()))
            .ToArray();
    }

    private async Task<TagMatch?> FindRawgMatchAsync(
        string collection, string description, float[] vector, double curatedScore, CancellationToken cancellationToken)
    {
        var query = NormalizeWords(description);
        var namedIds = _rawgSource.Select((tag, id) => new { Tag = tag, Id = id })
            .Where(entry => NamesTag(query, entry.Tag))
            .Select(entry => entry.Id)
            .ToArray();
        if (namedIds.Length > 0)
        {
            var named = await FindTagsAsync(collection, _rawgTags, vector, cancellationToken, namedIds);
            var match = named.FirstOrDefault(hit => hit.Score >= MinNamedRawgTagScore);
            if (match is not null)
                return match;
        }

        if (curatedScore >= MinBestTagScore)
            return null;

        var hits = await FindTagsAsync(collection, _rawgTags, vector, cancellationToken);
        return hits.FirstOrDefault(hit => hit.Score >= MinRawgTagScore);
    }

    private static bool NamesTag(string query, RawgTag tag)
    {
        var name = NormalizeWords(tag.Name);
        var russian = NormalizeWords(tag.RussianName ?? "");
        return query == name || query == russian
            || name.Contains(' ') && ContainsPhrase(query, name)
            || russian.Contains(' ') && ContainsPhrase(query, russian)
            || tag.Aliases?.Any(alias => ContainsPhrase(query, NormalizeWords(alias))) == true;
    }

    private static string NormalizeWords(string text) =>
        WordSeparator.Replace(text.ToLowerInvariant().Replace('ё', 'е'), " ").Trim();

    private static bool ContainsPhrase(string text, string phrase) =>
        phrase.Length > 0 && $" {text} ".Contains($" {phrase} ", StringComparison.Ordinal);

    private async Task EnsureIndexAsync(string collection, int dimensions, CancellationToken cancellationToken)
    {
        if (await GetPointCountAsync(collection, cancellationToken) == _tags.Length)
            return;

        await _indexLock.WaitAsync(cancellationToken);
        try
        {
            await PopulateIndexAsync(collection, _tags, dimensions, cancellationToken);
        }
        finally
        {
            _indexLock.Release();
        }
    }

    private async Task BuildRawgIndexAsync(string collection, int dimensions)
    {
        if (!_rawgIndexLock.Wait(0))
            return;

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await PopulateIndexAsync(collection, _rawgTags, dimensions, timeout.Token);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Не удалось построить расширенный индекс тегов RAWG. {error.Message}");
        }
        finally
        {
            _rawgIndexLock.Release();
        }
    }

    private async Task PopulateIndexAsync(
        string collection, RecommendationTag[] tags, int dimensions, CancellationToken cancellationToken)
    {
        var count = await GetPointCountAsync(collection, cancellationToken);
        if (count == tags.Length)
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

        var firstId = 0;
        foreach (var batch in tags.Chunk(PointBatchSize))
        {
            var vectors = new List<float[]>(batch.Length);
            foreach (var inputs in batch.Chunk(EmbeddingClient.BatchSize))
            {
                vectors.AddRange(await _embeddings.EmbedAsync(
                    inputs.Select(tag => $"passage: {tag.Name}. {tag.Description}").ToArray(), cancellationToken));
            }

            using var upsert = await _httpClient.PutAsJsonAsync(
                CollectionUri(collection, "/points?wait=true"),
                new
                {
                    points = batch.Select((tag, offset) => new
                    {
                        id = firstId + offset,
                        vector = vectors[offset],
                        payload = new { tag.Slug, tag.Name, tag.IsGenre }
                    }).ToArray()
                },
                cancellationToken);
            upsert.EnsureSuccessStatusCode();
            firstId += batch.Length;
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

    private sealed record RecommendationTag(string Slug, string Name, string Description, bool IsGenre);
    private sealed record RawgTag(string Slug, string Name, string? RussianName, string[]? Aliases);
    private sealed record TagMatch(RecommendationTag Tag, double Score);

    public sealed record RecommendationResult(string[] Tags, JsonElement[] Results);
}
