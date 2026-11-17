using System.Net;

namespace GameHub.Server;

public class StaticFileHandler
{
    private readonly string _rootPath;
    private readonly Dictionary<string, string> _contentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".css"] = "text/css; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".html"] = "text/html; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
        [".ico"] = "image/x-icon"
    };

    public StaticFileHandler(string rootPath)
    {
        _rootPath = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
    }

    public async Task<bool> TryHandleAsync(HttpListenerContext context)
    {
        if (context.Request.HttpMethod != "GET") return false;

        string path = Uri.UnescapeDataString(context.Request.Url?.AbsolutePath ?? "");
        if (path.Contains('\\') || path.Contains('\0') || path.Split('/').Contains("..")) return false;

        string filePath = Path.GetFullPath(Path.Combine(_rootPath, path.TrimStart('/')));
        if (!filePath.StartsWith(_rootPath, StringComparison.OrdinalIgnoreCase)
            || !_contentTypes.TryGetValue(Path.GetExtension(filePath), out string? contentType)
            || !File.Exists(filePath))
        {
            return false;
        }

        byte[] content = await File.ReadAllBytesAsync(filePath);
        context.Response.StatusCode = (int)HttpStatusCode.OK;
        context.Response.ContentType = contentType;
        context.Response.ContentLength64 = content.Length;
        await context.Response.OutputStream.WriteAsync(content);
        context.Response.Close();
        return true;
    }
}
