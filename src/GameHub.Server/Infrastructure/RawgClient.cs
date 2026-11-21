namespace GameHub.Server.Infrastructure;

public class RawgClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public RawgClient(HttpClient httpClient, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("RAWG API key is required.", nameof(apiKey));
        }

        _httpClient = httpClient;
        _apiKey = apiKey;
    }

    public async Task<string> GetGamesAsync(
        int page = 1,
        int pageSize = 40,
        string ordering = "-added",
        string? search = null,
        string? tag = null,
        string? genre = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        string requestUrl = $"https://api.rawg.io/api/games?key={Uri.EscapeDataString(_apiKey)}&page={page}&page_size={pageSize}&ordering={Uri.EscapeDataString(ordering)}";
        if (!string.IsNullOrWhiteSpace(search))
        {
            requestUrl += $"&search={Uri.EscapeDataString(search)}";
        }

        if (tag is not null)
            requestUrl += $"&tags={Uri.EscapeDataString(tag)}";

        if (genre is not null)
            requestUrl += $"&genres={Uri.EscapeDataString(genre)}";

        if (ordering == "-released")
        {
            string today = DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            requestUrl += $"&dates=1900-01-01,{today}";
        }

        using var response = await _httpClient.GetAsync(requestUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<string> GetGameAsync(string slug)
    {
        string requestUrl = $"https://api.rawg.io/api/games/{Uri.EscapeDataString(slug)}?key={Uri.EscapeDataString(_apiKey)}";

        using var response = await _httpClient.GetAsync(requestUrl);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }
}
