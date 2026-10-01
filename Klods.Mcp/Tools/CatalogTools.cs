using System.ComponentModel;
using ModelContextProtocol.Server;
using static Klods.Mcp.KlodsApiClient;

namespace Klods.Mcp.Tools;

/// <summary>Read-only search over the shared catalog (whatever the site already imported; never Rebrickable).</summary>
[McpServerToolType]
public class CatalogTools(KlodsApiClient api)
{
    private const int MaxPageSize = 50;

    [McpServerTool(Name = "whoami", ReadOnly = true, Idempotent = true)]
    [Description("Returns the Klods username and API key name this connection acts as.")]
    public Task<string> WhoAmI() => api.GetJsonAsync("/api/auth/whoami");

    [McpServerTool(Name = "search_sets", ReadOnly = true, Idempotent = true)]
    [Description("Searches the site's set catalog by set number or name. Only sets an administrator has already imported " +
                 "are available. Results include userOwnedCount (how many copies you own). Returns { items, total }.")]
    public Task<string> SearchSets(
        [Description("Set number or name fragment, at least 2 characters. Omit to browse.")] string? query = null,
        [Description("Restrict to a theme id from list_themes.")] int? themeId = null,
        [Description("Sort by: year (default), name, id, pieces, theme.")] string? sort = null,
        [Description("Sort descending (default true).")] bool descending = true,
        [Description("Zero-based page number.")] int page = 0,
        [Description("Page size, 1-50.")] int pageSize = 25) =>
        api.GetJsonAsync($"/api/sets/catalog?q={Q(query)}{(themeId is int t ? $"&theme={t}" : "")}&sort={Q(sort)}&dir={Dir(descending)}" +
                         $"&page={page}&pageSize={Math.Clamp(pageSize, 1, MaxPageSize)}");

    [McpServerTool(Name = "list_themes", ReadOnly = true, Idempotent = true)]
    [Description("Lists set themes (id and name) that have sets in the catalog.")]
    public Task<string> ListThemes() => api.GetJsonAsync("/api/sets/themes");

    [McpServerTool(Name = "search_bricks", ReadOnly = true, Idempotent = true)]
    [Description("Searches the brick catalog by part number, name, or color name. Each row is one part in one color " +
                 "(partNum + colorId identify it). Returns { items, total }.")]
    public Task<string> SearchBricks(
        [Description("Part number, name, or color fragment, at least 2 characters. Omit to browse.")] string? query = null,
        [Description("Sort by: sets (default, most used first), id, name, color.")] string? sort = null,
        [Description("Sort descending (default true).")] bool descending = true,
        [Description("Zero-based page number.")] int page = 0,
        [Description("Page size, 1-50.")] int pageSize = 25) =>
        api.GetJsonAsync($"/api/bricks/catalog?q={Q(query)}&sort={Q(sort)}&dir={Dir(descending)}" +
                         $"&page={page}&pageSize={Math.Clamp(pageSize, 1, MaxPageSize)}");

    [McpServerTool(Name = "find_sets_with_brick", ReadOnly = true, Idempotent = true)]
    [Description("Lists catalog sets that contain a given part in a given color, with how many each set uses. Returns { items, total }.")]
    public Task<string> FindSetsWithBrick(
        [Description("Part number.")] string partNum,
        [Description("Color id.")] string colorId,
        [Description("Optional set number or name filter.")] string? query = null,
        [Description("Zero-based page number.")] int page = 0,
        [Description("Page size, 1-50.")] int pageSize = 25) =>
        api.GetJsonAsync($"/api/bricks/{Segment(partNum)}/{Segment(colorId)}/sets/paged?q={Q(query)}" +
                         $"&page={page}&pageSize={Math.Clamp(pageSize, 1, MaxPageSize)}");

    [McpServerTool(Name = "search_minifigs", ReadOnly = true, Idempotent = true)]
    [Description("Searches the minifig catalog by minifig id or name. Returns { items, total }.")]
    public Task<string> SearchMinifigs(
        [Description("Minifig id or name fragment, at least 2 characters. Omit to browse.")] string? query = null,
        [Description("Zero-based page number.")] int page = 0,
        [Description("Page size, 1-50.")] int pageSize = 25) =>
        api.GetJsonAsync($"/api/minifigs/catalog?q={Q(query)}&page={page}&pageSize={Math.Clamp(pageSize, 1, MaxPageSize)}");

    [McpServerTool(Name = "get_minifig_parts", ReadOnly = true, Idempotent = true)]
    [Description("Lists the parts (partNum, colorId, count) that make up a minifig.")]
    public Task<string> GetMinifigParts([Description("Minifig id, e.g. fig-000123.")] string minifigId) =>
        api.GetJsonAsync($"/api/minifigs/{Segment(minifigId)}/bricks");

    internal static string Q(string? value) => Uri.EscapeDataString(value ?? "");
    internal static string Dir(bool descending) => descending ? "desc" : "asc";
}
