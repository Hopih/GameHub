namespace GameHub.Server.Models;

// Локальная запись игры: RawgId связывает её с внешним каталогом RAWG.
public class Game
{
    public Guid Id { get; set; }

    public int RawgId { get; set; }

    public int? SteamAppId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? BackgroundImageUrl { get; set; }

    public DateOnly? Released { get; set; }

    public decimal? RawgRating { get; set; }
}
