using System.Net;
using System.Text;

namespace GameHub.Server;

public static class ResponseWriter
{
    public static async Task SendAsync(
        HttpListenerResponse response,
        string text,
        HttpStatusCode statusCode,
        string contentType = "text/plain; charset=utf-8")
    {
        byte[] body = Encoding.UTF8.GetBytes(text);
        
        response.StatusCode = (int)statusCode;
        response.ContentType = contentType;
        response.ContentLength64 = body.Length;
        await response.OutputStream.WriteAsync(body);
        response.Close();
    }
}
