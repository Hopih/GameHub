namespace GameHub.Server.Models;

public class Review
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid GameId { get; set; }

    public string Body { get; set; } = string.Empty;

    public bool IsHidden { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
