namespace GameHub.Server.Auth;
using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

public static class AppClaims
{
    public const string Name = "name";
    public const string Email = "email";
    public const string Role = "role";

    public static Guid GetUserId(this ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                   ?? throw new InvalidOperationException("В токене нет sub"));

    public static bool IsAdmin(this ClaimsPrincipal principal) =>
        principal.HasClaim(Role, Roles.Admin);
}
