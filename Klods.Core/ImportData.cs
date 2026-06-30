using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Klods.Database;
using Klods.Services;
using Microsoft.EntityFrameworkCore;

namespace Klods;

public class ImportData(IDbContextFactory<InventoryContext> contextFactory, ILogger<ImportData> logger, ImageStorageService imageStorage, RebrickableApi rebrickable)
{
    private readonly Dictionary<int, string> _themeCache = new();
    /// <summary>
    /// Imports set catalog info, bricks, and BOM data from Rebrickable.
    /// Does NOT create an owned set — call AddOwnedSet separately.
    /// </summary>
    public async Task<bool> ImportAll(List<string> setIds)
    {
        foreach (string setId in setIds)
        {
            try
            {
                logger.LogInformation("Importing all data for set {SetId}", setId);

                await ImportSetInfo(setId);
                // Fetch the set's parts once and reuse for both the catalog and BOM steps.
                var setParts = await rebrickable.GetSetParts(setId);
                await ImportBricks(setId, setParts);
                await ImportSetBOM(setId, setParts);
                await ImportSetMinifigBOM(setId);

                logger.LogInformation("Finished importing all data for set {SetId}", setId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to import all data for set {SetId}", setId);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Adds an owned set instance for a user. Creates SetOwned + SetBrickOwned rows.
    /// Requires ImportAll to have been called for this set first.
    /// </summary>
    public async Task<bool> AddOwnedSet(string setId, int? userId = null, bool applyBricks = false)
    {
        try
        {
            logger.LogInformation("Adding owned set {SetId} for user {UserId}", setId, userId);

            if (userId == null)
            {
                logger.LogWarning("AddOwnedSet called without a userId for set {SetId} — skipping", setId);
                return false;
            }

            await using var context = contextFactory.CreateDbContext();
            var ownedSetContext = context.Set<SetOwned>();

            var index = await ownedSetContext.CountAsync(so => so.SetId == setId && so.UserId == userId);

            ownedSetContext.Add(new SetOwned
            {
                SetId = setId,
                SetIndex = index,
                UserId = userId.Value
            });

            await context.SaveChangesAsync();

            await CreateSetBrickOwned(userId.Value, setId, index, applyBricks);
            await EnsureBrickOwnedForSet(userId.Value, setId);
            await EnsureMinifigOwnedForSet(userId.Value, setId, index);

            logger.LogInformation("Finished adding owned set {SetId} for user {UserId}", setId, userId);
            return true;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to add owned set {SetId} for user {UserId}", setId, userId);
            return false;
        }
    }

    /// <summary>
    /// Creates SetBrickOwned rows (Stock = 0) for a specific owned set instance,
    /// based on the existing SetBrick BOM entries for that set.
    /// </summary>
    public async Task<bool> CreateSetBrickOwned(int userId, string setId, int setIndex, bool applyBricks = false)
    {
        logger.LogInformation("Creating SetBrickOwned for user {UserId}, {SetId}-{SetIndex}", userId, setId, setIndex);

        await using var context = contextFactory.CreateDbContext();
        var setBrickContext = context.Set<SetBrick>();
        var setBrickOwnedContext = context.Set<SetBrickOwned>();

        var bomEntries = await setBrickContext.Where(sb => sb.SetId == setId).ToListAsync();

        var existingKeys = (await setBrickOwnedContext
            .Where(sbo => sbo.UserId == userId && sbo.SetId == setId && sbo.SetIndex == setIndex)
            .Select(sbo => new { sbo.PartNum, sbo.ColorId })
            .ToListAsync())
            .Select(k => (k.PartNum, k.ColorId))
            .ToHashSet();

        foreach (var bom in bomEntries)
        {
            if (!existingKeys.Contains((bom.PartNum, bom.ColorId)))
            {
                setBrickOwnedContext.Add(new SetBrickOwned
                {
                    UserId = userId,
                    SetId = setId,
                    SetIndex = setIndex,
                    PartNum = bom.PartNum,
                    ColorId = bom.ColorId,
                    Stock = applyBricks ? bom.Count : 0
                });
            }
        }

        return await context.SaveChangesAsync() > 0;
    }

    /// <summary>
    /// Ensures a BrickOwned(Stock=0) row exists for every brick in a set's BOM for the given user.
    /// Called when a user adds a set so My Bricks shows all relevant bricks immediately.
    /// </summary>
    public async Task EnsureBrickOwnedForSet(int userId, string setId)
    {
        await using var context = contextFactory.CreateDbContext();

        var bomPartKeys = await context.Set<SetBrick>()
            .Where(sb => sb.SetId == setId)
            .Select(sb => new { sb.PartNum, sb.ColorId })
            .ToListAsync();

        var bomPartNums = bomPartKeys.Select(k => k.PartNum).ToList();

        var existingKeys = (await context.Set<BrickOwned>()
            .Where(bo => bo.UserId == userId && bomPartNums.Contains(bo.PartNum))
            .Select(bo => new { bo.PartNum, bo.ColorId })
            .ToListAsync())
            .Select(k => (k.PartNum, k.ColorId))
            .ToHashSet();

        foreach (var key in bomPartKeys)
        {
            if (!existingKeys.Contains((key.PartNum, key.ColorId)))
            {
                context.Set<BrickOwned>().Add(new BrickOwned
                {
                    UserId = userId,
                    PartNum = key.PartNum,
                    ColorId = key.ColorId,
                    Stock = 0
                });
            }
        }

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Creates MinifigOwned instances for an owned set copy: one per fig per SetMinifig.Count,
    /// each linked to (setId, setIndex). Tops up to the required count if some already exist,
    /// so it's safe to call repeatedly for the same copy.
    /// </summary>
    public async Task EnsureMinifigOwnedForSet(int userId, string setId, int setIndex)
    {
        await using var context = contextFactory.CreateDbContext();
        var ownedContext = context.Set<MinifigOwned>();

        var bom = await context.Set<SetMinifig>()
            .Where(sm => sm.SetId == setId)
            .Select(sm => new { sm.MinifigId, sm.Count })
            .ToListAsync();

        foreach (var fig in bom)
        {
            var alreadyOnCopy = await ownedContext.CountAsync(mo =>
                mo.UserId == userId && mo.MinifigId == fig.MinifigId &&
                mo.SetId == setId && mo.SetIndex == setIndex);

            var nextIndex = await NextMinifigIndex(ownedContext, userId, fig.MinifigId);

            for (var i = alreadyOnCopy; i < fig.Count; i++)
            {
                ownedContext.Add(new MinifigOwned
                {
                    UserId = userId,
                    MinifigId = fig.MinifigId,
                    MinifigIndex = nextIndex++,
                    SetId = setId,
                    SetIndex = setIndex,
                });
            }
        }

        await context.SaveChangesAsync();
    }

    /// <summary>Next free per-(user, fig) instance index. MAX(index)+1 — never reused, gaps are fine.</summary>
    private static async Task<int> NextMinifigIndex(DbSet<MinifigOwned> ownedContext, int userId, string minifigId)
    {
        var max = await ownedContext
            .Where(mo => mo.UserId == userId && mo.MinifigId == minifigId)
            .Select(mo => (int?)mo.MinifigIndex)
            .MaxAsync();
        return (max ?? -1) + 1;
    }

    /// <summary>
    /// Ensures the user owns exactly <paramref name="target"/> loose (unattached) instances of a fig,
    /// adding new loose instances or removing the highest-indexed loose ones. Leaves set-attached instances alone.
    /// </summary>
    public async Task SetLooseMinifigCount(int userId, string minifigId, int target) =>
        await SetMinifigInstanceCount(userId, minifigId, setId: null, setIndex: null, target);

    /// <summary>
    /// Ensures an owned set copy holds exactly <paramref name="target"/> instances of a fig
    /// (adding/removing instances linked to that copy). Used by the BOM "present on this copy" control.
    /// </summary>
    public async Task SetSetCopyMinifigCount(int userId, string setId, int setIndex, string minifigId, int target) =>
        await SetMinifigInstanceCount(userId, minifigId, setId, setIndex, target);

    private async Task SetMinifigInstanceCount(int userId, string minifigId, string? setId, int? setIndex, int target)
    {
        if (target < 0) target = 0;

        await using var context = contextFactory.CreateDbContext();
        var owned = context.Set<MinifigOwned>();

        var matching = await owned
            .Where(mo => mo.UserId == userId && mo.MinifigId == minifigId && mo.SetId == setId && mo.SetIndex == setIndex)
            .OrderBy(mo => mo.MinifigIndex)
            .ToListAsync();

        if (matching.Count < target)
        {
            var next = await NextMinifigIndex(owned, userId, minifigId);
            for (var i = matching.Count; i < target; i++)
                owned.Add(new MinifigOwned
                {
                    UserId = userId,
                    MinifigId = minifigId,
                    MinifigIndex = next++,
                    SetId = setId,
                    SetIndex = setIndex,
                });
        }
        else if (matching.Count > target)
        {
            // Remove the highest-indexed instances; MinifigBrickOwned children cascade at the DB.
            owned.RemoveRange(matching.Skip(target));
        }

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Sets the owned stock of a single part for a fig on a set copy, tracked against that copy's
    /// lowest-indexed instance of the fig (creating one if the copy currently has none).
    /// </summary>
    public Task SetMinifigBrickOwnedStock(
        int userId, string setId, int setIndex, string minifigId, string partNum, string colorId, int stock)
        => SetMinifigBrickOwnedStockCore(userId, setId, setIndex, minifigId, partNum, colorId, stock);

    /// <summary>
    /// Sets the owned stock of a single part for a <em>loose</em> (unattached) fig, tracked against the
    /// user's lowest-indexed loose instance of the fig (creating one if none exists). Powers loose-fig part tracking.
    /// </summary>
    public Task SetLooseMinifigBrickOwnedStock(
        int userId, string minifigId, string partNum, string colorId, int stock)
        => SetMinifigBrickOwnedStockCore(userId, null, null, minifigId, partNum, colorId, stock);

    // Per-instance equivalent of SetBrickOwned stock; multi-instance distribution is a future refinement.
    private async Task SetMinifigBrickOwnedStockCore(
        int userId, string? setId, int? setIndex, string minifigId, string partNum, string colorId, int stock)
    {
        if (stock < 0) stock = 0;

        await using var context = contextFactory.CreateDbContext();
        var owned = context.Set<MinifigOwned>();

        var instanceIndex = await owned
            .Where(mo => mo.UserId == userId && mo.MinifigId == minifigId && mo.SetId == setId && mo.SetIndex == setIndex)
            .OrderBy(mo => mo.MinifigIndex)
            .Select(mo => (int?)mo.MinifigIndex)
            .FirstOrDefaultAsync();

        if (instanceIndex == null)
        {
            instanceIndex = await NextMinifigIndex(owned, userId, minifigId);
            owned.Add(new MinifigOwned
            {
                UserId = userId, MinifigId = minifigId, MinifigIndex = instanceIndex.Value,
                SetId = setId, SetIndex = setIndex,
            });
            await context.SaveChangesAsync();
        }

        var brickOwned = context.Set<MinifigBrickOwned>();
        var existing = await brickOwned.FirstOrDefaultAsync(x =>
            x.UserId == userId && x.MinifigId == minifigId && x.MinifigIndex == instanceIndex.Value &&
            x.PartNum == partNum && x.ColorId == colorId);

        if (existing == null)
            brickOwned.Add(new MinifigBrickOwned
            {
                UserId = userId, MinifigId = minifigId, MinifigIndex = instanceIndex.Value,
                PartNum = partNum, ColorId = colorId, Stock = stock,
            });
        else
            existing.Stock = stock;

        await context.SaveChangesAsync();
    }

    public async Task<bool> ImportSetInfo(string? setId)
    {
        logger.LogInformation("Importing set info for {SetId}", setId);
        var api = rebrickable;

        var setInfo = await api.GetSetInfo(setId);

        await using var context = contextFactory.CreateDbContext();
        var setContext = context.Set<Set>();

        // Lazy images: keep the source URL; it's materialized into MinIO on first view via /img.
        var setImg = setInfo!["set_img_url"]?.ToString();

        int? themeId = setInfo!["theme_id"] != null ? (int)setInfo["theme_id"]! : null;
        var themeName = themeId.HasValue ? await ResolveThemeNameAsync(api, themeId.Value) : null;

        var existingSet = await setContext.FirstOrDefaultAsync(s => s.SetId == setId);
        if (existingSet != null)
        {
            var apiDate = DateTime.Parse(setInfo!["last_modified_dt"]!.ToString()).ToUniversalTime();
            if (apiDate > existingSet.DateModified)
            {
                existingSet.Name = setInfo!["name"]!.ToString();
                existingSet.SetImg = setImg;
                existingSet.SetURL = setInfo!["set_url"]!.ToString();
                existingSet.DateModified = apiDate;
                existingSet.NumBricks = int.Parse(setInfo!["num_parts"]!.ToString());
                existingSet.ReleaseYear = int.Parse(setInfo!["year"]!.ToString());
                existingSet.ManualUrl = $"https://www.lego.com/en-us/service/buildinginstructions/{setId!.Split('-').First()}";
                existingSet.ThemeId = themeId;
                existingSet.ThemeName = themeName;
            }
        }
        else
        {
            setContext.Add(new Set
            {
                SetId = setInfo!["set_num"]!.ToString(),
                Name = setInfo!["name"]!.ToString(),
                SetURL = setInfo["set_url"]?.ToString(),
                SetImg = setImg,
                DateModified = DateTime.Parse(setInfo!["last_modified_dt"]!.ToString()).ToUniversalTime(),
                NumBricks = int.Parse(setInfo!["num_parts"]!.ToString()),
                ReleaseYear = int.Parse(setInfo!["year"]!.ToString()),
                ManualUrl = $"https://www.lego.com/en-us/service/buildinginstructions/{setId!.Split('-').First()}",
                ThemeId = themeId,
                ThemeName = themeName,
            });
        }

        logger.LogInformation("Finished importing set info for {SetId}", setId);
        return await context.SaveChangesAsync() > 0;
    }

    /// <summary>
    /// Creates/updates SetBrick BOM entries for a set (no SetIndex — BOM is per-set).
    /// </summary>
    public async Task<bool> ImportSetBOM(string setId, JsonArray? setParts = null)
    {
        logger.LogInformation("Importing SetBrick BOM for {SetId}", setId);

        setParts ??= await rebrickable.GetSetParts(setId);

        await using var context = contextFactory.CreateDbContext();

        if (!await context.Set<Set>().AnyAsync(s => s.SetId == setId))
            throw new Exception($"No set found with ID {setId} in database");

        var brickContext = context.Set<Brick>();
        var setBrickContext = context.Set<SetBrick>();

        foreach (var part in setParts!)
        {
            var brick = await brickContext.FirstOrDefaultAsync(b =>
                b.PartNum == part!["part"]!["part_num"]!.ToString() &&
                b.ColorId == part["color"]!["id"]!.ToString());

            if (brick == null)
                throw new Exception($"No brick found with ID {part!["part"]!["part_num"]}");

            var isSpare = part!["is_spare"]!.ToString().Equals("true");
            var quantity = int.Parse(part!["quantity"].ToString());

            var existing = await setBrickContext.FirstOrDefaultAsync(sb =>
                sb.SetId == setId && sb.PartNum == brick.PartNum && sb.ColorId == brick.ColorId);

            if (existing == null)
            {
                setBrickContext.Add(new SetBrick
                {
                    SetId = setId,
                    PartNum = brick.PartNum,
                    ColorId = brick.ColorId,
                    Count = isSpare ? 0 : quantity,
                    SpareCount = isSpare ? quantity : 0,
                });
            }
            else
            {
                if (isSpare)
                    existing.SpareCount = quantity;
                else
                    existing.Count = quantity;
            }
        }

        var saved = await context.SaveChangesAsync();
        logger.LogInformation("Finished importing SetBrick BOM for {SetId}", setId);
        return saved > 0;
    }

    /// <summary>
    /// Creates/updates SetMinifig BOM entries and merges minifig brick parts into SetBrick BOM.
    /// </summary>
    public async Task ImportSetMinifigBOM(string setId)
    {
        logger.LogInformation("Importing SetMinifig BOM for {SetId}", setId);
        var api = rebrickable;

        var minifigs = await api.GetSetMinifigs(setId);

        foreach (var minifig in minifigs!)
        {
            var minifigId = minifig!["set_num"]!.ToString();
            var quantity = (int)minifig!["quantity"]!;

            await ImportMinifig(minifigId);
            await LinkMinifigBricks(minifigId);
            await LinkMinifigToSetBOM(minifigId, setId, quantity);
        }
    }

    public async Task<bool> ImportMinifig(string minifigId)
    {
        logger.LogInformation("Importing minifig {MinifigId}", minifigId);

        await using var context = contextFactory.CreateDbContext();
        var minifigContext = context.Set<Minifig>();

        var info = await rebrickable.GetMinifigInfo(minifigId);
        if (info == null)
            return false;

        var apiDate = info["last_modified_dt"] != null
            ? DateTime.Parse(info["last_modified_dt"]!.ToString()).ToUniversalTime()
            : DateTime.UnixEpoch;

        var existing = await minifigContext.FirstOrDefaultAsync(m => m.MinifigId == minifigId);
        if (existing != null && existing.DateModified >= apiDate)
            return false;

        var minifigImg = info["set_img_url"]?.ToString();

        var numParts = info["num_parts"] != null ? int.Parse(info["num_parts"]!.ToString()) : 0;

        if (existing != null)
        {
            existing.Name = info["name"]!.ToString();
            existing.ImgUrl = minifigImg;
            existing.Url = info["set_url"]?.ToString();
            existing.NumParts = numParts;
            existing.DateModified = apiDate;
        }
        else
        {
            minifigContext.Add(new Minifig
            {
                MinifigId = minifigId,
                Name = info["name"]!.ToString(),
                ImgUrl = minifigImg,
                Url = info["set_url"]?.ToString(),
                NumParts = numParts,
                DateModified = apiDate,
            });
        }

        logger.LogInformation("Imported minifig {MinifigId}", minifigId);
        return await context.SaveChangesAsync() > 0;
    }

    /// <summary>
    /// Creates/updates the SetMinifig BOM entry (set → minifig + count).
    /// Minifig parts are NOT merged into the set's SetBrick BOM — they live on the minifig
    /// (MinifigBrick) and are composed into a set's full part list at read time.
    /// </summary>
    public async Task<bool> LinkMinifigToSetBOM(string minifigId, string setId, int quantity)
    {
        logger.LogInformation("Linking minifig {MinifigId} to set {SetId} BOM", minifigId, setId);

        await using var context = contextFactory.CreateDbContext();
        var setMinifigContext = context.Set<SetMinifig>();

        var existing = await setMinifigContext.FirstOrDefaultAsync(sm => sm.MinifigId == minifigId && sm.SetId == setId);
        if (existing == null)
            setMinifigContext.Add(new SetMinifig { MinifigId = minifigId, SetId = setId, Count = quantity });
        else
            existing.Count = quantity;

        return await context.SaveChangesAsync() > 0;
    }

    public async Task<bool> LinkMinifigBricks(string minifigId)
    {
        logger.LogInformation("Linking minifig bricks for {MinifigId}", minifigId);

        await using var context = contextFactory.CreateDbContext();
        var minifigBrickContext = context.Set<MinifigBrick>();
        var brickContext = context.Set<Brick>();

        var parts = await rebrickable.GetMinifigParts(minifigId);

        // Aggregate API rows by (part, color): Rebrickable can return the same part twice
        // (regular + spare), which would otherwise collide on the (MinifigId, PartNum, ColorId) key.
        var aggregated = new Dictionary<(string PartNum, string ColorId), (int Count, int SpareCount)>();

        foreach (var part in parts!)
        {
            var partNum = part["part"]!["part_num"]!.ToString();
            var colorId = part["color"]!["id"]!.ToString();
            var quantity = (int)part["quantity"]!;
            var isSpare = part["is_spare"]!.ToString().Equals("true", StringComparison.OrdinalIgnoreCase);

            if (!await brickContext.AnyAsync(b => b.PartNum == partNum && b.ColorId == colorId))
                await ImportBrickAsync(part);

            aggregated.TryGetValue((partNum, colorId), out var acc);
            aggregated[(partNum, colorId)] = isSpare
                ? (acc.Count, acc.SpareCount + quantity)
                : (acc.Count + quantity, acc.SpareCount);
        }

        foreach (var ((partNum, colorId), (count, spareCount)) in aggregated)
        {
            var existing = await minifigBrickContext.FirstOrDefaultAsync(mb =>
                mb.MinifigId == minifigId && mb.PartNum == partNum && mb.ColorId == colorId);

            if (existing == null)
            {
                minifigBrickContext.Add(new MinifigBrick
                {
                    MinifigId = minifigId,
                    PartNum = partNum,
                    ColorId = colorId,
                    Count = count,
                    SpareCount = spareCount,
                });
            }
            else
            {
                existing.Count = count;
                existing.SpareCount = spareCount;
            }
        }

        return await context.SaveChangesAsync() > 0;
    }

    public async Task<bool> ImportBricks(string setId, JsonArray? setParts = null)
    {
        logger.LogInformation("Importing bricks for {SetId}", setId);

        try
        {
            setParts ??= await rebrickable.GetSetParts(setId);

            await using var context = contextFactory.CreateDbContext();
            var brickContext = context.Set<Brick>();

            foreach (var part in setParts!)
            {
                if (!await brickContext.AnyAsync(b =>
                        b.PartNum == part!["part"]!["part_num"]!.ToString() &&
                        b.ColorId == part!["color"]!["id"]!.ToString()))
                {
                    await ImportBrickAsync(part);
                }
            }

            logger.LogInformation("Finished importing bricks for {SetId}", setId);
            return true;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to import bricks for {SetId}", setId);
            return false;
        }
    }

    public async Task<Brick> ImportBrickAsync(JsonNode part)
    {
        logger.LogInformation("Importing brick {PartNum}", part!["part"]!["part_num"]);

        await using var context = contextFactory.CreateDbContext();
        var brickContext = context.Set<Brick>();

        var partNum = part!["part"]!["part_num"]!.ToString();
        var colorId = part["color"]?["id"]?.ToString();

        var existing = await brickContext.FirstOrDefaultAsync(b => b.PartNum == partNum && b.ColorId == colorId);
        if (existing != null)
            return existing;

        var partImg = part["part"]?["part_img_url"]?.ToString();

        var bricklinkIds = part["part"]?["external_ids"]?["BrickLink"]?.AsArray();
        var brickOwlIds = part["part"]?["external_ids"]?["BrickOwl"]?.AsArray();
        var partCatId = part["part"]?["part_cat_id"];

        var brick = new Brick
        {
            PartNum = partNum,
            Name = part!["part"]!["name"]!.ToString(),
            PartURL = part["part"]?["part_url"]?.ToString(),
            PartImg = partImg,
            ColorId = colorId,
            ColorName = part["color"]?["name"]?.ToString(),
            IsTrans = part!["color"]!["is_trans"]!.ToString().Equals("true"),
            HexColor = part["color"]?["rgb"]?.ToString(),
            BricklinkId = bricklinkIds?.Count > 0 ? bricklinkIds[0]?.ToString() : null,
            BrickOwlId = brickOwlIds?.Count > 0 ? brickOwlIds[0]?.ToString() : null,
            ElementId = part["element_id"]?.ToString(),
            PartCatId = partCatId != null ? (int)partCatId : null,
        };

        brickContext.Add(brick);
        await context.SaveChangesAsync();
        return brick;
    }

    /// <summary>
    /// Resolves a raw set ID input (e.g. "4502" or "75192-1") to one or more Rebrickable set candidates.
    /// Returns a single resolved candidate, a list of variants to choose from, or a not-found result.
    /// No DB access — purely API resolution.
    /// </summary>
    public async Task<(SetCandidate? Resolved, List<SetCandidate> Candidates, bool NotFound, bool HasMore)>
        ResolveSetId(string input, int page = 1)
    {
        var api = rebrickable;
        var trimmed = input.Trim();

        // On the first page only, try an exact match before falling back to search.
        // A full variant ID like "75192-1" resolves instantly this way.
        if (page == 1)
        {
            try
            {
                var setInfo = await api.GetSetInfo(trimmed);
                if (setInfo != null)
                    return (ToSetCandidate(setInfo), [], false, false);
            }
            catch { /* 404 or API error — fall through to search */ }
        }

        // Extract the base number: "4502-1" → "4502", "4502" → "4502"
        var baseNum = trimmed.Contains('-') ? trimmed.Split('-')[0] : trimmed;
        var pattern = new Regex($@"^{Regex.Escape(baseNum)}-\d+$");

        var searchResult = await api.SearchSets(baseNum, page);
        if (searchResult == null)
            return (null, [], true, false);

        var candidates = searchResult["results"]!.AsArray()
            .Where(r => r != null && pattern.IsMatch(r!["set_num"]?.ToString() ?? ""))
            .Select(r => ToSetCandidate(r!))
            .ToList();

        var hasMore = searchResult["next"] != null;

        if (candidates.Count == 0 && !hasMore)
            return (null, [], true, false);

        if (candidates.Count == 1 && !hasMore)
            return (candidates[0], [], false, false);

        return (null, candidates, false, hasMore);
    }

    private static SetCandidate ToSetCandidate(JsonNode node) => new(
        node["set_num"]!.ToString(),
        node["name"]!.ToString(),
        node["year"] != null ? int.Parse(node["year"]!.ToString()) : 0,
        node["set_img_url"]?.ToString()
    );

    private async Task<string?> ResolveThemeNameAsync(RebrickableApi api, int themeId)
    {
        if (_themeCache.TryGetValue(themeId, out var cached))
            return cached;
        try
        {
            var theme = await api.GetTheme(themeId);
            var name = theme?["name"]?.ToString();
            if (name != null)
                _themeCache[themeId] = name;
            return name;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Syncs the Colors reference table from Rebrickable. Safe to call repeatedly — upserts.
    /// </summary>
    public async Task<bool> ImportColors()
    {
        logger.LogInformation("Importing colors from Rebrickable");
        var api = rebrickable;
        var colors = await api.GetColors();
        if (colors == null)
            return false;

        await using var context = contextFactory.CreateDbContext();
        var colorContext = context.Set<Color>();

        foreach (var color in colors)
        {
            if (color == null) continue;
            var id = color["id"]!.ToString();
            var existing = await colorContext.FirstOrDefaultAsync(c => c.Id == id);
            if (existing == null)
            {
                colorContext.Add(new Color
                {
                    Id = id,
                    Name = color["name"]!.ToString(),
                    Hex = color["rgb"]!.ToString(),
                    IsTrans = color["is_trans"]!.GetValue<bool>(),
                });
            }
            else
            {
                existing.Name = color["name"]!.ToString();
                existing.Hex = color["rgb"]!.ToString();
                existing.IsTrans = color["is_trans"]!.GetValue<bool>();
            }
        }

        var saved = await context.SaveChangesAsync();
        logger.LogInformation("Imported {Count} colors", colors.Count);
        return saved > 0;
    }

    /// <summary>
    /// Syncs the PartCategories reference table from Rebrickable. Safe to call repeatedly — upserts.
    /// </summary>
    public async Task<bool> ImportPartCategories()
    {
        logger.LogInformation("Importing part categories from Rebrickable");
        var categories = await rebrickable.GetPartCategories();
        if (categories == null)
            return false;

        await using var context = contextFactory.CreateDbContext();
        var catContext = context.Set<PartCategory>();

        foreach (var cat in categories)
        {
            if (cat == null) continue;
            var id = (int)cat["id"]!;
            var existing = await catContext.FirstOrDefaultAsync(c => c.Id == id);
            if (existing == null)
                catContext.Add(new PartCategory { Id = id, Name = cat["name"]!.ToString() });
            else
                existing.Name = cat["name"]!.ToString();
        }

        var saved = await context.SaveChangesAsync();
        logger.LogInformation("Imported {Count} part categories", categories.Count);
        return saved > 0;
    }

    /// <summary>
    /// Resolves a minifig ID or search term to one or more Rebrickable minifig candidates.
    /// Tries exact match first (if input looks like "fig-XXXXXX"), then falls back to search.
    /// </summary>
    public async Task<(MinifigCandidate? Resolved, List<MinifigCandidate> Candidates, bool NotFound, bool HasMore)>
        ResolveMinifigId(string input, int page = 1)
    {
        var api = rebrickable;
        var trimmed = input.Trim();

        if (page == 1)
        {
            try
            {
                var info = await api.GetMinifigInfo(trimmed);
                if (info != null)
                    return (ToMinifigCandidate(info), [], false, false);
            }
            catch { /* not an exact ID — fall through to search */ }
        }

        var searchResult = await api.SearchMinifigs(trimmed, page);
        if (searchResult == null)
            return (null, [], true, false);

        var candidates = searchResult["results"]!.AsArray()
            .Where(r => r != null)
            .Select(r => ToMinifigCandidate(r!))
            .ToList();

        var hasMore = searchResult["next"] != null;

        if (candidates.Count == 0 && !hasMore) return (null, [], true, false);
        if (candidates.Count == 1 && !hasMore) return (candidates[0], [], false, false);
        return (null, candidates, false, hasMore);
    }

    private static MinifigCandidate ToMinifigCandidate(JsonNode node) => new(
        node["set_num"]!.ToString(),
        node["name"]!.ToString(),
        node["num_parts"] != null ? int.Parse(node["num_parts"]!.ToString()) : 0,
        node["set_img_url"]?.ToString()
    );

    /// <summary>
    /// Imports a minifig from Rebrickable (if needed) and creates <paramref name="count"/> loose
    /// MinifigOwned instances (no set link) for the user — each a distinct, indexed physical copy.
    /// </summary>
    public async Task<bool> AddOwnedMinifig(string minifigId, int? userId, int count = 1, bool applyParts = false)
    {
        if (userId == null)
        {
            logger.LogWarning("AddOwnedMinifig called without userId for {MinifigId}", minifigId);
            return false;
        }

        logger.LogInformation("Adding {Count} loose minifig(s) {MinifigId} for user {UserId}", count, minifigId, userId);

        await ImportMinifig(minifigId);
        await LinkMinifigBricks(minifigId);

        await using var context = contextFactory.CreateDbContext();
        var ownedContext = context.Set<MinifigOwned>();

        var nextIndex = await NextMinifigIndex(ownedContext, userId.Value, minifigId);
        var firstIndex = nextIndex;
        for (var i = 0; i < count; i++)
        {
            ownedContext.Add(new MinifigOwned
            {
                UserId = userId.Value,
                MinifigId = minifigId,
                MinifigIndex = nextIndex++,
                SetId = null,
                SetIndex = null,
            });
        }

        if (applyParts)
        {
            var minifigBricks = await context.Set<MinifigBrick>()
                .Where(mb => mb.MinifigId == minifigId).ToListAsync();
            var brickOwnedCtx = context.Set<MinifigBrickOwned>();
            foreach (var mb in minifigBricks)
            {
                brickOwnedCtx.Add(new MinifigBrickOwned
                {
                    UserId = userId.Value,
                    MinifigId = minifigId,
                    MinifigIndex = firstIndex,
                    PartNum = mb.PartNum,
                    ColorId = mb.ColorId,
                    Stock = mb.Count,
                });
            }
        }

        await context.SaveChangesAsync();

        logger.LogInformation("Finished adding owned minifig {MinifigId} for user {UserId}", minifigId, userId);
        return true;
    }

    /// <summary>
    /// Looks up a part number on Rebrickable and returns its name + all available colors.
    /// </summary>
    public async Task<(string? PartName, List<PartColorInfo> Colors, bool NotFound)>
        ResolvePartColors(string partNum)
    {
        var api = rebrickable;
        var trimmed = partNum.Trim();

        JsonObject? partInfo;
        try
        {
            partInfo = await api.GetPartInfo(trimmed);
            if (partInfo == null) return (null, [], true);
        }
        catch { return (null, [], true); }

        var partName = partInfo["name"]?.ToString();
        var colorResults = await api.GetPartColors(trimmed);
        if (colorResults == null) return (partName, [], false);

        var colors = colorResults
            .Where(c => c != null)
            .Select(c => new PartColorInfo(
                c!["color_id"]!.ToString(),
                c["color_name"]!.ToString(),
                c["elements"]?.AsArray().FirstOrDefault()?["part_img_url"]?.ToString()
            ))
            .ToList();

        return (partName, colors, false);
    }

    /// <summary>
    /// Ensures a Brick catalog record exists for the given part+color, then adds the specified
    /// quantity to the user's BrickOwned stock (creates the row if absent, otherwise increments).
    /// </summary>
    public async Task AddLooseBrick(string partNum, string partName, PartColorInfo colorInfo, int quantity, int userId)
    {
        logger.LogInformation("Adding loose brick {PartNum}/{ColorId} for user {UserId}", partNum, colorInfo.ColorId, userId);

        await using var context = contextFactory.CreateDbContext();
        var brickContext = context.Set<Brick>();

        if (!await brickContext.AnyAsync(b => b.PartNum == partNum && b.ColorId == colorInfo.ColorId))
        {
            var colorRow = await context.Set<Color>().FirstOrDefaultAsync(c => c.Id == colorInfo.ColorId);
            var partImg = colorInfo.PartImgUrl;

            brickContext.Add(new Brick
            {
                PartNum = partNum,
                Name = partName,
                ColorId = colorInfo.ColorId,
                ColorName = colorInfo.ColorName,
                HexColor = colorRow?.Hex,
                IsTrans = colorRow?.IsTrans ?? false,
                PartImg = partImg,
            });
            await context.SaveChangesAsync();
        }

        var brickOwnedContext = context.Set<BrickOwned>();
        var existing = await brickOwnedContext
            .FirstOrDefaultAsync(bo => bo.UserId == userId && bo.PartNum == partNum && bo.ColorId == colorInfo.ColorId);

        if (existing == null)
            brickOwnedContext.Add(new BrickOwned { UserId = userId, PartNum = partNum, ColorId = colorInfo.ColorId, Stock = quantity });
        else
            existing.Stock += quantity;

        await context.SaveChangesAsync();
    }
}
