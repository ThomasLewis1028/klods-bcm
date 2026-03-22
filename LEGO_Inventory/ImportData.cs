using System.Text.Json.Nodes;
using LEGO_Inventory.Database;

namespace LEGO_Inventory;

public class ImportData
{
    private readonly ILogger<ImportData> _logger =
        LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<ImportData>();

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
                _logger.LogInformation($"Importing All Data for set {setId}");

                await ImportSetInfo(setId);
                await ImportBricks(setId);
                await ImportSetBOM(setId);
                await ImportMinifigs(setId);
                await ImportSetMinifigBOM(setId);

                _logger.LogInformation($"DONE Importing All Data for set {setId}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to import All Data for set {setId}" + Environment.NewLine + ex);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Adds an owned set instance for a user. Creates SetOwned + SetBrickOwned rows.
    /// </summary>
    public async Task<bool> AddOwnedSet(string setId, int? userId = null, bool applyBricks = false)
    {
        try
        {
            _logger.LogInformation($"Adding Owned Set for {setId}");

            if (userId == null)
            {
                _logger.LogWarning($"AddOwnedSet called without a userId for set {setId} — skipping.");
                return false;
            }

            using var context = new InventoryContext();
            var ownedSetContext = context.Set<SetOwned>();

            // SetIndex is per-user: count only this user's copies of the set
            var index = ownedSetContext.Count(so => so.SetId == setId && so.UserId == userId);

            ownedSetContext.Add(new SetOwned
            {
                SetId = setId,
                SetIndex = index,
                UserId = userId.Value
            });

            context.SaveChanges();

            // Ensure BOM exists before creating owned entries
            await ImportSetBOM(setId);
            await ImportSetMinifigBOM(setId);

            CreateSetBrickOwned(userId.Value, setId, index, applyBricks);
            EnsureBrickOwnedForSet(userId.Value, setId);
            EnsureMinifigOwnedForSet(userId.Value, setId);

            _logger.LogInformation($"DONE Adding Owned Set for {setId}");
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError($"Failed to add Owned Set for {setId}" + Environment.NewLine + e);
            return false;
        }
    }

    /// <summary>
    /// Creates SetBrickOwned rows (Stock = 0) for a specific owned set instance,
    /// based on the existing SetBrick BOM entries for that set.
    /// </summary>
    public bool CreateSetBrickOwned(int userId, string setId, int setIndex, bool applyBricks = false)
    {
        _logger.LogInformation($"Creating SetBrickOwned for user {userId}, {setId}-{setIndex}");

        using var context = new InventoryContext();
        var setBrickContext = context.Set<SetBrick>();
        var setBrickOwnedContext = context.Set<SetBrickOwned>();

        var bomEntries = setBrickContext.Where(sb => sb.SetId == setId).ToList();

        var existingKeys = setBrickOwnedContext
            .Where(sbo => sbo.UserId == userId && sbo.SetId == setId && sbo.SetIndex == setIndex)
            .Select(sbo => new { sbo.PartNum, sbo.ColorId })
            .AsEnumerable()
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

        return context.SaveChanges() > 0;
    }

    /// <summary>
    /// Ensures a BrickOwned(Stock=0) row exists for every brick in a set's BOM for the given user.
    /// Called when a user adds a set so My Bricks shows all relevant bricks immediately.
    /// </summary>
    public void EnsureBrickOwnedForSet(int userId, string setId)
    {
        using var context = new InventoryContext();

        var bomPartKeys = context.Set<SetBrick>()
            .Where(sb => sb.SetId == setId)
            .Select(sb => new { sb.PartNum, sb.ColorId })
            .ToList();

        var existingKeys = context.Set<BrickOwned>()
            .Where(bo => bo.UserId == userId)
            .Select(bo => new { bo.PartNum, bo.ColorId })
            .ToList()
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

        context.SaveChanges();
    }

    /// <summary>
    /// Ensures a MinifigOwned(Stock=0) row exists for every minifig in a set's BOM for the given user.
    /// Called when a user adds a set so the BOM minifig tab is immediately editable.
    /// </summary>
    public void EnsureMinifigOwnedForSet(int userId, string setId)
    {
        using var context = new InventoryContext();

        var bomMinifigIds = context.Set<SetMinifig>()
            .Where(sm => sm.SetId == setId)
            .Select(sm => sm.MinifigId)
            .ToList();

        var existingIds = context.Set<MinifigOwned>()
            .Where(mo => mo.UserId == userId)
            .Select(mo => mo.MinifigId)
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

        context.SaveChanges();
    }

    public async Task<bool> ImportSetInfo(string? setId)
    {
        _logger.LogInformation($"Importing {setId}");
        RebrickableApi api = new RebrickableApi();

        JsonObject? setInfo = await api.GetSetInfo(setId);

        using var context = new InventoryContext();
        var setContext = context.Set<Set>();

        if (setContext.Any(s => s.SetId == setId))
        {
            var set = setContext.First(s => s.SetId == setId);

            if (set.DateModified >= DateTime.Parse(setInfo!["last_modified_dt"]!.ToString()).ToUniversalTime())
            {
                set.Name = setInfo!["name"]!.ToString();
                set.SetImg = setInfo!["set_img_url"]!.ToString();
                set.SetURL = setInfo!["set_url"]!.ToString();
                set.DateModified = DateTime.Parse(setInfo!["last_modified_dt"]!.ToString()).ToUniversalTime();
                set.NumBricks = int.Parse(setInfo!["num_parts"]!.ToString());
                set.ReleaseYear = int.Parse(setInfo!["year"]!.ToString());
                set.ManualUrl =
                    $"https://www.lego.com/en-us/service/buildinginstructions/{setId.Split('-').First()}";
            }
        }
        else
        {
            var set = new Set
            {
                SetId = setInfo!["set_num"]!.ToString(),
                Name = setInfo!["name"]!.ToString(),
                SetURL = setInfo["set_url"]?.ToString(),
                SetImg = setInfo!["set_img_url"]?.ToString(),
                DateModified = DateTime.Parse(setInfo!["last_modified_dt"]!.ToString()).ToUniversalTime(),
                NumBricks = int.Parse(setInfo!["num_parts"]!.ToString()),
                ReleaseYear = int.Parse(setInfo!["year"]!.ToString()),
                ManualUrl = $"https://www.lego.com/en-us/service/buildinginstructions/{setId.Split('-').First()}",
            };

            setContext.Add(set);
        }

        _logger.LogInformation($"Importing {setId} Completed");
        return context.SaveChanges() > 0;
    }

    /// <summary>
    /// Creates/updates SetBrick BOM entries for a set (no SetIndex — BOM is per-set).
    /// </summary>
    public async Task<bool> ImportSetBOM(string setId)
    {
        _logger.LogInformation($"Importing SetBrick BOM for {setId}");
        RebrickableApi api = new RebrickableApi();

        JsonObject? setParts = await api.GetSetParts(setId);
        int saveCount = 0;

        using var context = new InventoryContext();

        if (!context.Set<Set>().Any(s => s.SetId == setId))
            throw new Exception($"No set found with ID {setId} in database");

        var brickContext = context.Set<Brick>();
        var setBrickContext = context.Set<SetBrick>();

        foreach (var part in setParts!["results"]!.AsArray())
        {
            var brick = brickContext.FirstOrDefault(b =>
                b.PartNum == part!["part"]!["part_num"]!.ToString() &&
                b.ColorId == part["color"]!["id"]!.ToString());

            if (brick == null)
                throw new Exception($"No brick found with ID {part!["part"]!["part_num"]}");

            var partNum = brick.PartNum;
            var colorId = brick.ColorId;
            var isSpare = part!["is_spare"]!.ToString().Equals("true");
            var quantity = int.Parse(part!["quantity"].ToString());

            var existing = setBrickContext.FirstOrDefault(sb =>
                sb.SetId == setId && sb.PartNum == partNum && sb.ColorId == colorId);

            if (existing == null)
            {
                setBrickContext.Add(new SetBrick
                {
                    SetId = setId,
                    PartNum = partNum,
                    ColorId = colorId,
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

            saveCount += context.SaveChanges();
        }

        _logger.LogInformation($"Importing SetBrick BOM for {setId} Completed");
        return saveCount > 0;
    }

    /// <summary>
    /// Creates/updates SetMinifig BOM entries and merges minifig brick parts into SetBrick BOM.
    /// </summary>
    public async Task<bool> ImportSetMinifigBOM(string setId)
    {
        _logger.LogInformation($"Importing SetMinifig BOM for {setId}");
        RebrickableApi api = new RebrickableApi();

        using var context = new InventoryContext();
        JsonObject? jsonObject = await api.GetSetMinifigs(setId);

        foreach (var minifig in jsonObject!["results"].AsArray())
        {
            var minifigId = minifig!["set_num"]!.ToString();
            var quantity = (int)minifig!["quantity"]!;

            await ImportMinifig(minifigId);
            await LinkMinifigBricks(minifigId);
            LinkMinifigToSetBOM(minifigId, setId, quantity);
        }

        return false;
    }

    public async Task<bool> ImportMinifig(string minifigId)
    {
        _logger.LogInformation($"Importing minifig {minifigId}");
        RebrickableApi api = new RebrickableApi();

        using var context = new InventoryContext();
        var minifigContext = context.Set<Minifig>();

        if (minifigContext.Any(m => m.MinifigId == minifigId))
            return false;

        JsonObject? minifigJsonObject = await api.GetMinifigInfo(minifigId);

        var minifig = new Minifig
        {
            MinifigId = minifigId,
            MinifigName = minifigJsonObject!["name"]?.ToString(),
            MinifigImgUrl = minifigJsonObject["set_img_url"]?.ToString(),
            MinifigUrl = minifigJsonObject["set_url"]?.ToString(),
        };

        minifigContext.Add(minifig);
        _logger.LogInformation($"Imported minifig ({minifigId}) {minifig.MinifigName}");

        return context.SaveChanges() > 0;
    }

    public async Task<bool> ImportMinifigs(string setId)
    {
        _logger.LogInformation($"Importing set minifigs for {setId}");
        RebrickableApi api = new RebrickableApi();

        JsonObject? jsonObject = await api.GetSetMinifigs(setId);

        foreach (var minifig in jsonObject!["results"].AsArray())
        {
            var minifigId = minifig!["set_num"]!.ToString();
            await ImportMinifig(minifigId);
            await LinkMinifigBricks(minifigId);
        }

        return false;
    }

    /// <summary>
    /// Creates/updates a SetMinifig BOM entry and merges minifig bricks into SetBrick BOM.
    /// </summary>
    public bool LinkMinifigToSetBOM(string minifigId, string setId, int quantity)
    {
        _logger.LogInformation($"Linking minifig {minifigId} to set {setId} BOM");

        using var context = new InventoryContext();
        var setMinifigContext = context.Set<SetMinifig>();
        var setBrickContext = context.Set<SetBrick>();
        var minifigBrickContext = context.Set<MinifigBrick>();

        // Create or update SetMinifig BOM entry
        var existing = setMinifigContext.FirstOrDefault(sm => sm.MinifigId == minifigId && sm.SetId == setId);
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

        // Merge minifig brick parts into SetBrick BOM
        var minifigBricks = minifigBrickContext.Where(mb => mb.MinifigID == minifigId).ToList();

        foreach (var minifigBrick in minifigBricks)
        {
            var existingBrick = setBrickContext.FirstOrDefault(sb =>
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

        return context.SaveChanges() > 0;
    }

    public async Task<bool> LinkMinifigBricks(string minifigId)
    {
        _logger.LogInformation($"Linking minifig bricks for {minifigId}");
        RebrickableApi api = new RebrickableApi();

        using var context = new InventoryContext();
        var minifigBrickContext = context.Set<MinifigBrick>();
        var brickContext = context.Set<Brick>();

        JsonObject? jsonObject = await api.GetMinifigParts(minifigId);

        foreach (var brick in jsonObject!["results"]!.AsArray())
        {
            var brickId = brick["part"]!["part_num"]?.ToString();
            var colorId = brick["color"]!["id"]?.ToString();
            var quantity = (int)brick["quantity"]!;

            if (minifigBrickContext.Any(mb => mb.MinifigID == minifigId && mb.BrickID == brickId && mb.ColorId == colorId))
                continue;

            MinifigBrick minifigBrick;

            if (brickContext.Any(b => b.PartNum == brickId && b.ColorId == colorId))
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
                Brick newBrick = ImportBrick(brick);
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

        return context.SaveChanges() > 0;
    }

    public async Task<bool> ImportBrick(string brickId, string colorId)
    {
        _logger.LogInformation($"Importing brick {brickId} with color {colorId}");

        try
        {
            using var context = new InventoryContext();
            var brickContext = context.Set<Brick>();

            if (brickContext.Any(b => b.PartNum == brickId && b.ColorId == colorId))
                throw new Exception("Brick already imported");

            RebrickableApi api = new RebrickableApi();

            JsonObject part = (await api.GetPartInfo(brickId))!.AsObject();
            var partNum = part!["part_num"]!.ToString();
            var name = part!["name"]!.ToString();
            var partUrl = part["part_url"]?.ToString() ?? null;

            JsonObject color = await api.GetColorInfo(colorId);
            var colorName = color!["name"]?.ToString() ?? null;
            var rgb = color["rgb"]?.ToString() ?? null;
            var isTrans = color["is_trans"]!.ToString().Equals("true");

            JsonObject partColorInfo = (await api.GetPartColorInfo(brickId, colorId))!.AsObject();
            var partImg = partColorInfo!["part_img_url"]?.ToString() ?? null;

            brickContext.Add(new Brick
            {
                PartNum = partNum,
                Name = name,
                PartURL = partUrl,
                PartImg = partImg,
                ColorId = colorId,
                ColorName = colorName,
                IsTrans = isTrans,
                HexColor = rgb
            });

            context.SaveChanges();
        }
        catch (Exception e)
        {
            _logger.LogError($"Failed to import brick {brickId} with color {colorId}: {e}");
            return false;
        }

        return true;
    }

    public async Task<bool> ImportBricks(string setId)
    {
        _logger.LogInformation($"Importing bricks for {setId}");

        try
        {
            RebrickableApi api = new RebrickableApi();
            JsonObject? setParts = await api.GetSetParts(setId);
            int saveCount = 0;

            using var context = new InventoryContext();
            var brickContext = context.Set<Brick>();

            foreach (var part in setParts!["results"]!.AsArray())
            {
                if (!brickContext.Any(b => b.PartNum == part!["part"]!["part_num"]!.ToString()
                                           && b.ColorId == part!["color"]!["id"]!.ToString()))
                {
                    ImportBrick(part);
                    saveCount += context.SaveChanges();
                }
            }

            _logger.LogInformation($"Importing bricks for {setId} Completed");
            return saveCount > 0;
        }
        catch (Exception e)
        {
            _logger.LogError($"Failed to import bricks for {setId}: {e}");
            return false;
        }
    }

    public Brick ImportBrick(JsonNode part)
    {
        _logger.LogInformation($"Importing set parts for {part!["part"]!["part_num"]}");
        using var context = new InventoryContext();
        var brickContext = context.Set<Brick>();

        if (brickContext.Any(b => b.PartNum == part!["part"]!["part_num"]!.ToString()
                                   && b.ColorId == part!["color"]!["id"]!.ToString()))
            return null;

        var partNum = part!["part"]!["part_num"]!.ToString();
        var name = part!["part"]!["name"]!.ToString();
        var partUrl = part["part"]?["part_url"]?.ToString() ?? null;
        var partImg = part["part"]?["part_img_url"]?.ToString() ?? null;
        var colorId = part["color"]?["id"]?.ToString() ?? null;
        var colorName = part["color"]?["name"]?.ToString() ?? null;
        var rgb = part["color"]?["rgb"]?.ToString() ?? null;
        var isTrans = part!["color"]!["is_trans"]!.ToString().Equals("true");

        var brick = new Brick
        {
            PartNum = partNum,
            Name = name,
            PartURL = partUrl,
            PartImg = partImg,
            ColorId = colorId,
            ColorName = colorName,
            IsTrans = isTrans,
            HexColor = rgb
        };

        brickContext.Add(brick);
        context.SaveChanges();
        return brick;
    }

    public async Task<bool> ImportColors()
    {
        _logger.LogInformation($"Importing colors");
        RebrickableApi api = new RebrickableApi();

        using var context = new InventoryContext();
        var colorContext = context.Set<Color>();
        JsonObject? jsonObject = await api.GetColors();

        List<Color> colors = new List<Color>();

        foreach (var color in jsonObject!["results"]!.AsArray())
        {
            if (colorContext.Any(c => c.Id == color!["id"]!.ToString()))
                continue;

            colors.Add(new Color
            {
                Id = color!["id"]!.ToString(),
                Name = color!["name"]!.ToString(),
                Hex = color!["rgb"]!.ToString(),
                IsTrans = color!["is_trans"]!.ToString().Equals("true")
            });
        }

        colorContext.AddRange(colors);
        return context.SaveChanges() > 0;
    }
}
