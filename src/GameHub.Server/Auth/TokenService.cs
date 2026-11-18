using GameHub.Server.Models;

namespace GameHub.Server.Auth;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

public class TokenService(IOptions<JwtOptions> options)
{
    private readonly JwtOptions _options = options.Value;

    public SymmetricSecurityKey SigningKey => CreateKey(_options.Key);

    public static SymmetricSecurityKey CreateKey(string key) => new(Encoding.UTF8.GetBytes(key));

    // Access-токен: подписанный JWT с данными пользователя, живёт несколько минут.
    // Сервер проверяет подпись, время действия, издателя и получателя токена.
    public (string Token, DateTime ExpiresAt) CreateAccessToken(User user)
    {
        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expiresAt,
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
                [AppClaims.Name] = user.Nickname,
                [AppClaims.Email] = user.Email,
                [AppClaims.Role] = user.Role
            }
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expiresAt);
    }

    public async Task<ClaimsIdentity?> ValidateAccessTokenAsync(string token)
    {
        var handler = new JsonWebTokenHandler { MapInboundClaims = false };
        var parameters = new TokenValidationParameters
        {
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = SigningKey,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudience = _options.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.Zero,
            NameClaimType = AppClaims.Name,
            RoleClaimType = AppClaims.Role
        };

        var result = await handler.ValidateTokenAsync(token, parameters);
        return result.IsValid ? result.ClaimsIdentity : null;
    }

    // Refresh-токен: просто 64 случайных байта (не JWT). Клиент получает сам токен,
    // а в БД кладём только его SHA-256 — утечка таблицы не даст войти.
    public (string Token, string Hash, DateTime ExpiresAt) CreateRefreshToken()
    {
        var token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));
        return (token, HashRefreshToken(token), DateTime.UtcNow.AddDays(_options.RefreshTokenDays));
    }

    public static string HashRefreshToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
