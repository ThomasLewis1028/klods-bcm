using System.Text.Json.Nodes;
using LEGO_Inventory.Database;

namespace LEGO_Inventory;

public class ImportData
{
    private readonly ILogger<ImportData> _logger =
        LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<ImportData>();

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
                }
            }
            else
            {
                var set = new Set
                {
                    SetId = setInfo!["set_num"]!.ToString(),
                    Name = setInfo!["name"]!.ToString(),
                    SetURL = setInfo!["set_url"]!.ToString(),
                    SetImg = setInfo!["set_img_url"]!.ToString(),
                    DateModified = DateTime.Parse(setInfo!["last_modified_dt"]!.ToString()),
                    NumBricks = int.Parse(setInfo!["num_parts"]!.ToString()),
                    ReleaseYear = int.Parse(setInfo!["year"]!.ToString()),
                    ManualUrl = "",
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
                ImportMinifig(minifig!["set_num"]!.ToString());

                LinkMinifigToSet(minifig!["set_num"]!.ToString(), setId, (int)minifig!["quantity"]!);
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

                var minifigName = minifigJsonObject!["name"].ToString();
                var minifigImgUrl = minifigJsonObject!["set_img_url"].ToString();
                var minifigUrl = minifigJsonObject!["set_url"].ToString();
                // var minifigBricks = LinkMinifigBricks(minifigId);

                minifig = new Minifig
                {
                    MinifigId = minifigId,
                    MinifigName = minifigName,
                    MinifigImgUrl = minifigImgUrl,
                    MinifigUrl = minifigUrl,
                    // MinifigBricks = minifigBricks,
                };

                minifigContext.Add(minifig);
                _logger.LogInformation($"Imported minifig ({minifigId}) {minifigName}");

                return context.SaveChanges() > 0;
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

            if (!setMinifigContext.Any(sm => sm.MinifigId == minifigId && sm.SetId == setId))
            {
                SetMinifig setMinifig = new SetMinifig
                {
                    MinifigId = minifigId,
                    SetId = setId,
                    Count = quantity,
                };

                setMinifigContext.Add(setMinifig);

                return context.SaveChanges() > 0;
            }
        }

        return false;
    }

    public List<Brick> LinkMinifigBricks(string minifigId)
    {
        _logger.LogInformation($"Linking minifig bricks for {minifigId}");
        RebrickableApi api = new RebrickableApi();

        using (var context = new InventoryContext())
        {
            var brickContext = context.Set<Brick>();

            JsonObject? jsonObject = api.GetMinifigParts(minifigId).Result;

            List<Brick> minifigBricks = new List<Brick>();

            foreach (var brick in jsonObject!["results"]!.AsArray())
            {
                try
                {
                    minifigBricks.Add(brickContext.First(b => b.PartNum == brick["part"]!["part_num"]!.ToString()
                                                              && b.ColorId == brick["color"]!["id"]!.ToString()));
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to get minifig bricks for {minifigId}");
                    throw new Exception($"Failed to get minifig bricks for {minifigId}", ex);
                }
            }

            return minifigBricks;
        }
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
}