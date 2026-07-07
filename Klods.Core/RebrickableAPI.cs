using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Klods;

/// <summary>A recently-added set from Rebrickable's public sets RSS feed.</summary>
public record RssSetItem(string SetNum, string Name, DateTime PubDate);

/// <summary>A catalog set with its upstream last-modified timestamp, from the ordered sets list.</summary>
public record ModifiedSetItem(string SetNum, DateTime LastModified);

/// <summary>
/// Thrown when the Rebrickable API returns a non-success status. Carries the HTTP status code so
/// callers can distinguish a permanent 404 from a transient 429/5xx (worth retrying).
/// </summary>
public class RebrickableApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public class RebrickableApi
{
    private const string BaseUrl = "https://rebrickable.com/api/v3/lego/";
    private const int PageSize = 1000;

    private static readonly HttpClient _httpClient = new();

    private readonly ILogger<RebrickableApi> _logger;

    public RebrickableApi(ILogger<RebrickableApi>? logger = null)
    {
        _logger = logger ?? NullLogger<RebrickableApi>.Instance;
    }

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

    public async Task<JsonObject?> GetTheme(int themeId)
    {
        _logger.LogInformation("Getting theme {ThemeId}", themeId);
        return await SendQuery($"{BaseUrl}themes/{themeId}/?");
    }

    public async Task<JsonObject?> GetPartInfo(string partNum)
    {
        _logger.LogInformation("Getting part info for {PartNum}", partNum);
        return await SendQuery($"{BaseUrl}parts/{partNum}/?");
    }

    public async Task<JsonObject?> SearchMinifigs(string query, int page = 1)
    {
        _logger.LogInformation("Searching minifigs for {Query} (page {Page})", query, page);
        return await SendQuery($"{BaseUrl}minifigs/?search={Uri.EscapeDataString(query)}&page={page}&page_size=25&");
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

    public async Task<JsonArray?> GetColors()
    {
        _logger.LogInformation("Getting all colors");
        return await GetAllPagesAsync($"{BaseUrl}colors/?page_size={PageSize}&");
    }

    public async Task<JsonArray?> GetPartCategories()
    {
        _logger.LogInformation("Getting all part categories");
        return await GetAllPagesAsync($"{BaseUrl}part_categories/?page_size={PageSize}&");
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

    // ── Catalog change feed (sets ordered by last-modified, newest first) ────

    /// <summary>
    /// Streams catalog sets newest-modified-first, stopping as soon as a set's last_modified_dt is at
    /// or before <paramref name="since"/>. Returns only sets modified strictly after <paramref name="since"/>,
    /// oldest of those last. One page (1000) spans ~2 months of catalog churn, so this is normally a
    /// single API call; it follows pagination only when the gap since the last poll is very large.
    /// </summary>
    public async Task<List<ModifiedSetItem>> GetSetsModifiedSince(DateTime since, CancellationToken ct = default)
    {
        _logger.LogInformation("Fetching sets modified since {Since:o}", since);
        var results = new List<ModifiedSetItem>();
        string? apiKey = Environment.GetEnvironmentVariable("REBRICKABLE_API_KEY");

        Uri nextUri = new Uri($"{BaseUrl}sets/?ordering=-last_modified_dt&page_size={PageSize}&key={apiKey}");

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var page = await FetchAsync(nextUri);
            if (page?["results"] is not JsonArray rows)
                break;

            var crossedWatermark = false;
            foreach (var row in rows)
            {
                var lmRaw = row?["last_modified_dt"]?.ToString();
                var setNum = row?["set_num"]?.ToString();
                if (lmRaw == null || setNum == null)
                    continue;

                var lastModified = DateTime.Parse(lmRaw).ToUniversalTime();
                if (lastModified <= since) { crossedWatermark = true; break; }

                results.Add(new ModifiedSetItem(setNum, lastModified));
            }

            if (crossedWatermark)
                break;

            var nextUrl = page?["next"]?.ToString();
            if (nextUrl == null)
                break;

            nextUri = new Uri($"{nextUrl}&key={apiKey}");
        }

        _logger.LogInformation("Found {Count} sets modified since {Since:o}", results.Count, since);
        return results;
    }

    // ── Public RSS feed (no API key; newest sets first) ──────────────────────

    /// <summary>Fetches Rebrickable's public "newest sets" RSS feed and parses out the set numbers + dates.</summary>
    public async Task<List<RssSetItem>> GetRecentSetsFromRssAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Fetching Rebrickable sets RSS feed");
        var xml = await _httpClient.GetStringAsync("https://rebrickable.com/sets/rss/", ct);
        var doc = XDocument.Parse(xml);

        var items = new List<RssSetItem>();
        var skippedNonSets = 0;
        foreach (var item in doc.Descendants("item"))
        {
            // title is "<set_num> <name>", e.g. "75192-1 Millennium Falcon"
            var title = item.Element("title")?.Value?.Trim() ?? "";
            var split = title.Split(' ', 2);
            var setNum = split[0];
            if (setNum.Length == 0) continue;

            // The feed also carries minifigs (fig-*) and alternate-build/subset variants
            // ("31313-1-b11", "60509-1-s1") that don't exist on the sets API — skip them so we
            // don't spend rate-limited calls 404ing on entries that were never sets.
            if (!IsSetNumber(setNum)) { skippedNonSets++; continue; }

            var name = split.Length > 1 ? split[1] : setNum;
            if (!DateTimeOffset.TryParse(item.Element("pubDate")?.Value, out var pub)) continue;

            items.Add(new RssSetItem(setNum, name, pub.UtcDateTime));
        }

        _logger.LogInformation("RSS feed: {SetCount} sets, {NonSetCount} non-set entries skipped",
            items.Count, skippedNonSets);
        return items;
    }

    // A real catalog set number is "<base>-<variant>" with a numeric variant: "42238-1", "POSTER-3".
    // Minifigs ("fig-017933") and alternate builds/subsets ("31313-1-b11", "60509-1-s1") don't match.
    private static readonly Regex SetNumberPattern = new(@"^\w+-\d+$", RegexOptions.Compiled);

    private static bool IsSetNumber(string setNum) =>
        !setNum.StartsWith("fig-", StringComparison.OrdinalIgnoreCase) && SetNumberPattern.IsMatch(setNum);

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task<JsonArray> GetAllPagesAsync(string baseUrl)
    {
        var allResults = new JsonArray();
        string? apiKey = Environment.GetEnvironmentVariable("REBRICKABLE_API_KEY");

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
        _logger.LogInformation("API Call {Url}", url);
        string? apiKey = Environment.GetEnvironmentVariable("REBRICKABLE_API_KEY");
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

            var body = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
                return JsonNode.Parse(body)?.AsObject();

            var safeUri = uri.GetLeftPart(UriPartial.Path);
            throw new RebrickableApiException((int)response.StatusCode,
                $"Rebrickable API {(int)response.StatusCode} at {safeUri} — {body}");
        }
        catch (Exception e)
        {
            _logger.LogTrace("FetchAsync Exception: {Message}", e.Message);
            throw;
        }
    }
}
