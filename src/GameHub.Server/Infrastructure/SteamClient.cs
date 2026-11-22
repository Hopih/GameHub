using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameHub.Server.Infrastructure;

internal sealed class SteamClient(HttpClient httpClient)
{
    public async Task<SteamGame[]> SearchAsync(string title, CancellationToken cancellationToken)
    {
        // The store's typeahead endpoint is separate from the documented Steam Web API.
        var url = $"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(title)}&l=russian&cc=US";
        var search = await httpClient.GetFromJsonAsync<StoreSearch>(url, cancellationToken)
            ?? throw new JsonException("Steam returned an empty game search response.");

        return (search.Items ?? [])
            .Where(item => item.Id > 0 && item.Type == "app" && !string.IsNullOrWhiteSpace(item.Name))
            .DistinctBy(item => item.Id)
            .Select(item => new SteamGame(item.Id, item.Name!))
            .ToArray();
    }

    public async Task<SteamPost[]> GetNewsAsync(uint appId, CancellationToken cancellationToken)
    {
        var url = $"https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?appid={appId}&count=8&maxlength=120";
        var response = await httpClient.GetFromJsonAsync<NewsResponse>(url, cancellationToken);
        var appNews = response?.AppNews;
        if (appNews is null || appNews.AppId != appId)
            throw new JsonException("Steam returned an unexpected game news response.");

        return (appNews.NewsItems ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.Title) && IsArticleUrl(item.Url))
            .OrderByDescending(item => item.Date)
            .Select(item => new SteamPost(item.Title!, item.Url!, item.Date))
            .ToArray();
    }
    
    private static bool IsArticleUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme is "https" or "http" && uri.Host.Length > 0 && uri.UserInfo.Length == 0;

    private sealed record StoreSearch(StoreItem[]? Items);
    private sealed record StoreItem(uint Id, string? Name, string? Type);

    private sealed record NewsResponse([property: JsonPropertyName("appnews")] AppNews? AppNews);
    private sealed record AppNews(
        [property: JsonPropertyName("appid")] uint AppId,
        [property: JsonPropertyName("newsitems")] NewsItem[]? NewsItems);
    private sealed record NewsItem(string? Title, string? Url, long Date);
}

internal sealed record SteamGame(uint AppId, string Name);
internal sealed record SteamPost(string Title, string Url, long PublishedAt);
