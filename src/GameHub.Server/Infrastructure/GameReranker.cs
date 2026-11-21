using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GameHub.Server.Infrastructure;

internal sealed class GameReranker(HttpClient http, Uri url, RawgClient rawg, GameSearchIndex index)
{
    public const int CandidateLimit = 30;
    private const int RankOffset = 10;
    private const double RerankerWeight = 0.25;
    private readonly SemaphoreSlim _cacheFill = new(1);

    public async Task<IndexedGame[]> RankAsync(
        string query, string version, IndexedGame[] candidates, CancellationToken cancellationToken)
    {
        if (candidates.Length < 2)
            return candidates;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            using var healthTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            healthTimeout.CancelAfter(TimeSpan.FromMilliseconds(750));
            using var health = await http.GetAsync(new Uri(url, "/health"), healthTimeout.Token);
            if (!health.IsSuccessStatusCode)
                return candidates;

            var texts = await PrepareTextsAsync(version, candidates, timeout.Token);
            if (texts is null)
                return candidates;

            using var response = await http.PostAsJsonAsync(new Uri(url, "/rerank"),
                new { query, texts, truncate = true, raw_scores = true }, timeout.Token);
            response.EnsureSuccessStatusCode();
            var scores = await response.Content.ReadFromJsonAsync<RankedCandidate[]>(timeout.Token);
            if (scores is null || scores.Length != texts.Length || scores.Any(score => score is null
                || score.Index < 0 || score.Index >= candidates.Length || !double.IsFinite(score.Score))
                || scores.Select(score => score.Index).Distinct().Count() != candidates.Length)
                throw new InvalidDataException("Реранкер вернул неверный набор оценок.");

            var reranked = scores.OrderByDescending(score => score.Score).ThenBy(score => score.Index);
            return reranked.Select((score, position) => new
                {
                    Game = candidates[score.Index],
                    Combined = 1d / (RankOffset + score.Index) + RerankerWeight / (RankOffset + position)
                })
                .OrderByDescending(game => game.Combined).Select(game => game.Game).ToArray();
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested &&
            error is HttpRequestException or OperationCanceledException or JsonException or InvalidDataException)
        {
            if (error is not HttpRequestException { StatusCode: null })
                Console.Error.WriteLine($"Реранжирование пропущено ({error.GetType().Name}).");
            return candidates;
        }
    }

    private async Task<string[]?> PrepareTextsAsync(string version, IndexedGame[] candidates, CancellationToken token)
    {
        var texts = candidates.Select(candidate => candidate.RankingText).ToArray();
        var missing = Enumerable.Range(0, texts.Length).Where(i => texts[i] is null).ToArray();
        if (missing.Length == 0)
            return texts.Select(text => text!).ToArray();
        if (!await _cacheFill.WaitAsync(0, token))
            return null;

        try
        {
            using var fetchTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            fetchTimeout.CancelAfter(TimeSpan.FromSeconds(8));
            try
            {
                await Parallel.ForEachAsync(missing, new ParallelOptions
                {
                    MaxDegreeOfParallelism = 8, CancellationToken = fetchTimeout.Token
                }, async (i, cancel) =>
                {
                    var game = candidates[i].Game;
                    var name = game.GetProperty("name").GetString() ?? "";
                    try
                    {
                        var raw = await rawg.GetGameAsync(candidates[i].Id.ToString(CultureInfo.InvariantCulture), cancel);
                        using var details = JsonDocument.Parse(raw);
                        var detail = details.RootElement;
                        texts[i] = GameRankingText.Create(detail, name, GameRankingText.ReadDescription(detail));
                    }
                    catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.NotFound)
                    {
                        texts[i] = GameRankingText.Create(game, name, "");
                    }
                });
            }
            catch (Exception error) when (!token.IsCancellationRequested &&
                error is HttpRequestException or OperationCanceledException or JsonException)
            {
                Console.Error.WriteLine($"Не все описания для реранжирования получены ({error.GetType().Name}).");
            }

            var fetched = missing.Where(i => texts[i] is not null)
                .Select(i => new GameRankingDocument(candidates[i].Id, texts[i]!)).ToArray();
            if (fetched.Length > 0)
            {
                try { await index.SaveRankingTextsAsync(version, fetched, token); }
                catch (HttpRequestException error)
                {
                    Console.Error.WriteLine($"Не удалось сохранить описания для реранжирования ({error.StatusCode}).");
                }
            }

            return texts.All(text => text is not null) ? texts.Select(text => text!).ToArray() : null;
        }
        finally
        {
            _cacheFill.Release();
        }
    }

    private sealed record RankedCandidate(int Index, double Score);
}
