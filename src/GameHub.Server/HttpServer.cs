using System.Net;

namespace GameHub.Server;

public class HttpServer
{
    private readonly Router _router;

    public HttpServer(Router router)
    {
        _router = router;
    }

    public async Task RunAsync(string prefix)
    {
        using var listener = new HttpListener();

        listener.Prefixes.Add(prefix);
        listener.Start();

        Console.WriteLine($"Сервер запущен: {prefix}");

        while (true)
        {
            var context = await listener.GetContextAsync();

            try
            {
                await _router.HandleAsync(context);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                await SendInternalServerErrorAsync(context.Response);
            }
        }
    }

    private static async Task SendInternalServerErrorAsync(HttpListenerResponse response)
    {
        try
        {
            await ResponseWriter.SendAsync(
                response,
                "Внутренняя ошибка сервера",
                HttpStatusCode.InternalServerError);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            response.Abort();
        }
    }
}
