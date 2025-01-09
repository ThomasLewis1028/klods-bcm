using System.Text.Json.Nodes;

namespace LEGO_Inventory;


public class RebrickableApi
{
    private const string BaseUrl = "https://rebrickable.com/api/v3/lego/";
    
    
    public async Task<JsonObject?> GetSetInfo(string? setId)
    {
        string url = $"{BaseUrl}sets/{setId}/?page_size=1000000&";

        return await SendQuery(url);
    }
    
    
    public async Task<JsonObject?> GetSetParts(string setId)
    {
        string url = $"{BaseUrl}sets/{setId}/parts/?page_size=1000000&inc_minifig_parts=1&";

        return await SendQuery(url);
    }
    
    
    public async Task<JsonObject?> GetPartInfo(string partNum)
    {
        string url = $"{BaseUrl}parts/{partNum}/?";

        return await SendQuery(url);
    }

    private async Task<JsonObject?> SendQuery(string url)
    {
        try
        {
            HttpClient client = new HttpClient();
            
            string? apiKey = Environment.GetEnvironmentVariable("LEGO_API_KEY");
                
            HttpResponseMessage response;
            Uri uri = new Uri($"{url}key={apiKey}");
            response = client.GetAsync(uri).Result;
                

            if (response.IsSuccessStatusCode)
                return JsonNode.Parse(await response.Content.ReadAsStringAsync())?.AsObject();
                
            throw new Exception($"Rebrickable API returned status code {(int)response.StatusCode}.");
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            
            throw;
        }
    }
}