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

    public async Task<string> GetGamesAsync(int page = 1, int pageSize = 40)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        string requestUrl = $"https://api.rawg.io/api/games?key={Uri.EscapeDataString(_apiKey)}&page={page}&page_size={pageSize}";

        using var response = await _httpClient.GetAsync(requestUrl);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }

    public async Task<string> GetGameAsync(string slug)
    {
        string requestUrl = $"https://api.rawg.io/api/games/{Uri.EscapeDataString(slug)}?key={Uri.EscapeDataString(_apiKey)}";

        using var response = await _httpClient.GetAsync(requestUrl);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }
}
