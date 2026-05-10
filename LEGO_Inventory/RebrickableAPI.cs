using System.Text.Json.Nodes;

namespace LEGO_Inventory;

public class RebrickableApi
{
    private const string BaseUrl = "https://rebrickable.com/api/v3/lego/";
    private const int PageSize = 1000;

    private static readonly HttpClient _httpClient = new();

    private readonly ILogger<RebrickableApi> _logger =
        LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<RebrickableApi>();

    // ── Single-object endpoints ──────────────────────────────────────────────

    public async Task<JsonObject?> GetSetInfo(string? setId)
    {
        _logger.LogInformation("Getting set info for {SetId}", setId);
        return await SendQuery($"{BaseUrl}sets/{setId}/?");
    }

    public async Task<JsonObject?> GetMinifigInfo(string itemNum)
    {
        _logger.LogInformation("Getting minifig info for {ItemNum}", itemNum);
        return await SendQuery($"{BaseUrl}minifigs/{itemNum}/?");
    }

    // ── List endpoints (auto-paginated, returns all results) ─────────────────

    public async Task<JsonArray?> GetSetParts(string setId)
    {
        _logger.LogInformation("Getting set parts for {SetId}", setId);
        return await GetAllPagesAsync($"{BaseUrl}sets/{setId}/parts/?page_size={PageSize}&inc_part_details=1&");
    }

    public async Task<JsonArray?> GetSetMinifigs(string setId)
    {
        _logger.LogInformation("Getting set minifigs for {SetId}", setId);
        return await GetAllPagesAsync($"{BaseUrl}sets/{setId}/minifigs/?page_size={PageSize}&");
    }

    public async Task<JsonArray?> GetMinifigParts(string itemNum)
    {
        _logger.LogInformation("Getting minifig parts for {ItemNum}", itemNum);
        return await GetAllPagesAsync($"{BaseUrl}minifigs/{itemNum}/parts/?page_size={PageSize}&inc_part_details=1&");
    }

    public async Task<JsonArray?> GetPartColors(string partNum)
    {
        _logger.LogInformation("Getting colors for part {PartNum}", partNum);
        return await GetAllPagesAsync($"{BaseUrl}parts/{partNum}/colors/?page_size={PageSize}&");
    }

    // ── Search endpoint (single page — caller controls pagination) ───────────

    /// <summary>
    /// Returns one page of set search results. Response includes count, next, previous, and results.
    /// Use count and next to implement UI pagination.
    /// </summary>
    public async Task<JsonObject?> SearchSets(string query, int page = 1)
    {
        _logger.LogInformation("Searching sets for {Query} (page {Page})", query, page);
        return await SendQuery($"{BaseUrl}sets/?search={Uri.EscapeDataString(query)}&page={page}&page_size=25&");
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task<JsonArray> GetAllPagesAsync(string baseUrl)
    {
        var allResults = new JsonArray();
        string? apiKey = Environment.GetEnvironmentVariable("LEGO_API_KEY");

        // First page: our convention is that baseUrl ends with & or ?
        Uri nextUri = new Uri($"{baseUrl}key={apiKey}");

        while (true)
        {
            var page = await FetchAsync(nextUri);

            if (page?["results"] is JsonArray results)
            {
                foreach (var item in results)
                    allResults.Add(item?.DeepClone());
            }

            var nextUrl = page?["next"]?.ToString();
            if (nextUrl == null)
                break;

            // Rebrickable's "next" URL contains all params but not the API key
            nextUri = new Uri($"{nextUrl}&key={apiKey}");
        }

        return allResults;
    }

    private async Task<JsonObject?> SendQuery(string url)
    {
        _logger.LogTrace("API Call {Url}", url);
        string? apiKey = Environment.GetEnvironmentVariable("LEGO_API_KEY");
        return await FetchAsync(new Uri($"{url}key={apiKey}"));
    }

    private async Task<JsonObject?> FetchAsync(Uri uri)
    {
        try
        {
            var startTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var response = await _httpClient.GetAsync(uri);
            var endTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var diff = endTime - startTime;

            if (diff < 1000)
                await Task.Delay(1000 - (int)diff);

            if (response.IsSuccessStatusCode)
                return JsonNode.Parse(await response.Content.ReadAsStringAsync())?.AsObject();

            throw new Exception($"Rebrickable API returned status code {(int)response.StatusCode}.");
        }
        catch (Exception e)
        {
            _logger.LogTrace("FetchAsync Exception: {Message}", e.Message);
            throw;
        }
    }
}
