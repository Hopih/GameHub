using System.Net;

namespace GameHub.Server.Controllers;

public class HomeController
{
    public async Task IndexAsync(HttpListenerContext context)
    {
        string viewPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
        string html = await File.ReadAllTextAsync(viewPath);

        await ResponseWriter.SendAsync(context.Response, html, HttpStatusCode.OK, "text/html; charset=utf-8");
    }
}
