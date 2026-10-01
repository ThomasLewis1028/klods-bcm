using System.ComponentModel;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using static Klods.Mcp.KlodsApiClient;
using static Klods.Mcp.Tools.CatalogTools;

namespace Klods.Mcp.Tools;

/// <summary>The caller's loose bricks and minifig copies.</summary>
[McpServerToolType]
public class LooseTools(KlodsApiClient api)
{
    private const int MaxPageSize = 200;

    [McpServerTool(Name = "list_my_bricks", ReadOnly = true, Idempotent = true)]
    [Description("Lists bricks relevant to you: ones in your loose pile plus ones your sets need. Per brick: stock " +
                 "(loose), userNeeded (total across your sets), userSetCount. Returns { items, total }.")]
    public async Task<string> ListMyBricks(
        [Description("Filter by part number, name, or color name.")] string? query = null,
        [Description("Only bricks you actually have in your loose pile (stock > 0).")] bool ownedOnly = false,
        [Description("Zero-based page number.")] int page = 0,
        [Description("Page size, 1-200.")] int pageSize = 50) =>
        Page(await api.GetAsync("/api/mybricks/"), query, ["partNum", "name", "colorName"], page, pageSize,
            ownedOnly ? b => (int?)b["stock"] > 0 : null);

    [McpServerTool(Name = "get_loose_brick", ReadOnly = true, Idempotent = true)]
    [Description("Returns your loose stock, storage location, and notes for one part in one color.")]
    public Task<string> GetLooseBrick(
        [Description("Part number.")] string partNum,
        [Description("Color id.")] string colorId) =>
        api.GetJsonAsync($"/api/bricks/{Segment(partNum)}/{Segment(colorId)}/owned");

    [McpServerTool(Name = "list_my_sets_needing_brick", ReadOnly = true, Idempotent = true)]
    [Description("Lists your owned sets that use a given part in a given color, with how many each needs and how many copies you own.")]
    public Task<string> ListMySetsNeedingBrick(
        [Description("Part number.")] string partNum,
        [Description("Color id.")] string colorId) =>
        api.GetJsonAsync($"/api/mybricks/{Segment(partNum)}/{Segment(colorId)}/sets");

    [McpServerTool(Name = "set_loose_brick_stock", Idempotent = true)]
    [Description("Sets how many of a part you have in your loose pile (absolute value, not a delta). The part/color " +
                 "must exist in the catalog.")]
    public Task<string> SetLooseBrickStock(
        [Description("Part number.")] string partNum,
        [Description("Color id.")] string colorId,
        [Description("New stock, 0 or more.")] int stock) =>
        api.WriteAsync(HttpMethod.Put, $"/api/mybricks/{Segment(partNum)}/{Segment(colorId)}/stock",
            new { Stock = stock }, "Loose stock updated.", "That part/color isn't in the catalog.");

    [McpServerTool(Name = "set_loose_brick_notes", Idempotent = true)]
    [Description("Sets the storage location and notes for a part in your loose pile. Omitted or empty values clear the field.")]
    public Task<string> SetLooseBrickNotes(
        [Description("Part number.")] string partNum,
        [Description("Color id.")] string colorId,
        [Description("Where it's stored, up to 100 characters.")] string? location = null,
        [Description("Free-form notes, up to 2000 characters.")] string? notes = null) =>
        api.WriteAsync(HttpMethod.Put, $"/api/bricks/owned/{Segment(partNum)}/{Segment(colorId)}/notes",
            new { Location = location, Notes = notes }, "Notes saved.", "That part/color isn't in the catalog.");

    [McpServerTool(Name = "list_my_minifigs", ReadOnly = true, Idempotent = true)]
    [Description("Lists minifigs relevant to you: ones you own plus ones your sets need. Per minifig: stock (loose copies), " +
                 "inUseStock (copies assigned to set copies), userNeeded. Returns { items, total }.")]
    public async Task<string> ListMyMinifigs(
        [Description("Filter by minifig id or name.")] string? query = null,
        [Description("Zero-based page number.")] int page = 0,
        [Description("Page size, 1-200.")] int pageSize = 50) =>
        Page(await api.GetAsync("/api/myminifigs/"), query, ["minifigId", "minifigName"], page, pageSize);

    [McpServerTool(Name = "list_minifig_copies", ReadOnly = true, Idempotent = true)]
    [Description("Lists every copy you own of a minifig: its index, which set copy it's assigned to (or loose), and per-part stock.")]
    public Task<string> ListMinifigCopies([Description("Minifig id.")] string minifigId) =>
        api.GetJsonAsync($"/api/myminifigs/{Segment(minifigId)}/instances");

    [McpServerTool(Name = "list_assignable_set_copies", ReadOnly = true, Idempotent = true)]
    [Description("Lists your set copies that include this minifig and still have a free slot for it.")]
    public Task<string> ListAssignableSetCopies([Description("Minifig id.")] string minifigId) =>
        api.GetJsonAsync($"/api/myminifigs/{Segment(minifigId)}/assignable-copies");

    [McpServerTool(Name = "add_loose_minifig")]
    [Description("Adds one loose copy of a catalog minifig to your collection and returns its index.")]
    public Task<string> AddLooseMinifig([Description("Minifig id.")] string minifigId) =>
        api.WriteAsync(HttpMethod.Post, $"/api/myminifigs/{Segment(minifigId)}/instances", null,
            "Minifig copy added.", "That minifig isn't in the catalog.");

    [McpServerTool(Name = "remove_minifig_copy", Destructive = true)]
    [Description("Removes one copy of a minifig (and its per-part stock) from your collection.")]
    public Task<string> RemoveMinifigCopy(
        [Description("Minifig id.")] string minifigId,
        [Description("Copy index from list_minifig_copies.")] int index) =>
        api.WriteAsync(HttpMethod.Delete, $"/api/myminifigs/{Segment(minifigId)}/instances/{index}", null,
            "Minifig copy removed.", "You don't own that minifig copy.");

    [McpServerTool(Name = "assign_minifig_copy", Idempotent = true)]
    [Description("Assigns a minifig copy to one of your set copies, or makes it loose when setId and setIndex are omitted.")]
    public Task<string> AssignMinifigCopy(
        [Description("Minifig id.")] string minifigId,
        [Description("Copy index from list_minifig_copies.")] int index,
        [Description("Target set number, or omit to make the copy loose.")] string? setId = null,
        [Description("Target set copy index, or omit to make the copy loose.")] int? setIndex = null) =>
        api.WriteAsync(HttpMethod.Patch, $"/api/myminifigs/{Segment(minifigId)}/instances/{index}/assign",
            new { SetId = setId, SetIndex = setIndex }, "Minifig copy reassigned.", "You don't own that minifig copy.");

    [McpServerTool(Name = "set_minifig_copy_part_stock", Idempotent = true)]
    [Description("Sets how many of one part you have for a specific minifig copy (absolute value).")]
    public Task<string> SetMinifigCopyPartStock(
        [Description("Minifig id.")] string minifigId,
        [Description("Copy index from list_minifig_copies.")] int index,
        [Description("Part number from get_minifig_parts.")] string partNum,
        [Description("Color id.")] string colorId,
        [Description("New stock, 0 or more.")] int stock) =>
        api.WriteAsync(HttpMethod.Patch,
            $"/api/myminifigs/{Segment(minifigId)}/instances/{index}/parts/{Segment(partNum)}/{Segment(colorId)}",
            new { Stock = stock }, "Stock updated.", "You don't own that minifig copy, or that part isn't in this minifig.");

    // The API returns these collections whole; filter and page here so a large collection doesn't flood the model.
    internal static string Page(JsonNode? list, string? query, string[] fields, int page, int pageSize,
        Func<JsonNode, bool>? keep = null)
    {
        var items = (list as JsonArray ?? []).Where(i => i is not null).Select(i => i!);
        if (keep is not null) items = items.Where(keep);
        if (!string.IsNullOrWhiteSpace(query))
            items = items.Where(i => fields.Any(f =>
                i[f]?.GetValue<string>().Contains(query, StringComparison.OrdinalIgnoreCase) == true));

        var matched = items.ToList();
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var pageItems = matched.Skip(Math.Max(page, 0) * pageSize).Take(pageSize).Select(i => i.DeepClone()).ToArray();
        return new JsonObject { ["items"] = new JsonArray(pageItems), ["total"] = matched.Count }.ToJsonString();
    }
}
