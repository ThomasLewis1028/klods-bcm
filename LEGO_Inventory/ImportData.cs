using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using LEGO_Inventory.Components.Pages;
using LEGO_Inventory.Database;

namespace LEGO_Inventory;

public class ImportData
{
    private readonly ILogger<ImportData> _logger =
        LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<ImportData>();

    public bool ImportAll(List<string> setIds)
    {
        foreach (string setId in setIds)
        {
            try
            {
                _logger.LogInformation($"Importing All Data for set {setId}");

                ImportSetInfo(setId);
                ImportBricks(setId);
                ImportMinifigs(setId);

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

    public bool AddOwnedSet(string setId)
    {
        try
        {
            _logger.LogInformation($"Adding Owned Set for {setId}");

            using (var context = new InventoryContext())
            {
                var ownedSetContext = context.Set<SetOwned>();

                var index = ownedSetContext.Count(so => so.SetId == setId);

                ownedSetContext.Add(new SetOwned
                {
                    SetId = setId,
                    SetIndex = index
                });

                var count = context.SaveChanges();

                ImportSetParts(setId, index);
                ImportSetMinifigs(setId, index);
                
                return count > 0;
            }


            _logger.LogInformation($"DONE Adding Owned Set for {setId}");
        }
        catch (Exception e)
        {
            _logger.LogError($"Failed to add Owned Set for {setId}" + Environment.NewLine + e);
            return false;
        }

        return true;
    }

    public bool ImportSetInfo(string? setId)
    {
        _logger.LogInformation($"Importing {setId}");
        RebrickableApi api = new RebrickableApi();

        JsonObject? setInfo = api.GetSetInfo(setId).Result;

        using (var context = new InventoryContext())
        {
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

        return false;
    }

    public bool ImportSetParts(string setId, int setIndex)
    {
        _logger.LogInformation($"Importing set parts for {setId}");
        RebrickableApi api = new RebrickableApi();

        JsonObject? setParts = api.GetSetParts(setId).Result;
        int saveCount = 0;

        using (var context = new InventoryContext())
        {
            var setContext = context.Set<Set>();

            if (setContext.Any(s => s.SetId == setId))
            {
                var set = setContext.First(s => s.SetId == setId);

                var brickContext = context.Set<Brick>();
                var setBrickContext = context.Set<SetBrick>();

                foreach (var part in setParts!["results"]!.AsArray())
                {
                    Brick brick;

                    brick = brickContext.First(b => b.PartNum == part!["part"]!["part_num"]!.ToString()
                                                    && b.ColorId == part["color"]!["id"]!.ToString());

                    if (brick == null)
                    {
                        throw new Exception($"No brick found with ID {part!["part"]!["part_num"]}");
                    }

                    var partNum = brick!.PartNum;
                    var colorId = brick!.ColorId;
                    var localSetId = set.SetId;
                    var count = 0;
                    var spareCount = 0;

                    var isSpare = part!["is_spare"]!.ToString().Equals("true");

                    if (isSpare)
                    {
                        spareCount = int.Parse(part!["quantity"].ToString());
                    }
                    else
                    {
                        count = int.Parse(part!["quantity"].ToString());
                    }

                    if (!setBrickContext.Any(sb => sb.PartNum == partNum
                                                   && sb.ColorId == colorId
                                                   && sb.SetId == localSetId))
                    {
                        SetBrick setBrick = new SetBrick
                        {
                            PartNum = partNum,
                            ColorId = colorId,
                            SetId = localSetId,
                            Count = count,
                            SpareCount = spareCount,
                            SetIndex = setIndex,
                        };

                        setBrickContext.Add(setBrick);
                    }
                    else
                    {
                        SetBrick setBrick = setBrickContext.First(sb => sb.PartNum == partNum
                                                                        && sb.ColorId == colorId
                                                                        && sb.SetId == localSetId
                                                                        && sb.SetIndex == setIndex);

                        if (isSpare)
                            setBrick.SpareCount = spareCount;
                        else
                            setBrick.Count = count;
                    }


                    saveCount += context.SaveChanges();
                }
            }
            else
            {
                throw new Exception($"No set found with ID {setId} in database");
            }

            _logger.LogInformation($"Importing set parts for {setId} Completed");
            return saveCount > 0;
        }
    }

    public bool ImportSetMinifigs(string setId, int setIndex)
    {
        _logger.LogInformation($"Importing set minifigs for {setId}");
        RebrickableApi api = new RebrickableApi();

        using (var context = new InventoryContext())
        {
            JsonObject? jsonObject = api.GetSetMinifigs(setId).Result;

            foreach (var minifig in jsonObject!["results"].AsArray())
            {
                var minifigId = minifig!["set_num"]!.ToString();
                var quantity = (int)minifig!["quantity"]!;

                ImportMinifig(minifigId);

                LinkMinifigBricks(minifigId);

                LinkMinifigToSet(minifigId, setId, setIndex, quantity);
            }
        }

        return false;
    }

    public bool ImportMinifig(string minifigId)
    {
        _logger.LogInformation($"Importing minifig {minifigId}");
        RebrickableApi api = new RebrickableApi();

        using (var context = new InventoryContext())
        {
            var minifigContext = context.Set<Minifig>();

            Minifig minifig;

            if (!minifigContext.Any(m => m.MinifigId == minifigId))
            {
                JsonObject? minifigJsonObject = api.GetMinifigInfo(minifigId).Result;

                var minifigName = minifigJsonObject!["name"]?.ToString();
                var minifigImgUrl = minifigJsonObject["set_img_url"]?.ToString();
                var minifigUrl = minifigJsonObject["set_url"]?.ToString();

                minifig = new Minifig
                {
                    MinifigId = minifigId,
                    MinifigName = minifigName,
                    MinifigImgUrl = minifigImgUrl,
                    MinifigUrl = minifigUrl,
                };

                minifigContext.Add(minifig);
                _logger.LogInformation($"Imported minifig ({minifigId}) {minifigName}");

                if (context.SaveChanges() > 0)
                    return true;
                else return false;
            }

            return false;
        }
    }

    public bool ImportMinifigs(string setId)
    {
        _logger.LogInformation($"Importing set minifigs for {setId}");
        RebrickableApi api = new RebrickableApi();

        JsonObject? jsonObject = api.GetSetMinifigs(setId).Result;

        foreach (var minifig in jsonObject!["results"].AsArray())
        {
            var minifigId = minifig!["set_num"]!.ToString();

            ImportMinifig(minifigId);

            LinkMinifigBricks(minifigId);
        }

        return false;
    }

    public bool LinkMinifigToSet(string minifigId, string setId, int setIndex, int quantity)
    {
        _logger.LogInformation($"Linking minifig {minifigId} to set {setId}");

        using (var context = new InventoryContext())
        {
            var setMinifigContext = context.Set<SetMinifig>();
            var setBrickContext = context.Set<SetBrick>();
            var minifigBrickContext = context.Set<MinifigBrick>();

            if (!setMinifigContext.Any(sm => sm.MinifigId == minifigId && sm.SetId == setId))
            {
                List<MinifigBrick> minifigBricks = minifigBrickContext.Where(mb => mb.MinifigID == minifigId).ToList();

                SetMinifig setMinifig = new SetMinifig
                {
                    MinifigId = minifigId,
                    SetId = setId,
                    Count = quantity,
                    SetIndex = setIndex,
                };

                List<SetBrick> setBricks = new List<SetBrick>();

                foreach (var minifigBrick in minifigBricks)
                {
                    if (setBrickContext.Any(sb =>
                            sb.PartNum == minifigBrick.BrickID
                            && sb.ColorId == minifigBrick.ColorId
                            && sb.SetId == setId
                            && sb.SetIndex == setIndex))
                    {
                        setBrickContext.First(sb =>
                            sb.PartNum == minifigBrick.BrickID
                            && sb.ColorId == minifigBrick.ColorId
                            && sb.SetId == setId
                            && sb.SetIndex == setIndex).Count += quantity * minifigBrick.Quantity;
                    }
                    else
                    {
                        SetBrick setBrick = new SetBrick()
                        {
                            SetId = setId,
                            PartNum = minifigBrick.BrickID,
                            ColorId = minifigBrick!.ColorId,
                            Count = quantity * minifigBrick.Quantity,
                            SetIndex = setIndex
                        };

                        setBricks.Add(setBrick);
                    }
                }

                setBrickContext.AddRange(setBricks);
                setMinifigContext.Add(setMinifig);

                return context.SaveChanges() > 0;
            }
        }

        return false;
    }

    public bool LinkMinifigBricks(string minifigId)
    {
        _logger.LogInformation($"Linking minifig bricks for {minifigId}");
        RebrickableApi api = new RebrickableApi();

        using (var context = new InventoryContext())
        {
            var minifigBrickContext = context.Set<MinifigBrick>();
            var brickContext = context.Set<Brick>();

            JsonObject? jsonObject = api.GetMinifigParts(minifigId).Result;

            foreach (var brick in jsonObject!["results"]!.AsArray())
            {
                var brickId = brick["part"]!["part_num"]?.ToString();
                var colorId = brick["color"]!["id"]?.ToString();
                var quantity = (int)brick["quantity"]!;

                MinifigBrick minifigBrick;

                if (minifigBrickContext.Any(mb => mb.MinifigID == minifigId
                                                  && mb.BrickID == brickId
                                                  && mb.ColorId == colorId))
                    continue;

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

        return false;
    }

    public bool ImportBrick(string brickId, string colorId)
    {
        _logger.LogInformation($"Importing brick {brickId} with color {colorId}");

        try
        {
            using (var context = new InventoryContext())
            {
                var brickContext = context.Set<Brick>();

                Brick brick;

                if (brickContext.Any(b => b.PartNum == brickId && b.ColorId == colorId))
                    throw new Exception("Brick already imported");

                RebrickableApi api = new RebrickableApi();

                JsonObject part = api.GetPartInfo(brickId).Result.AsObject();
                var partNum = part!["part_num"]!.ToString();
                var name = part!["name"]!.ToString();
                var partUrl = part["part_url"]?.ToString() ?? null;

                JsonObject color = api.GetColorInfo(colorId).Result;
                var colorName = color!["name"]?.ToString() ?? null;
                var rgb = color["rgb"]?.ToString() ?? null;
                var isTrans = color["is_trans"]!.ToString().Equals("true");
                var count = 0;

                JsonObject partColorInfo = api.GetPartColorInfo(brickId, colorId).Result.AsObject();
                var partImg = partColorInfo!["part_img_url"]?.ToString() ?? null;

                brick = new Brick
                {
                    PartNum = partNum,
                    Name = name,
                    PartURL = partUrl,
                    PartImg = partImg,
                    Count = count,
                    ColorId = colorId ?? null,
                    ColorName = colorName,
                    IsTrans = isTrans,
                    HexColor = rgb
                };

                brickContext.Add(brick);
                context.SaveChanges();
            }
        }
        catch (Exception e)
        {
            _logger.LogError($"Failed to import brick {brickId} with color {colorId}");
            _logger.LogError($"Exception: {e}");

            return false;
        }

        return false;
    }


    public bool ImportBricks(string setId)
    {
        _logger.LogInformation($"Importing bricks for {setId}");

        try
        {
            RebrickableApi api = new RebrickableApi();

            JsonObject? setParts = api.GetSetParts(setId).Result;
            int saveCount = 0;

            using (var context = new InventoryContext())
            {
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
        }
        catch (Exception e)
        {
            _logger.LogError($"Failed to import bricks for {setId}");
            _logger.LogError($"Exception: {e}");

            return false;
        }

        return false;
    }

    public Brick ImportBrick(JsonNode part)
    {
        _logger.LogInformation($"Importing set parts for {part!["part"]!["part_num"]}");
        using (var context = new InventoryContext())
        {
            var brickContext = context.Set<Brick>();

            Brick brick;

            if (!brickContext.Any(b => b.PartNum == part!["part"]!["part_num"]!.ToString()
                                       && b.ColorId == part!["color"]!["id"]!.ToString()))
            {
                var partNum = part!["part"]!["part_num"]!.ToString();
                var name = part!["part"]!["name"]!.ToString();
                var partUrl = part["part"]?["part_url"]?.ToString() ?? null;
                var partImg = part["part"]?["part_img_url"]?.ToString() ?? null;
                var colorId = part["color"]?["id"]?.ToString() ?? null;
                var colorName = part["color"]?["name"]?.ToString() ?? null;
                var rgb = part["color"]?["rgb"]?.ToString() ?? null;
                var isTrans = part!["color"]!["is_trans"]!.ToString().Equals("true");
                var count = 0;

                brick = new Brick
                {
                    PartNum = partNum,
                    Name = name,
                    PartURL = partUrl,
                    PartImg = partImg,
                    Count = count,
                    ColorId = colorId ?? null,
                    ColorName = colorName,
                    IsTrans = isTrans,
                    HexColor = rgb
                };

                brickContext.Add(brick);
                context.SaveChanges();

                return brick;
            }
        }

        return null;
    }

    public bool ImportColors()
    {
        _logger.LogInformation($"Importing colors");
        RebrickableApi api = new RebrickableApi();

        using (var context = new InventoryContext())
        {
            var colorContext = context.Set<Color>();
            JsonObject? jsonObject = api.GetColors().Result;

            List<Color> colors = new List<Color>();

            foreach (var color in jsonObject!["results"]!.AsArray())
            {
                if (colorContext.Any(c => c.Id == color!["id"]!.ToString()))
                    continue;

                Color c = new Color
                {
                    Id = color!["id"]!.ToString(),
                    Name = color!["name"]!.ToString(),
                    Hex = color!["rgb"]!.ToString(),
                    IsTrans = color!["is_trans"]!.ToString().Equals("true")
                };

                colors.Add(c);
            }

            colorContext.AddRange(colors);

            return context.SaveChanges() > 0;
        }

        return false;
    }
}