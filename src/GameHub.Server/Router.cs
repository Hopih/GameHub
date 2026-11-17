using System.Net;

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
}
