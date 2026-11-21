using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GameHub.Server.Infrastructure;

internal static class GameRankingText
{
    private static readonly Regex Html = new(@"<[^>]+>", RegexOptions.CultureInvariant);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant);
    private static readonly Lazy<Dictionary<string, string>> LocalizedTags = new(LoadTagNames);
    private static readonly HashSet<string> ModeTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "singleplayer", "multiplayer", "multi-player", "co-op", "online-co-op", "local-co-op",
        "co-op-campaign", "local-multiplayer", "split-screen", "pvp", "online-pvp", "local-pvp", "pve",
        "sharedsplit-screen-co-op", "sharedsplit-screen-pvp", "single-player", "two-players"
    };

    public static string ReadDescription(JsonElement game)
    {
        var text = ReadString(game, "description_raw") ?? ReadString(game, "description") ?? "";
        return Whitespace.Replace(WebUtility.HtmlDecode(Html.Replace(text, " ")), " ").Trim();
    }

    public static string Create(JsonElement game, string name, string description)
    {
        var tags = ReadNames(game, "tags");
        var modes = tags.Where(tag => ModeTags.Contains(tag.Slug)).Select(NameWithTranslation);
        var features = tags.Where(tag => !ModeTags.Contains(tag.Slug) && !tag.Slug.StartsWith("steam-", StringComparison.Ordinal))
            .OrderByDescending(tag => LocalizedTags.Value.ContainsKey(tag.Slug))
            .Select(NameWithTranslation).Distinct(StringComparer.OrdinalIgnoreCase).Take(16);
        var genres = ReadNames(game, "genres").Select(NameWithTranslation);
        var platforms = game.TryGetProperty("platforms", out var supported) && supported.ValueKind == JsonValueKind.Array
            ? supported.EnumerateArray().Where(entry => entry.TryGetProperty("platform", out var platform) && platform.ValueKind == JsonValueKind.Object)
                .Select(entry => ReadString(entry.GetProperty("platform"), "name")).OfType<string>()
            : [];

        var sections = new List<string> { $"Title: {name}" };
        Add("Game modes", modes);
        Add("Genres", genres);
        Add("Features", features);
        Add("Platforms", platforms);
        if (description.Length > 0)
            sections.Add($"Description: {description[..Math.Min(1500, description.Length)]}");
        return string.Join('\n', sections);

        void Add(string label, IEnumerable<string> entries)
        {
            var values = string.Join(", ", entries.Distinct(StringComparer.OrdinalIgnoreCase));
            if (values.Length > 0)
                sections.Add($"{label}: {values}");
        }
    }

    private static string NameWithTranslation((string Slug, string Name) tag) =>
        LocalizedTags.Value.TryGetValue(tag.Slug, out var translated) && !string.Equals(tag.Name, translated, StringComparison.OrdinalIgnoreCase)
            ? $"{tag.Name} ({translated})" : tag.Name;

    private static Dictionary<string, string> LoadTagNames()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Infrastructure", "RecommendationTags.json");
        if (!File.Exists(path))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var tags = JsonSerializer.Deserialize<LocalizedTag[]>(File.ReadAllBytes(path), JsonSerializerOptions.Web) ?? [];
        return tags.DistinctBy(tag => tag.Slug, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(tag => tag.Slug, tag => tag.Name, StringComparer.OrdinalIgnoreCase);
    }

    private static (string Slug, string Name)[] ReadNames(JsonElement game, string property) =>
        game.TryGetProperty(property, out var entries) && entries.ValueKind == JsonValueKind.Array
            ? entries.EnumerateArray().Where(entry => entry.ValueKind == JsonValueKind.Object)
                .Select(entry => (Slug: ReadString(entry, "slug") ?? "", Name: ReadString(entry, "name") ?? ""))
                .Where(entry => entry.Name.Length > 0).ToArray()
            : [];

    private static string? ReadString(JsonElement value, string property) =>
        value.TryGetProperty(property, out var field) && field.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(field.GetString())
            ? field.GetString() : null;

    private sealed record LocalizedTag(string Slug, string Name);
}
