namespace GameHub.Server.Auth;

public class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "GameHub";

    public string Audience { get; set; } = "GameHub.Api";

    // Секрет для подписи HMAC-SHA256, минимум 32 байта
    public string Key { get; set; } = "";

    public int AccessTokenMinutes { get; set; } = 5;

    public int RefreshTokenDays { get; set; } = 7;
}
