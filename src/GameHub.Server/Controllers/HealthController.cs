using System.Net;

namespace GameHub.Server.Controllers;

public class HealthController
{
    public async Task CheckHealthAsync(HttpListenerContext context)
    {
        await ResponseWriter.SendAsync(context.Response, "OK", HttpStatusCode.OK);
    }
}
