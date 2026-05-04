using LEGO_Inventory.Database;
using Microsoft.EntityFrameworkCore;

namespace LEGO_Inventory;

public static class InventoryAggregates
{
    public static Dictionary<string, int> GetSetCopies(InventoryContext context, int? userId = null)
    {
        var query = context.Set<SetOwned>().AsNoTracking();
        if (userId.HasValue)
            query = query.Where(so => so.UserId == userId.Value);
        return query
            .GroupBy(so => so.SetId)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    public static async Task<Dictionary<string, int>> GetSetCopiesAsync(InventoryContext context, int? userId = null)
    {
        var query = context.Set<SetOwned>().AsNoTracking();
        if (userId.HasValue)
            query = query.Where(so => so.UserId == userId.Value);
        var list = await query.ToListAsync();
        return list
            .GroupBy(so => so.SetId)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    public static Dictionary<(string PartNum, string ColorId), int> GetBrickNeededDict(
        IEnumerable<SetBrick> setBricks, Dictionary<string, int> setCopies) =>
        setBricks
            .GroupBy(sb => (sb.PartNum, sb.ColorId))
            .ToDictionary(
                g => g.Key,
                g => g.Sum(sb => sb.Count * setCopies.GetValueOrDefault(sb.SetId, 0)));

    public static Dictionary<(string PartNum, string ColorId), int> GetBrickSetCountDict(
        IEnumerable<SetBrick> setBricks) =>
        setBricks
            .GroupBy(sb => (sb.PartNum, sb.ColorId))
            .ToDictionary(g => g.Key, g => g.Select(sb => sb.SetId).Distinct().Count());

    public static Dictionary<string, int> GetMinifigNeededDict(
        IEnumerable<SetMinifig> setMinifigs, Dictionary<string, int> setCopies) =>
        setMinifigs
            .GroupBy(sm => sm.MinifigId)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(sm => sm.Count * setCopies.GetValueOrDefault(sm.SetId, 0)));

    public static Dictionary<string, int> GetMinifigSetCountDict(
        IEnumerable<SetMinifig> setMinifigs) =>
        setMinifigs
            .GroupBy(sm => sm.MinifigId)
            .ToDictionary(g => g.Key, g => g.Select(sm => sm.SetId).Distinct().Count());
}
