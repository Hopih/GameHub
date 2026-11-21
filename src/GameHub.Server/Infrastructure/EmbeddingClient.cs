using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GameHub.Server.Infrastructure;

internal sealed class EmbeddingClient(HttpClient http, Uri url)
{
    public const int Dimensions = 384;
    public const int BatchSize = 16;

    public async Task<string> GetModelVersionAsync(CancellationToken cancellationToken)
    {
        var model = await http.GetFromJsonAsync<JsonElement>(new Uri(url, "/info"), cancellationToken);
        return VersionHash(model.GetProperty("model_id").GetString() + "|" + model.GetProperty("model_sha").GetString());
    }

    public async Task<float[][]> EmbedAsync(string[] inputs, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(new Uri(url, "/embed"), new { inputs, truncate = true }, cancellationToken);
        response.EnsureSuccessStatusCode();
        var vectors = await response.Content.ReadFromJsonAsync<float[][]>(cancellationToken);
        if (vectors is null || vectors.Length != inputs.Length || vectors.Any(vector => vector.Length != Dimensions))
            throw new InvalidDataException($"Модель должна вернуть один вектор из {Dimensions} чисел на каждый текст.");
        return vectors;
    }

    public static string VersionHash(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)).AsSpan(0, 8));
}
