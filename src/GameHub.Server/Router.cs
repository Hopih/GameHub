using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using GameHub.Server.Auth;
using Microsoft.IdentityModel.JsonWebTokens;

namespace GameHub.Server;

public class Router
{
    private Dictionary<(string Method, string Path), Func<HttpListenerContext, Task>> _routes = new();
    
    public async Task HandleAsync(HttpListenerContext context)
    {
        var path = context.Request.Url?.AbsolutePath ?? "";
        var method = context.Request.HttpMethod;

        if (_routes.TryGetValue((method, path), out var handler))
        {
            await handler(context);
            return;
        }

        await ResponseWriter.SendAsync(context.Response, "Not Found", HttpStatusCode.NotFound);
    }

    public void Register(string method, string path, Func<HttpListenerContext, Task> handler)
    {
        _routes.Add((method, path), handler);
    }

    public void RegisterProtected(
        string method, string path, TokenService tokens,
        Func<HttpListenerContext, Guid, ClaimsIdentity, Task> handler)
    {
        Register(method, path, async context =>
        {
            var authorization = context.Request.Headers["Authorization"];
            var identity = AuthenticationHeaderValue.TryParse(authorization, out var header) &&
                           header.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) &&
                           !string.IsNullOrWhiteSpace(header.Parameter)
                ? await tokens.ValidateAccessTokenAsync(header.Parameter)
                : null;

            context.Response.Headers.Set("Cache-Control", "no-store");
            if (identity is null ||
                !Guid.TryParse(identity.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId))
            {
                context.Response.Headers.Set("WWW-Authenticate", "Bearer");
                await ResponseWriter.SendAsync(
                    context.Response, "Требуется действительный токен", HttpStatusCode.Unauthorized);
                return;
            }

            await handler(context, userId, identity);
        });
    }
}
