namespace GameHub.Server.Models;

public class NewsSource
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Kind { get; set; } = "rss";

    public string Url { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset? LastFetchedAt { get; set; }
}
