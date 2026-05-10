using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LEGO_Inventory.Database;
using LEGO_Inventory.Services;
using Microsoft.EntityFrameworkCore;

namespace LEGO_Inventory;

public class ImportData(IDbContextFactory<InventoryContext> contextFactory, ILogger<ImportData> logger, ImageStorageService imageStorage)
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
                await ImportBricks(setId);
                await ImportSetBOM(setId);
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
            await EnsureMinifigOwnedForSet(userId.Value, setId);

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
    /// Ensures a MinifigOwned(Stock=0) row exists for every minifig in a set's BOM for the given user.
    /// Called when a user adds a set so the BOM minifig tab is immediately editable.
    /// </summary>
    public async Task EnsureMinifigOwnedForSet(int userId, string setId)
    {
        await using var context = contextFactory.CreateDbContext();

        var bomMinifigIds = await context.Set<SetMinifig>()
            .Where(sm => sm.SetId == setId)
            .Select(sm => sm.MinifigId)
            .ToListAsync();

        var existingIds = (await context.Set<MinifigOwned>()
            .Where(mo => mo.UserId == userId && bomMinifigIds.Contains(mo.MinifigId))
            .Select(mo => mo.MinifigId)
            .ToListAsync())
            .ToHashSet();

        foreach (var minifigId in bomMinifigIds)
        {
            if (!existingIds.Contains(minifigId))
            {
                context.Set<MinifigOwned>().Add(new MinifigOwned
                {
                    UserId = userId,
                    MinifigId = minifigId,
                    Stock = 0
                });
            }
        }

        await context.SaveChangesAsync();
    }

    public async Task<bool> ImportSetInfo(string? setId)
    {
        logger.LogInformation("Importing set info for {SetId}", setId);
        var api = new RebrickableApi();

        var setInfo = await api.GetSetInfo(setId);

        await using var context = contextFactory.CreateDbContext();
        var setContext = context.Set<Set>();

        var setImg = await imageStorage.StoreImageAsync(
            setInfo!["set_img_url"]?.ToString(),
            $"sets/{setId}.jpg");

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
    public async Task<bool> ImportSetBOM(string setId)
    {
        logger.LogInformation("Importing SetBrick BOM for {SetId}", setId);
        var api = new RebrickableApi();

        var setParts = await api.GetSetParts(setId);

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
        var api = new RebrickableApi();

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
        var api = new RebrickableApi();

        await using var context = contextFactory.CreateDbContext();
        var minifigContext = context.Set<Minifig>();

        if (await minifigContext.AnyAsync(m => m.MinifigId == minifigId))
            return false;

        var minifigJsonObject = await api.GetMinifigInfo(minifigId);

        var minifigImg = await imageStorage.StoreImageAsync(
            minifigJsonObject!["set_img_url"]?.ToString(),
            $"minifigs/{minifigId}.jpg");

        var minifig = new Minifig
        {
            MinifigId = minifigId,
            MinifigName = minifigJsonObject!["name"]?.ToString(),
            MinifigImgUrl = minifigImg,
            MinifigUrl = minifigJsonObject["set_url"]?.ToString(),
        };

        minifigContext.Add(minifig);
        logger.LogInformation("Imported minifig ({MinifigId}) {MinifigName}", minifigId, minifig.MinifigName);

        return await context.SaveChangesAsync() > 0;
    }

    /// <summary>
    /// Creates/updates a SetMinifig BOM entry and merges minifig bricks into SetBrick BOM.
    /// </summary>
    public async Task<bool> LinkMinifigToSetBOM(string minifigId, string setId, int quantity)
    {
        logger.LogInformation("Linking minifig {MinifigId} to set {SetId} BOM", minifigId, setId);

        await using var context = contextFactory.CreateDbContext();
        var setMinifigContext = context.Set<SetMinifig>();
        var setBrickContext = context.Set<SetBrick>();
        var minifigBrickContext = context.Set<MinifigBrick>();

        var existing = await setMinifigContext.FirstOrDefaultAsync(sm => sm.MinifigId == minifigId && sm.SetId == setId);
        if (existing == null)
        {
            setMinifigContext.Add(new SetMinifig
            {
                MinifigId = minifigId,
                SetId = setId,
                Count = quantity,
            });
        }
        else
        {
            existing.Count = quantity;
        }

        var minifigBricks = await minifigBrickContext.Where(mb => mb.MinifigID == minifigId).ToListAsync();

        foreach (var minifigBrick in minifigBricks)
        {
            var existingBrick = await setBrickContext.FirstOrDefaultAsync(sb =>
                sb.PartNum == minifigBrick.BrickID &&
                sb.ColorId == minifigBrick.ColorId &&
                sb.SetId == setId);

            if (existingBrick != null)
            {
                existingBrick.Count += quantity * minifigBrick.Quantity;
            }
            else
            {
                setBrickContext.Add(new SetBrick
                {
                    SetId = setId,
                    PartNum = minifigBrick.BrickID,
                    ColorId = minifigBrick.ColorId,
                    Count = quantity * minifigBrick.Quantity,
                    SpareCount = 0,
                });
            }
        }

        return await context.SaveChangesAsync() > 0;
    }

    public async Task<bool> LinkMinifigBricks(string minifigId)
    {
        logger.LogInformation("Linking minifig bricks for {MinifigId}", minifigId);
        var api = new RebrickableApi();

        await using var context = contextFactory.CreateDbContext();
        var minifigBrickContext = context.Set<MinifigBrick>();
        var brickContext = context.Set<Brick>();

        var parts = await api.GetMinifigParts(minifigId);

        foreach (var brick in parts!)
        {
            var brickId = brick["part"]!["part_num"]?.ToString();
            var colorId = brick["color"]!["id"]?.ToString();
            var quantity = (int)brick["quantity"]!;

            if (await minifigBrickContext.AnyAsync(mb => mb.MinifigID == minifigId && mb.BrickID == brickId && mb.ColorId == colorId))
                continue;

            MinifigBrick minifigBrick;

            if (await brickContext.AnyAsync(b => b.PartNum == brickId && b.ColorId == colorId))
            {
                minifigBrick = new MinifigBrick
                {
                    MinifigID = minifigId,
                    BrickID = brickId,
                    ColorId = colorId,
                    Quantity = quantity,
                };
            }
            else
            {
                var newBrick = await ImportBrickAsync(brick);
                minifigBrick = new MinifigBrick
                {
                    MinifigID = minifigId,
                    BrickID = newBrick.PartNum,
                    ColorId = newBrick.ColorId,
                    Quantity = quantity,
                };
            }

            minifigBrickContext.Add(minifigBrick);
        }

        return await context.SaveChangesAsync() > 0;
    }

    public async Task<bool> ImportBricks(string setId)
    {
        logger.LogInformation("Importing bricks for {SetId}", setId);

        try
        {
            var api = new RebrickableApi();
            var setParts = await api.GetSetParts(setId);

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

        var partImg = await imageStorage.StoreImageAsync(
            part["part"]?["part_img_url"]?.ToString(),
            $"bricks/{partNum}-{colorId}.jpg");

        var bricklinkIds = part["part"]?["external_ids"]?["BrickLink"]?.AsArray();

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
            BricklinkId = bricklinkIds?.Count > 0 ? bricklinkIds[0]?.ToString() : null
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
        var api = new RebrickableApi();
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
        var api = new RebrickableApi();
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

    public async Task BackfillImagesAsync(IProgress<(int done, int total)>? progress = null, CancellationToken ct = default)
    {
        await using var context = contextFactory.CreateDbContext();

        var sets     = await context.Set<Set>()    .Where(s => s.SetImg        != null && s.SetImg.StartsWith("http"))       .ToListAsync(ct);
        var minifigs = await context.Set<Minifig>().Where(m => m.MinifigImgUrl != null && m.MinifigImgUrl.StartsWith("http")).ToListAsync(ct);
        var bricks   = await context.Set<Brick>()  .Where(b => b.PartImg       != null && b.PartImg.StartsWith("http"))      .ToListAsync(ct);

        var total = sets.Count + minifigs.Count + bricks.Count;
        var done = 0;

        var setWork     = sets    .Select((s, i) => (i, src: s.SetImg!,        key: $"sets/{s.SetId}.jpg")).ToArray();
        var minifigWork = minifigs.Select((m, i) => (i, src: m.MinifigImgUrl!, key: $"minifigs/{m.MinifigId}.jpg")).ToArray();
        var brickWork   = bricks  .Select((b, i) => (i, src: b.PartImg!,       key: $"bricks/{b.PartNum}-{b.ColorId}.jpg")).ToArray();

        var setUrls     = new string?[sets.Count];
        var minifigUrls = new string?[minifigs.Count];
        var brickUrls   = new string?[bricks.Count];

        var sem = new SemaphoreSlim(5, 5);

        async Task ProcessAsync(string src, string key, string?[] results, int index)
        {
            await sem.WaitAsync(ct);
            try   { results[index] = await imageStorage.StoreImageAsync(src, key); }
            finally
            {
                sem.Release();
                Interlocked.Increment(ref done);
                progress?.Report((done, total));
            }
        }

        var tasks = setWork    .Select(w => ProcessAsync(w.src, w.key, setUrls,     w.i))
            .Concat(minifigWork.Select(w => ProcessAsync(w.src, w.key, minifigUrls, w.i)))
            .Concat(brickWork  .Select(w => ProcessAsync(w.src, w.key, brickUrls,   w.i)));

        try   { await Task.WhenAll(tasks); }
        catch (OperationCanceledException) { }

        for (int i = 0; i < sets.Count;     i++) if (setUrls[i]     != null) sets[i].SetImg        = setUrls[i];
        for (int i = 0; i < minifigs.Count; i++) if (minifigUrls[i] != null) minifigs[i].MinifigImgUrl = minifigUrls[i];
        for (int i = 0; i < bricks.Count;   i++) if (brickUrls[i]   != null) bricks[i].PartImg      = brickUrls[i];

        await context.SaveChangesAsync(CancellationToken.None);
    }
}
