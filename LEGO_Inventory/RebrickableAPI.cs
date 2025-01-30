using System.Text.Json.Nodes;

namespace LEGO_Inventory;

public class RebrickableApi
{
    private const string BaseUrl = "https://rebrickable.com/api/v3/lego/";

    private readonly ILogger<RebrickableApi> _logger =
        LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<RebrickableApi>();

    public async Task<JsonObject?> GetSetInfo(string? setId)
    {
        _logger.LogInformation("Getting set info for {setId}", setId);
        string url = $"{BaseUrl}sets/{setId}/?page_size=1000000&";

        return await SendQuery(url);
    }


    public async Task<JsonObject?> GetSetParts(string setId)
    {
        _logger.LogInformation("Getting set parts for {setId}", setId);
        string url = $"{BaseUrl}sets/{setId}/parts/?page_size=1000000&";

        return await SendQuery(url);
    }


    public async Task<JsonObject?> GetPartInfo(string partNum)
    {
        _logger.LogInformation("Getting part info for {partNum}", partNum);
        string url = $"{BaseUrl}parts/{partNum}/?";

        return await SendQuery(url);
    }
    
    
    public async Task<JsonObject?> GetColorInfo(string colorId)
    {
        _logger.LogInformation("Getting color info for color {colorId}", colorId);
        string url = $"{BaseUrl}colors/{colorId}/?";

        return await SendQuery(url);
    }
    
    public async Task<JsonObject?> GetPartColorInfo(string partNum, string colorId)
    {
        _logger.LogInformation("Getting part info for {partNum}", partNum);
        string url = $"{BaseUrl}parts/{partNum}/colors/{colorId}?";

        return await SendQuery(url);
    }

    public async Task<JsonObject?> GetSetMinifigs(string setId)
    {
        _logger.LogInformation("Getting minifig info for {setId}", setId);
        string url = $"{BaseUrl}sets/{setId}/minifigs?";

        return await SendQuery(url);
    }

    public async Task<JsonObject?> GetMinifigInfo(string itemNum)
    {
        _logger.LogInformation("Getting minifig info for {itemNum}", itemNum);
        string url = $"{BaseUrl}minifigs/{itemNum}/?";

        return await SendQuery(url);
    }

    public async Task<JsonObject?> GetMinifigParts(string itemNum)
    {
        _logger.LogInformation("Getting minifig Parts for {itemNum}", itemNum);
        string url = $"{BaseUrl}minifigs/{itemNum}/parts/?page_size=1000000&inc_minifig_parts=1&";

        return await SendQuery(url);
    }

    public async Task<JsonObject?> GetColors()
    {
        _logger.LogInformation("Getting colors");
        string url = $"{BaseUrl}colors?page_size=1000000&";
        
        return await SendQuery(url);
    }

    private async Task<JsonObject?> SendQuery(string url)
    {
        _logger.LogTrace("API Call {url}", url);
        try
        {
            HttpClient client = new HttpClient();

            string? apiKey = Environment.GetEnvironmentVariable("LEGO_API_KEY");

            HttpResponseMessage response;
            Uri uri = new Uri($"{url}key={apiKey}");

            var startTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            response = client.GetAsync(uri).Result;
            var endTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var diff = endTime - startTime;

            // If the time it took is less than 1 second, sleep for the remaining time
            // This prevents getting a timeout on the API
            if (diff < 1000)
                Thread.Sleep(1000 - (int)diff);

            if (response.IsSuccessStatusCode)
                return JsonNode.Parse(await response.Content.ReadAsStringAsync())?.AsObject();

            throw new Exception($"Rebrickable API returned status code {(int)response.StatusCode}.");
        }
        catch (Exception e)
        {
            _logger.LogTrace("SendQuery Exception: {e}", e.Message);

            throw;
        }
    }
}