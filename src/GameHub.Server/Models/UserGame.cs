namespace GameHub.Server.Models;

// Связь пользователя с игрой; пара UserId и GameId должна быть уникальной.
public class UserGame
{
    public Guid UserId { get; set; }

    public Guid GameId { get; set; }

    public string Status { get; set; } = "want_to_play";

    public int? Score { get; set; }

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}
