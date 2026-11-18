using System.Net;

using GameHub.Server.Infrastructure;
using Npgsql;

namespace GameHub.Server.Controllers;

public class HealthController
{
    private readonly Database _database;

    public HealthController(Database database)
    {
        _database = database;
    }

    public async Task CheckHealthAsync(HttpListenerContext context)
    {
        await ResponseWriter.SendAsync(context.Response, "OK", HttpStatusCode.OK);
    }

    public async Task CheckDatabaseAsync(HttpListenerContext context)
    {
        bool available;
        try
        {
            available = await _database.CheckConnectionAsync();
        }
        catch (NpgsqlException exception)
        {
            Console.Error.WriteLine($"Не удалось подключиться к PostgreSQL: {exception.Message}");
            available = false;
        }

        await ResponseWriter.SendAsync(
            context.Response,
            available ? "OK" : "База данных недоступна",
            available ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable);
    }
}
