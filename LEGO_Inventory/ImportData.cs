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
                ImportSetParts(setId);
                ImportSetMinifigs(setId);
                
                _logger.LogInformation($"DONE Importing All Data for set {setId}");
            }
            catch
            {
                _logger.LogError($"Failed to import All Data for set {setId}");
                return false;
            }
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

                if (set.DateModified >= DateTime.Parse(setInfo!["last_modified_dt"]!.ToString()))
                {
                    set.Name = setInfo!["name"]!.ToString();
                    set.SetImg = setInfo!["set_img_url"]!.ToString();
                    set.SetURL = setInfo!["set_url"]!.ToString();
                    set.DateModified = DateTime.Parse(setInfo!["last_modified_dt"]!.ToString());
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
                    DateModified = DateTime.Parse(setInfo!["last_modified_dt"]!.ToString()),
                    NumBricks = int.Parse(setInfo!["num_parts"]!.ToString()),
                    ReleaseYear = int.Parse(setInfo!["year"]!.ToString()),
                    ManualUrl = $"https://www.lego.com/en-us/service/buildinginstructions/{setId.Split('-').First()}",
                    OwnCount = 0,
                    BuildCount = 0
                };

                setContext.Add(set);
            }

            _logger.LogInformation($"Importing {setId} Completed");
            return context.SaveChanges() > 0;
        }

        return false;
    }

    public bool ImportSetParts(string setId)
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

                    if (!brickContext.Any(b => b.PartNum == part!["part"]!["part_num"]!.ToString()
                                               && b.ColorId == part!["color"]!["id"]!.ToString()))
                    {
                        brick = ImportBrick(part);
                    }
                    else
                    {
                        brick = brickContext.First(b => b.PartNum == part!["part"]!["part_num"]!.ToString()
                                                        && b.ColorId == part["color"]!["id"]!.ToString());
                    }

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
                        };

                        setBrickContext.Add(setBrick);
                    }
                    else
                    {
                        SetBrick setBrick = setBrickContext.First(sb => sb.PartNum == partNum
                                                                        && sb.ColorId == colorId
                                                                        && sb.SetId == localSetId);

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

            _logger.LogInformation($"Importing  set parts for {setId} Completed");
            return saveCount > 0;
        }

        return false;
    }

    public bool ImportSetMinifigs(string setId)
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

                LinkMinifigToSet(minifigId, setId, quantity);
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

    public bool LinkMinifigToSet(string minifigId, string setId, int quantity)
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
                };

                List<SetBrick> setBricks = new List<SetBrick>();

                foreach (var minifigBrick in minifigBricks)
                {
                    if (setBrickContext.Any(sb =>
                            sb.PartNum == minifigBrick.BrickID && sb.ColorId == minifigBrick.ColorId &&
                            sb.SetId == setId))
                    {
                        setBrickContext.First(sb =>
                            sb.PartNum == minifigBrick.BrickID && sb.ColorId == minifigBrick.ColorId &&
                            sb.SetId == setId).Count += quantity * minifigBrick.Quantity;
                    }
                    else
                    {
                        SetBrick setBrick = new SetBrick()
                        {
                            SetId = setId,
                            PartNum = minifigBrick.BrickID,
                            ColorId = minifigBrick!.ColorId,
                            Count = quantity * minifigBrick.Quantity,
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