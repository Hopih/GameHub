using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GameHub.Server.Infrastructure;

internal sealed class GameIndexImporter(RawgClient rawg, EmbeddingClient embeddings, GameSearchIndex index)
{
    public const int GameCount = 10_000;
    private const int PageSize = 40;
    private const int PointBatchSize = 128;
    private const int RawgConcurrency = 12;
    private static readonly string[] Genres =
    [
        "action", "adventure", "role-playing-games-rpg", "strategy", "shooter", "simulation", "puzzle",
        "platformer", "racing", "sports", "fighting", "indie", "casual", "arcade", "family", "board-games", "card"
    ];
    private static readonly HashSet<string> ExcludedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "dlc", "expansion", "expansion-pack", "add-on", "demo", "playtest", "soundtrack", "mod", "total-conversion"
    };
    private static readonly Regex NonGameTitle = new(@"\b(?:demo|prologue|playtest|trial|soundtrack|artbook|benchmark|season pass|upgrade pack|dlc)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex EditionSuffix = new(@"\b(?:game of the year(?: edition)?|goty(?: edition)?|(?:complete|definitive|deluxe|ultimate|legendary|premium|gold|collector['’]?s|enhanced|special|anniversary|standard) edition)[)\]]?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AddOnDescription = new(
        @"\b(?:(?:is|as) (?:the|an?) [^.!?\n]{0,65}\b(?:expansion(?: pack)?|add[- ]on|dlc|downloadable content|demo|playtest) (?:for|of|to)\b|requires (?:the )?base game\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public async Task RunAsync(int target, CancellationToken token)
    {
        var version = await embeddings.GetModelVersionAsync(token);
        var selectionName = $"{GameSearchIndex.Alias(version)}_{target}";
        var collection = $"{selectionName}_standalone";
        var stateDirectory = Path.Combine(".local", "game-index");
        Directory.CreateDirectory(stateDirectory);
        var manifestPath = Path.Combine(stateDirectory, $"{index.Scope}_{selectionName}.json");
        await using var importLock = File.Open(manifestPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        await index.EnsureCollectionAsync(collection, token);
        var completed = await index.CountAsync(collection, token);
        if (completed < target)
        {
            var candidates = await LoadCandidatesAsync(manifestPath, target, token);
            Console.WriteLine($"Кандидатов для проверки: {candidates.Length}. В индексе: {completed}/{target}.");
            foreach (var pool in candidates.Chunk(PointBatchSize))
            {
                var existing = await index.FindExistingAsync(collection, pool.Select(candidate => candidate.Card.Id).ToArray(), token);
                var pending = pool.Where(candidate => !existing.Contains(candidate.Card.Id)).ToArray();
                for (var offset = 0; offset < pending.Length && completed < target;)
                {
                    var length = Math.Min(target - completed, pending.Length - offset);
                    var games = await ReadDescriptionsAsync(pending[offset..(offset + length)], token);
                    await WriteGamesAsync(collection, games, token);
                    completed += games.Length;
                    offset += length;
                }

                if (pending.Length > 0)
                    Console.WriteLine($"Индексирование игр: {completed}/{target}.");
                if (completed == target)
                    break;
            }
        }

        if (await index.CountAsync(collection, token) != target)
            throw new InvalidDataException($"В подборке получено {completed} из {target} игр с описаниями.");

        if (target == GameCount)
            await index.PublishAsync(collection, version, token);
        Console.WriteLine($"Индекс готов. Коллекция {collection}, игр: {target}.");
    }

    private async Task<ImportCandidate[]> LoadCandidatesAsync(string path, int target, CancellationToken token)
    {
        if (File.Exists(path))
        {
            await using var saved = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<ImportCandidate[]>(saved, JsonSerializerOptions.Web, token)
                ?? throw new InvalidDataException("Не удалось прочитать список кандидатов.");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var endDate = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var released = $"1950-01-01,{endDate}";
        var recent = $"{today.AddYears(-4).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)},{endDate}";
        var sources = new List<CatalogSource> { new(Math.Clamp(target / PageSize, 8, 250), null, released) };
        var byPeriod = Math.Max(3, target / 53);
        for (var firstYear = 2000; firstYear <= today.Year; firstYear += 5)
        {
            var start = firstYear == 2000 ? 1950 : firstYear;
            var end = Math.Min(firstYear + 4, today.Year);
            sources.Add(new CatalogSource(byPeriod, null, $"{start:D4}-01-01,{end:D4}-12-31"));
        }
        sources.AddRange(Genres.Select(genre => new CatalogSource(Math.Max(2, target / 800), genre, released)));
        var requests = sources.SelectMany(source => Enumerable.Range(1, source.Pages).Select(page => (source, page)))
            .OrderBy(request => request.page).ToArray();
        var selectionLimit = target + Math.Max(100, target / 4);
        var all = new ConcurrentDictionary<int, ImportCandidate>();
        var requested = 0;
        await Parallel.ForEachAsync(requests, new ParallelOptions { MaxDegreeOfParallelism = RawgConcurrency, CancellationToken = token },
            async (request, cancel) =>
            {
                if (request.page > 15 && all.Count >= selectionLimit + Math.Max(10, target / 30))
                    return;
                try
                {
                    var json = await RetryRawgAsync(() => rawg.GetGamesAsync(page: request.page, pageSize: PageSize,
                        genre: request.source.Genre, dates: request.source.Dates, cancellationToken: cancel), cancel);
                    using var page = JsonDocument.Parse(json);
                    foreach (var item in page.RootElement.GetProperty("results").EnumerateArray())
                    {
                        if (CreateCandidate(item, today) is { } candidate)
                            all.TryAdd(candidate.Card.Id, candidate);
                    }
                }
                catch (HttpRequestException error) when (request.page > 1 && error.StatusCode == HttpStatusCode.NotFound)
                {
                    // A smaller RAWG category can run out of pages before the configured limit.
                }

                var pages = Interlocked.Increment(ref requested);
                if (pages % 50 == 0)
                    Console.WriteLine($"Страниц RAWG проверено: {pages}, кандидатов: {all.Count}.");
            });
        Console.WriteLine($"Страниц RAWG проверено: {requested}, кандидатов: {all.Count}.");

        var ranked = all.Values.OrderByDescending(game => game.Score).ThenBy(game => game.Card.Id)
            .DistinctBy(game => EditionKey(game.Card.Name), StringComparer.OrdinalIgnoreCase).ToArray();
        var initial = target * 7 / 10;
        var variety = Math.Max(1, (target - initial) / (Genres.Length + 2));
        var selectedIds = new HashSet<int>();
        var selected = ranked.Where(game => selectedIds.Add(game.Card.Id)).Take(initial)
            .Concat(Genres.SelectMany(genre => Sample(game => game.Card.Genres.Any(item => item.Slug == genre))))
            .Concat(Sample(game => string.CompareOrdinal(game.Card.Released, "2010-01-01") < 0))
            .Concat(Sample(game => string.CompareOrdinal(game.Card.Released, recent[..10]) >= 0))
            .Concat(ranked.Where(game => selectedIds.Add(game.Card.Id)))
            .Take(selectionLimit).ToArray();
        if (selected.Length < target)
            throw new InvalidDataException($"Из RAWG отобрано {selected.Length} кандидатов при цели {target}.");

        await using (var output = File.Create(path + ".tmp"))
            await JsonSerializer.SerializeAsync(output, selected, JsonSerializerOptions.Web, token);
        File.Move(path + ".tmp", path, overwrite: true);
        return selected;

        IEnumerable<ImportCandidate> Sample(Func<ImportCandidate, bool> match) =>
            ranked.Where(game => match(game) && selectedIds.Add(game.Card.Id)).Take(variety);
    }

    private static ImportCandidate? CreateCandidate(JsonElement game, DateOnly today)
    {
        var name = ReadString(game, "name");
        var released = ReadString(game, "released");
        if (name is null || NonGameTitle.IsMatch(name) || !DateOnly.TryParseExact(released, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || date > today)
            return null;

        var tags = ReadTaxonomy(game, "tags");
        if (tags.Any(tag => ExcludedTags.Contains(tag.Slug)))
            return null;

        var votes = ReadInt(game, "ratings_count") ?? 0;
        var rating = game.TryGetProperty("rating", out var score) && score.ValueKind == JsonValueKind.Number ? score.GetDouble() : 0;
        var metacritic = ReadInt(game, "metacritic");
        var added = ReadInt(game, "added") ?? 0;
        var rated = votes >= 3 && rating >= 2.8;
        var withAudience = votes > 0 && rating >= 3.5 && added >= 50;
        if (!rated && !withAudience && metacritic is not >= 60)
            return null;

        const double priorVotes = 25;
        var byRatings = (3.3 * priorVotes + rating * votes) / (priorVotes + votes);
        var quality = byRatings + 0.19 * Math.Log10(1 + Math.Max(0, added)) + 0.12 * Math.Log10(1 + votes);
        if (metacritic is >= 1 and <= 100)
            quality += (metacritic.Value - 70) * 0.012;

        var id = game.GetProperty("id").GetInt32();
        var card = new GameSearchCard(id, ReadString(game, "slug") ?? id.ToString(CultureInfo.InvariantCulture),
            name, ReadString(game, "background_image"), metacritic, added, released!, ReadTaxonomy(game, "genres"));
        var features = tags.Where(tag => !tag.Slug.StartsWith("steam-", StringComparison.Ordinal))
            .Select(tag => tag.Name).Distinct(StringComparer.OrdinalIgnoreCase).Take(18).ToArray();
        return new ImportCandidate(card, features, quality);
    }

    private async Task<ImportGame[]> ReadDescriptionsAsync(ImportCandidate[] batch, CancellationToken token)
    {
        var docs = new ImportGame?[batch.Length];
        var unavailable = 0;
        await Parallel.ForEachAsync(Enumerable.Range(0, batch.Length),
            new ParallelOptions { MaxDegreeOfParallelism = RawgConcurrency, CancellationToken = token }, async (i, cancel) =>
            {
                var candidate = batch[i];
                if (NonGameTitle.IsMatch(candidate.Card.Name))
                    return;
                try
                {
                    var id = candidate.Card.Id.ToString(CultureInfo.InvariantCulture);
                    var json = await RetryRawgAsync(() => rawg.GetGameAsync(id, cancel), cancel);
                    using var detail = JsonDocument.Parse(json);
                    var root = detail.RootElement;
                    if (ReadInt(root, "parents_count") is > 0)
                        return;

                    var text = GameRankingText.ReadDescription(root);
                    if (text.Length < 40 || AddOnDescription.IsMatch(text))
                        return;

                    var genres = string.Join(", ", candidate.Card.Genres.Select(genre => genre.Name));
                    var tags = string.Join(", ", candidate.Features);
                    var description = text.Length > 2200 ? text[..2200] : text;
                    docs[i] = new ImportGame(candidate.Card,
                        $"passage: {candidate.Card.Name}. Genres: {genres}. Features: {tags}. {description}",
                        GameRankingText.Create(root, candidate.Card.Name, text));
                }
                catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.NotFound)
                {
                    // The game was removed between listing and fetching its description.
                }
                catch (HttpRequestException error) when (IsTransient(error))
                {
                    Console.Error.WriteLine($"RAWG не вернул карточку {candidate.Card.Id} ({error.StatusCode?.ToString() ?? "ошибка соединения"}).");
                    if (Interlocked.Increment(ref unavailable) >= 8)
                        throw new IOException("RAWG не отвечает на запросы карточек. Повтор команды продолжит импорт.", error);
                }
            });
        return docs.OfType<ImportGame>().ToArray();
    }

    private async Task WriteGamesAsync(string collection, ImportGame[] games, CancellationToken token)
    {
        if (games.Length == 0)
            return;
        var vectors = new List<float[]>(games.Length);
        try
        {
            foreach (var batch in games.Chunk(EmbeddingClient.BatchSize))
                vectors.AddRange(await embeddings.EmbedAsync(batch.Select(game => game.Passage).ToArray(), token));
        }
        catch (HttpRequestException error)
        {
            throw new IOException($"Сбой при векторизации описаний. HTTP: {error.StatusCode}.", error);
        }

        try
        {
            await index.AddAsync(collection, games.Select(game => game.Card).ToArray(), vectors.ToArray(),
                games.Select(game => game.RankingText).ToArray(), token);
        }
        catch (HttpRequestException error)
        {
            throw new IOException($"Сбой при сохранении игр в Qdrant. HTTP: {error.StatusCode}.", error);
        }
    }

    private static async Task<string> RetryRawgAsync(Func<Task<string>> request, CancellationToken token)
    {
        for (var retry = 0; ; retry++)
        {
            try { return await request(); }
            catch (HttpRequestException error) when (retry < 2 && IsTransient(error))
            {
                await Task.Delay(TimeSpan.FromSeconds(retry + 1), token);
            }
        }
    }

    private static bool IsTransient(HttpRequestException error) => error.StatusCode is null || (int)error.StatusCode >= 500;

    private static GameSearchGenre[] ReadTaxonomy(JsonElement item, string property) =>
        item.TryGetProperty(property, out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().Where(value => ReadString(value, "slug") is not null)
                .Select(value => new GameSearchGenre(ReadString(value, "slug")!, ReadString(value, "name") ?? "")).ToArray()
            : [];

    private static string? ReadString(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString() : null;

    private static int? ReadInt(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;

    private static string EditionKey(string title) => EditionSuffix.Replace(title, "").TrimEnd(' ', ':', '-', '–', '—', '(', '[');

    private sealed record CatalogSource(int Pages, string? Genre, string Dates);
    private sealed record ImportCandidate(GameSearchCard Card, string[] Features, double Score);
    private sealed record ImportGame(GameSearchCard Card, string Passage, string RankingText);
}
