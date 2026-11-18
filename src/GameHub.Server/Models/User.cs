using System.Text.Json.Serialization;

namespace GameHub.Server.Models;

public class User
{
    public Guid Id { get; set; }

    public string Nickname { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    [JsonIgnore]
    public string PasswordHash { get; set; } = string.Empty;

    public string Role { get; set; } = "player";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
