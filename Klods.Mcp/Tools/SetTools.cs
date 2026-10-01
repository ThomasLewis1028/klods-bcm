using System.ComponentModel;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using static Klods.Mcp.KlodsApiClient;

namespace Klods.Mcp.Tools;

/// <summary>The caller's owned set copies: inventory, per-part stock, notes, and substitutions.</summary>
[McpServerToolType]
public class SetTools(KlodsApiClient api)
{
    private const string NoCopy = "You don't own that set copy (check setId and setIndex with list_my_sets).";

    [McpServerTool(Name = "list_my_sets", ReadOnly = true, Idempotent = true)]
    [Description("Lists the sets you own. Each set has instances: one per physical copy, identified by setIndex, with " +
                 "completeness (percent, status, missingPieceCount), location and notes.")]
    public Task<string> ListMySets() => api.GetJsonAsync("/api/sets/my-owned");

    [McpServerTool(Name = "get_set_copy", ReadOnly = true, Idempotent = true)]
    [Description("Returns the parts list of one owned set copy. Per brick: count (needed), setStock (assigned to this copy), " +
                 "looseStock (your loose pile). Per minifig: count and ownedStock. Also completeness, location, and notes.")]
    public async Task<string> GetSetCopy(
        [Description("Set number, e.g. 75192-1.")] string setId,
        [Description("Which copy (from list_my_sets instances).")] int setIndex,
        [Description("Only return bricks and minifigs this copy is still short of.")] bool onlyMissing = false)
    {
        var bom = await api.GetAsync($"/api/bom/{Segment(setId)}/{setIndex}", NoCopy);
        if (onlyMissing && bom is JsonObject obj)
        {
            Keep(obj, "bricks", b => (int?)b["setStock"] < (int?)b["count"]);
            Keep(obj, "minifigs", m => (int?)m["ownedStock"] < (int?)m["count"]);
        }
        return bom?.ToJsonString() ?? "null";
    }

    [McpServerTool(Name = "add_set_copy", Destructive = false)]
    [Description("Adds a copy of a catalog set to your collection. The set must already be in the catalog (search_sets); " +
                 "this never imports from Rebrickable.")]
    public Task<string> AddSetCopy(
        [Description("Set number, e.g. 75192-1.")] string setId,
        [Description("Mark every part as already owned for this copy (a complete, built set). Default false: all parts start at zero.")]
        bool markAllPartsOwned = false) =>
        api.WriteAsync(HttpMethod.Post, "/api/sets/owned", new { SetId = setId, ApplyBricks = markAllPartsOwned },
            "Set copy added; use list_my_sets for its setIndex.", "That set isn't in the catalog. Ask an administrator to import it.");

    [McpServerTool(Name = "remove_set_copy", Destructive = true)]
    [Description("Removes one owned set copy and its per-part stock from your collection.")]
    public Task<string> RemoveSetCopy(
        [Description("Set number.")] string setId,
        [Description("Which copy.")] int setIndex,
        [Description("Move this copy's assigned parts into your loose pile instead of discarding them.")] bool moveStockToLoose) =>
        api.WriteAsync(HttpMethod.Delete, $"/api/sets/owned/{Segment(setId)}/{setIndex}?moveStock={(moveStockToLoose ? "true" : "false")}",
            null, "Set copy removed.", NoCopy);

    [McpServerTool(Name = "set_set_copy_notes", Idempotent = true)]
    [Description("Sets the storage location and notes of an owned set copy. Omitted or empty values clear the field.")]
    public Task<string> SetSetCopyNotes(
        [Description("Set number.")] string setId,
        [Description("Which copy.")] int setIndex,
        [Description("Where it's stored, up to 100 characters.")] string? location = null,
        [Description("Free-form notes, up to 2000 characters.")] string? notes = null) =>
        api.WriteAsync(HttpMethod.Put, $"/api/sets/owned/{Segment(setId)}/{setIndex}/notes",
            new { Location = location, Notes = notes }, "Notes saved.", NoCopy);

    [McpServerTool(Name = "set_set_copy_part_stock", Idempotent = true)]
    [Description("Sets how many of a part are assigned to an owned set copy (absolute value, not a delta). " +
                 "Does not touch your loose pile.")]
    public Task<string> SetSetCopyPartStock(
        [Description("Set number.")] string setId,
        [Description("Which copy.")] int setIndex,
        [Description("Part number from get_set_copy.")] string partNum,
        [Description("Color id from get_set_copy.")] string colorId,
        [Description("New stock, 0 or more.")] int stock) =>
        api.WriteAsync(HttpMethod.Patch, $"/api/bom/{Segment(setId)}/{setIndex}/bricks/{Segment(partNum)}/{Segment(colorId)}",
            new { Stock = stock }, "Stock updated.", "That part isn't in this set copy.");

    [McpServerTool(Name = "list_substitutions", ReadOnly = true, Idempotent = true)]
    [Description("Lists substitutions on an owned set copy: a different part/color standing in for a required one.")]
    public Task<string> ListSubstitutions(
        [Description("Set number.")] string setId,
        [Description("Which copy.")] int setIndex) =>
        api.GetJsonAsync($"/api/bom/{Segment(setId)}/{setIndex}/substitutions", NoCopy);

    [McpServerTool(Name = "add_substitution")]
    [Description("Records a substitute part filling a requirement on an owned set copy, optionally taking pieces from your loose pile.")]
    public Task<string> AddSubstitution(
        [Description("Set number.")] string setId,
        [Description("Which copy.")] int setIndex,
        [Description("Required part number (from the set's parts list).")] string requiredPartNum,
        [Description("Required color id.")] string requiredColorId,
        [Description("Substitute part number (any catalog part).")] string substitutePartNum,
        [Description("Substitute color id.")] string substituteColorId,
        [Description("How many pieces the substitute covers, 1 or more.")] int count,
        [Description("How many of those to take out of your loose pile (capped by what you have).")] int pullFromLoose = 0,
        [Description("Optional note.")] string? notes = null) =>
        api.WriteAsync(HttpMethod.Post, $"/api/bom/{Segment(setId)}/{setIndex}/substitutions",
            new
            {
                ReqPartNum = requiredPartNum, ReqColorId = requiredColorId,
                SubPartNum = substitutePartNum, SubColorId = substituteColorId,
                Count = count, PulledFromLoose = pullFromLoose, Notes = notes,
            },
            "Substitution recorded.", "Set copy, required part, or substitute part not found.");

    [McpServerTool(Name = "remove_substitution", Destructive = true)]
    [Description("Removes a substitution; any pieces it took from your loose pile go back to it.")]
    public Task<string> RemoveSubstitution(
        [Description("Set number.")] string setId,
        [Description("Which copy.")] int setIndex,
        [Description("Substitution id from list_substitutions.")] int substitutionId) =>
        api.WriteAsync(HttpMethod.Delete, $"/api/bom/{Segment(setId)}/{setIndex}/substitutions/{substitutionId}",
            null, "Substitution removed.", "Substitution not found.");

    private static void Keep(JsonObject obj, string property, Func<JsonNode, bool> predicate)
    {
        if (obj[property] is not JsonArray items) return;
        obj[property] = new JsonArray(items.Where(i => i is not null && predicate(i)).Select(i => i!.DeepClone()).ToArray());
    }
}
