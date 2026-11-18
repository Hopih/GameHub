using System.Net;
using System.Net.Mail;
using System.Security.Claims;
using System.Text.Json;
using GameHub.Server.Auth;
using GameHub.Server.Infrastructure;
using GameHub.Server.Models;
using Npgsql;

namespace GameHub.Server.Controllers;

public class AuthorizeController
{
    private const string RefreshCookie = "gamehub_refresh";
    private readonly Database _database;
    private readonly PasswordHasher _passwordHasher = new();
    private readonly TokenService _tokenService;
    private readonly RefreshSessions _sessions;
    
    public AuthorizeController(Database database, TokenService tokenService)
    {
        _database = database;
        _tokenService = tokenService;
        _sessions = new RefreshSessions(database, tokenService);
    }

    public async Task RegisterAsync(HttpListenerContext context)
    {
        RegisterRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<RegisterRequest>(
                context.Request.InputStream, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            await ResponseWriter.SendAsync(
                context.Response, "Некорректный JSON", HttpStatusCode.BadRequest);
            return;
        }

        var nickname = request?.Nickname?.Trim();
        var email = request?.Email?.Trim();
        var password = request?.Password;
        if (string.IsNullOrWhiteSpace(nickname) || nickname.Length > 120 ||
            string.IsNullOrWhiteSpace(email) || email.Length > 120 ||
            !MailAddress.TryCreate(email, out var address) || address.Address != email ||
            string.IsNullOrWhiteSpace(password))
        {
            await ResponseWriter.SendAsync(
                context.Response, "Укажите корректные ник, email и пароль", HttpStatusCode.BadRequest);
            return;
        }

        var passwordHash = _passwordHasher.Hash(password);
        await using var connection = await _database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO public.users (nickname, email, password_hash)
            VALUES (@nickname, @email, @passwordHash)
            RETURNING id, role, created_at;
            """, connection);
        command.Parameters.AddWithValue("nickname", nickname);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("passwordHash", passwordHash);

        User user;
        try
        {
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                throw new InvalidOperationException("Не удалось получить созданного пользователя.");

            user = new User
            {
                Id = reader.GetGuid(0),
                Nickname = nickname,
                Email = email,
                Role = reader.GetString(1),
                CreatedAt = reader.GetFieldValue<DateTimeOffset>(2)
            };
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await ResponseWriter.SendAsync(
                context.Response, "Ник или email уже занят", HttpStatusCode.Conflict);
            return;
        }

        await ResponseWriter.SendAsync(
            context.Response, JsonSerializer.Serialize(user, JsonSerializerOptions.Web),
            HttpStatusCode.Created, "application/json; charset=utf-8");
    }

    public async Task LoginAsync(HttpListenerContext context)
    {
        if (!await AllowSessionRequestAsync(context))
            return;

        LoginRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<LoginRequest>(
                context.Request.InputStream, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            await ResponseWriter.SendAsync(
                context.Response, "Некорректный JSON", HttpStatusCode.BadRequest);
            return;
        }
        var email = request?.Email?.Trim();
        var password = request?.Password;
        if (string.IsNullOrWhiteSpace(email) || email.Length > 120 || string.IsNullOrWhiteSpace(password))
        {
            await ResponseWriter.SendAsync(
                context.Response, "Укажите email и пароль", HttpStatusCode.BadRequest);
            return;
        }

        var user = await FindUserByEmailAsync(email);
        if (user is null)
            _passwordHasher.VerifyDummy(password);

        if (user is null || !_passwordHasher.Verify(password, user.PasswordHash))
        {
            await ResponseWriter.SendAsync(
                context.Response, "Неверный email или пароль", HttpStatusCode.Unauthorized);
            return;
        }

        var refresh = await _sessions.CreateAsync(user.Id);
        await SendSessionAsync(context, user, refresh.Token, refresh.ExpiresAt);
    }

    public async Task RefreshAsync(HttpListenerContext context)
    {
        if (!await AllowSessionRequestAsync(context))
            return;

        var token = ReadRefreshCookie(context.Request);
        var session = token is null ? null : await _sessions.RotateAsync(token);
        if (session is not { } refreshed)
        {
            await ResponseWriter.SendAsync(
                context.Response, "Сессия закончилась. Войдите снова.", HttpStatusCode.Unauthorized);
            return;
        }

        await SendSessionAsync(context, refreshed.User, refreshed.Token, refreshed.ExpiresAt);
    }

    public async Task LogoutAsync(HttpListenerContext context)
    {
        if (!await AllowSessionRequestAsync(context))
            return;

        var token = ReadRefreshCookie(context.Request);
        if (token is not null)
            await _sessions.RevokeAsync(token);

        SetRefreshCookie(context, "", 0);
        context.Response.StatusCode = (int)HttpStatusCode.NoContent;
        context.Response.Close();
    }

    public async Task AuthAsync(HttpListenerContext context, Guid userId, ClaimsIdentity identity)
    {
        var profile = new
        {
            id = userId,
            nickname = identity.FindFirst(AppClaims.Name)?.Value,
            email = identity.FindFirst(AppClaims.Email)?.Value,
            role = identity.FindFirst(AppClaims.Role)?.Value
        };
        await ResponseWriter.SendAsync(
            context.Response, JsonSerializer.Serialize(profile, JsonSerializerOptions.Web),
            HttpStatusCode.OK, "application/json; charset=utf-8");
    }

    private async Task SendSessionAsync(HttpListenerContext context, User user, string refreshToken, DateTime refreshExpiry)
    {
        var (accessToken, expiresAt) = _tokenService.CreateAccessToken(user);
        var maxAge = Math.Max(0, (long)(refreshExpiry - DateTime.UtcNow).TotalSeconds);
        SetRefreshCookie(context, refreshToken, maxAge);
        await ResponseWriter.SendAsync(
            context.Response, JsonSerializer.Serialize(new { accessToken, expiresAt }, JsonSerializerOptions.Web),
            HttpStatusCode.OK, "application/json; charset=utf-8");
    }

    private static string? ReadRefreshCookie(HttpListenerRequest request)
    {
        var token = request.Cookies[RefreshCookie]?.Value;
        return token?.Length == 86 ? token : null;
    }

    private static void SetRefreshCookie(HttpListenerContext context, string token, long maxAge)
    {
        var secure = context.Request.IsSecureConnection || context.Request.Url?.IsLoopback != true;
        context.Response.Headers.Add("Set-Cookie",
            $"{RefreshCookie}={token}; Path=/auth; Max-Age={maxAge}; HttpOnly; SameSite=Strict{(secure ? "; Secure" : "")}");
    }

    private static async Task<bool> AllowSessionRequestAsync(HttpListenerContext context)
    {
        context.Response.Headers.Set("Cache-Control", "no-store");
        var origin = context.Request.Headers["Origin"];
        var url = context.Request.Url;
        if (origin is null ||
            (Uri.TryCreate(origin, UriKind.Absolute, out var source) && url is not null &&
             source.Authority.Equals(url.Authority, StringComparison.OrdinalIgnoreCase) &&
             (source.Scheme == Uri.UriSchemeHttps || (!context.Request.IsSecureConnection && source.Scheme == Uri.UriSchemeHttp))))
            return true;

        await ResponseWriter.SendAsync(
            context.Response, "Источник запроса не разрешён", HttpStatusCode.Forbidden);
        return false;
    }

    private async Task<User?> FindUserByEmailAsync(string email)
    {
        await using var connection = await _database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            SELECT id, nickname, email, password_hash, role, created_at
            FROM public.users
            WHERE lower(email) = lower(@email);
            """, connection);
        command.Parameters.AddWithValue("email", email);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new User
        {
            Id = reader.GetGuid(0),
            Nickname = reader.GetString(1),
            Email = reader.GetString(2),
            PasswordHash = reader.GetString(3),
            Role = reader.GetString(4),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(5)
        };
    }

    private sealed record RegisterRequest(string? Nickname, string? Email, string? Password);
    private sealed record LoginRequest(string? Email, string? Password);
}
