using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;

namespace Klods.Mcp;

/// <summary>
/// Calls Klods.Api with the MCP caller's own key, so the API enforces identity, the endpoint allowlist,
/// and the per-user rate limit on every tool call. Failures become <see cref="McpException"/>s, whose
/// message the SDK hands back to the model as the tool error.
/// </summary>
public class KlodsApiClient(HttpClient http, IHttpContextAccessor httpContextAccessor)
{
    // Image URLs are useless to a model and dominate the payload size of every list.
    private static readonly HashSet<string> DroppedProperties = ["partImg", "setImg", "imgUrl", "subPartImg"];

    public async Task<JsonNode?> GetAsync(string path, string notFound = "Not found.")
    {
        using var resp = await SendAsync(HttpMethod.Get, path, null, notFound);
        var node = await resp.Content.ReadFromJsonAsync<JsonNode>();
        StripImages(node);
        return node;
    }

    public async Task<string> GetJsonAsync(string path, string notFound = "Not found.") =>
        (await GetAsync(path, notFound))?.ToJsonString() ?? "null";

    /// <summary>Sends a write; returns the response body, or <paramref name="success"/> when there isn't one.</summary>
    public async Task<string> WriteAsync(HttpMethod method, string path, object? body, string success, string notFound = "Not found.")
    {
        using var resp = await SendAsync(method, path, body, notFound);
        var text = await resp.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(text) ? success : $"{success} {text}";
    }

    public static string Segment(string value) => Uri.EscapeDataString(value);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, string notFound)
    {
        using var req = new HttpRequestMessage(method, path);
        req.Headers.Authorization = AuthenticationHeaderValue.Parse(
            httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString()
            ?? throw new McpException("No API key on the request."));
        if (body is not null) req.Content = JsonContent.Create(body);

        var resp = await http.SendAsync(req);
        if (resp.IsSuccessStatusCode) return resp;

        using (resp)
        {
            throw resp.StatusCode switch
            {
                HttpStatusCode.Unauthorized => new McpException(
                    "The Klods server rejected this API key (revoked, MCP access turned off by the administrator, or the account is inactive)."),
                HttpStatusCode.Forbidden => new McpException("Not permitted."),
                HttpStatusCode.NotFound => new McpException(notFound),
                HttpStatusCode.TooManyRequests => new McpException(
                    $"Rate limited by the Klods server. Retry in {RetryAfterSeconds(resp)} seconds."),
                HttpStatusCode.BadRequest => new McpException(await ReadMessageAsync(resp)),
                _ => new McpException($"The Klods server returned {(int)resp.StatusCode}."),
            };
        }
    }

    private static int RetryAfterSeconds(HttpResponseMessage resp) =>
        resp.Headers.RetryAfter?.Delta is { } delta ? (int)Math.Ceiling(delta.TotalSeconds) : 60;

    // The API returns plain-string errors JSON-encoded ("\"message\"").
    private static async Task<string> ReadMessageAsync(HttpResponseMessage resp)
    {
        var text = await resp.Content.ReadAsStringAsync();
        try { return JsonSerializer.Deserialize<string>(text) ?? "Bad request."; }
        catch (JsonException) { return string.IsNullOrWhiteSpace(text) ? "Bad request." : text; }
    }

    private static void StripImages(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var name in DroppedProperties) obj.Remove(name);
                foreach (var (_, child) in obj) StripImages(child);
                break;
            case JsonArray arr:
                foreach (var child in arr) StripImages(child);
                break;
        }
    }
}
