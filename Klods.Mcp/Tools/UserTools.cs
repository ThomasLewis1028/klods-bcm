using System.ComponentModel;
using ModelContextProtocol.Server;
using static Klods.Mcp.KlodsApiClient;

namespace Klods.Mcp.Tools;

/// <summary>Read-only views of other users' collections, e.g. to compare them with your own. Locations and notes are never included.</summary>
[McpServerToolType]
public class UserTools(KlodsApiClient api)
{
    private const string NoUser = "No active user with that id (check list_users).";

    [McpServerTool(Name = "list_users", ReadOnly = true, Idempotent = true)]
    [Description("Lists the site's users with their userId and how many sets, bricks, and minifigs each owns.")]
    public Task<string> ListUsers() => api.GetJsonAsync("/api/users/");

    [McpServerTool(Name = "list_user_sets", ReadOnly = true, Idempotent = true)]
    [Description("Lists the sets another user owns, shaped like list_my_sets (without location or notes).")]
    public Task<string> ListUserSets([Description("User id from list_users.")] int userId) =>
        api.GetJsonAsync($"/api/users/{userId}/sets", NoUser);

    [McpServerTool(Name = "get_user_set_copy", ReadOnly = true, Idempotent = true)]
    [Description("Returns the parts list of one of another user's set copies, shaped like get_set_copy (without location or notes).")]
    public async Task<string> GetUserSetCopy(
        [Description("User id from list_users.")] int userId,
        [Description("Set number, e.g. 75192-1.")] string setId,
        [Description("Which copy (from list_user_sets instances).")] int setIndex,
        [Description("Only return bricks and minifigs this copy is still short of.")] bool onlyMissing = false) =>
        SetTools.OnlyMissing(await api.GetAsync($"/api/users/{userId}/bom/{Segment(setId)}/{setIndex}",
            "That user or set copy doesn't exist (check list_users and list_user_sets)."), onlyMissing);

    [McpServerTool(Name = "list_user_bricks", ReadOnly = true, Idempotent = true)]
    [Description("Lists bricks relevant to another user, shaped like list_my_bricks. Returns { items, total }.")]
    public async Task<string> ListUserBricks(
        [Description("User id from list_users.")] int userId,
        [Description("Filter by part number, name, or color name.")] string? query = null,
        [Description("Only bricks they actually have in their loose pile (stock > 0).")] bool ownedOnly = false,
        [Description("Zero-based page number.")] int page = 0,
        [Description("Page size, 1-200.")] int pageSize = 50) =>
        LooseTools.Page(await api.GetAsync($"/api/users/{userId}/bricks", NoUser), query, ["partNum", "name", "colorName"], page, pageSize,
            ownedOnly ? b => (int?)b["stock"] > 0 : null);

    [McpServerTool(Name = "list_user_minifigs", ReadOnly = true, Idempotent = true)]
    [Description("Lists minifigs relevant to another user, shaped like list_my_minifigs. Returns { items, total }.")]
    public async Task<string> ListUserMinifigs(
        [Description("User id from list_users.")] int userId,
        [Description("Filter by minifig id or name.")] string? query = null,
        [Description("Zero-based page number.")] int page = 0,
        [Description("Page size, 1-200.")] int pageSize = 50) =>
        LooseTools.Page(await api.GetAsync($"/api/users/{userId}/minifigs", NoUser), query, ["minifigId", "minifigName"], page, pageSize);

    [McpServerTool(Name = "list_user_minifig_copies", ReadOnly = true, Idempotent = true)]
    [Description("Lists every copy another user owns of a minifig, shaped like list_minifig_copies.")]
    public Task<string> ListUserMinifigCopies(
        [Description("User id from list_users.")] int userId,
        [Description("Minifig id.")] string minifigId) =>
        api.GetJsonAsync($"/api/users/{userId}/minifigs/{Segment(minifigId)}/instances", NoUser);
}
