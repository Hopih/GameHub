namespace GameHub.Server.Models;

public class NewsArticle
{
    public Guid Id { get; set; }

    public Guid SourceId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string? Summary { get; set; }

    public string? ImageUrl { get; set; }

    public DateTimeOffset PublishedAt { get; set; } = DateTimeOffset.UtcNow;
}
