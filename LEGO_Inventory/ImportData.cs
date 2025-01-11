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
                    set.NumParts = int.Parse(setInfo!["num_parts"]!.ToString());
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
                    NumParts = int.Parse(setInfo!["num_parts"]!.ToString()),
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