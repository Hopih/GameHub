using System.Net;

namespace GameHub.Server.Controllers;

public class HomeController
{
    public Task IndexAsync(HttpListenerContext context) => SendPageAsync(context, "index.html");

    public Task LibraryAsync(HttpListenerContext context) => SendPageAsync(context, "library.html");

    public Task RecommendationsAsync(HttpListenerContext context) => SendPageAsync(context, "recommendations.html");

    private static async Task SendPageAsync(HttpListenerContext context, string fileName)
    {
        string viewPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", fileName);
        string html = await File.ReadAllTextAsync(viewPath);

        await ResponseWriter.SendAsync(context.Response, html, HttpStatusCode.OK, "text/html; charset=utf-8");
    }
}
