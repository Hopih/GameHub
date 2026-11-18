using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text.Json;
using GameHub.Server.Auth;
using GameHub.Server.Infrastructure;
using GameHub.Server.Models;
using Microsoft.IdentityModel.JsonWebTokens;
using Npgsql;

namespace GameHub.Server.Controllers;

public class AuthorizeController
{
    private readonly Database _database;
    private readonly PasswordHasher _passwordHasher = new();
    private readonly TokenService _tokenService;
    
    public AuthorizeController(Database database, TokenService tokenService)
    {
        _database = database;
        _tokenService = tokenService;
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

        var (accessToken, expiresAt) = _tokenService.CreateAccessToken(user);
        context.Response.Headers.Set("Cache-Control", "no-store");
        await ResponseWriter.SendAsync(
            context.Response, JsonSerializer.Serialize(new { accessToken, expiresAt }, JsonSerializerOptions.Web),
            HttpStatusCode.OK, "application/json; charset=utf-8");
    }

    public async Task AuthAsync(HttpListenerContext context)
    {
        var authorization = context.Request.Headers["Authorization"];
        var identity = AuthenticationHeaderValue.TryParse(authorization, out var header) &&
                       header.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) &&
                       !string.IsNullOrWhiteSpace(header.Parameter)
            ? await _tokenService.ValidateAccessTokenAsync(header.Parameter)
            : null;

        if (identity is null ||
            !Guid.TryParse(identity.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId))
        {
            context.Response.Headers.Set("WWW-Authenticate", "Bearer");
            await ResponseWriter.SendAsync(
                context.Response, "Требуется действительный токен", HttpStatusCode.Unauthorized);
            return;
        }

        var profile = new
        {
            id = userId,
            nickname = identity.FindFirst(AppClaims.Name)?.Value,
            email = identity.FindFirst(AppClaims.Email)?.Value,
            role = identity.FindFirst(AppClaims.Role)?.Value
        };
        context.Response.Headers.Set("Cache-Control", "no-store");
        await ResponseWriter.SendAsync(
            context.Response, JsonSerializer.Serialize(profile, JsonSerializerOptions.Web),
            HttpStatusCode.OK, "application/json; charset=utf-8");
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
