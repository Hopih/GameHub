using System.Net;

namespace GameHub.Server;

public class HttpServer
{
    private const int MaxConcurrentRequests = 32;
    private readonly Router _router;
    private readonly StaticFileHandler _staticFiles;

    public HttpServer(Router router, StaticFileHandler staticFiles)
    {
        _router = router;
        _staticFiles = staticFiles;
    }

    public async Task RunAsync(string prefix)
    {
        using var listener = new HttpListener();

        listener.Prefixes.Add(prefix);
        listener.Start();

        Console.WriteLine($"Сервер запущен: {prefix}");

        await Task.WhenAll(Enumerable.Range(0, MaxConcurrentRequests)
            .Select(_ => ProcessRequestsAsync(listener)));
    }

    private async Task ProcessRequestsAsync(HttpListener listener)
    {
        while (true)
        {
            var context = await listener.GetContextAsync();

            try
            {
                if (await _staticFiles.TryHandleAsync(context))
                {
                    continue;
                }

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
