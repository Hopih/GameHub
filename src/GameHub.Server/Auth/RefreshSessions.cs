using GameHub.Server.Infrastructure;
using GameHub.Server.Models;
using Npgsql;

namespace GameHub.Server.Auth;

public sealed class RefreshSessions(Database database, TokenService tokens)
{
    public async Task RunCleanupAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        try
        {
            do
            {
                try
                {
                    await using var connection = await database.OpenConnectionAsync(cancellationToken);
                    await using var command = new NpgsqlCommand("""
                        DELETE FROM public.refresh_tokens WHERE expires_at <= statement_timestamp();
                        """, connection);
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }
                catch (Exception exception) when (exception is NpgsqlException or TimeoutException)
                {
                    Console.Error.WriteLine($"Не удалось удалить истёкшие refresh-токены: {exception.Message}");
                }
            } while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    public async Task<(string Token, DateTime ExpiresAt)> CreateAsync(Guid userId)
    {
        var refresh = tokens.CreateRefreshToken();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO public.refresh_tokens (user_id, family_id, token_hash, expires_at)
            VALUES (@userId, @familyId, @hash, @expiresAt);
            """, connection);
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("familyId", Guid.NewGuid());
        command.Parameters.AddWithValue("hash", refresh.Hash);
        command.Parameters.AddWithValue("expiresAt", refresh.ExpiresAt);
        await command.ExecuteNonQueryAsync();
        return (refresh.Token, refresh.ExpiresAt);
    }

    public async Task<(User User, string Token, DateTime ExpiresAt)?> RotateAsync(string token)
    {
        var hash = TokenService.HashRefreshToken(token);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var familyId = await LockFamilyAsync(transaction, hash);
        if (familyId is null)
            return null;

        var next = tokens.CreateRefreshToken();
        await using var command = new NpgsqlCommand("""
            WITH previous AS (
                UPDATE public.refresh_tokens
                SET revoked_at = statement_timestamp(), revoke_reason = 'rotated', replaced_by = @nextId
                WHERE token_hash = @hash AND revoked_at IS NULL AND expires_at > statement_timestamp()
                RETURNING user_id, family_id, expires_at
            ), replacement AS (
                INSERT INTO public.refresh_tokens (id, user_id, family_id, token_hash, expires_at)
                SELECT @nextId, user_id, family_id, @nextHash, expires_at FROM previous
                RETURNING user_id, expires_at
            )
            SELECT u.id, u.nickname, u.email, u.role, r.expires_at
            FROM replacement r JOIN public.users u ON u.id = r.user_id;
            """, connection, transaction);
        command.Parameters.AddWithValue("hash", hash);
        command.Parameters.AddWithValue("nextId", Guid.NewGuid());
        command.Parameters.AddWithValue("nextHash", next.Hash);

        (User User, string Token, DateTime ExpiresAt)? result = null;
        await using (var reader = await command.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
            {
                var user = new User
                {
                    Id = reader.GetGuid(0),
                    Nickname = reader.GetString(1),
                    Email = reader.GetString(2),
                    Role = reader.GetString(3)
                };
                result = (user, next.Token, reader.GetDateTime(4));
            }
        }

        if (result is null)
        {
            // Пересёкшиеся запросы могут принести старую cookie, пока ответ с новой ещё в пути.
            await using var revoke = new NpgsqlCommand("""
                UPDATE public.refresh_tokens
                SET revoked_at = statement_timestamp(), revoke_reason = 'reuse'
                WHERE family_id = @familyId AND revoked_at IS NULL AND EXISTS (
                    SELECT 1 FROM public.refresh_tokens
                    WHERE token_hash = @hash AND revoke_reason = 'rotated'
                      AND expires_at > statement_timestamp()
                      AND revoked_at < statement_timestamp() - interval '5 seconds'
                );
                """, connection, transaction);
            revoke.Parameters.AddWithValue("familyId", familyId.Value);
            revoke.Parameters.AddWithValue("hash", hash);
            await revoke.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        return result;
    }

    public async Task RevokeAsync(string token)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var familyId = await LockFamilyAsync(transaction, TokenService.HashRefreshToken(token));
        if (familyId is null)
            return;

        await using var command = new NpgsqlCommand("""
            UPDATE public.refresh_tokens
            SET revoked_at = statement_timestamp(), revoke_reason = 'logout'
            WHERE family_id = @familyId AND revoked_at IS NULL;
            """, connection, transaction);
        command.Parameters.AddWithValue("familyId", familyId.Value);
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    private static async Task<Guid?> LockFamilyAsync(NpgsqlTransaction transaction, string hash)
    {
        await using var lookup = new NpgsqlCommand("""
            SELECT family_id FROM public.refresh_tokens WHERE token_hash = @hash;
            """, transaction.Connection, transaction);
        lookup.Parameters.AddWithValue("hash", hash);
        if (await lookup.ExecuteScalarAsync() is not Guid familyId)
            return null;
        
        // Один вход обрабатывается последовательно даже при одновременных refresh и logout.
        await using var familyLock = new NpgsqlCommand("""
            SELECT pg_advisory_xact_lock(hashtextextended(CAST(@familyId AS text), 0)); 
            """, transaction.Connection, transaction);
        familyLock.Parameters.AddWithValue("familyId", familyId);
        await familyLock.ExecuteNonQueryAsync();
        return familyId;
    }
}
