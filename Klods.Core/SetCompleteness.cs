using Klods.Database;
using Microsoft.EntityFrameworkCore;

namespace Klods;

/// <summary>
/// Per-copy "traffic light" completeness for owned set copies. A copy's needs are flattened to
/// (part, color) → qty (set bricks plus each required minifig's parts). What's present in the copy
/// (its <see cref="SetBrickOwned"/> plus the parts of the figs tied to it) is compared against that;
/// any shortfall is tested against the user's LOOSE pool (loose bricks plus the parts of loose figs).
/// The loose pool is read independently per copy — parts locked in other copies never count.
/// </summary>
public static class SetCompleteness
{
    public enum Status { Complete, Completable, Short }

    /// <param name="Have">Pieces present toward completion (per-part capped at what's required).</param>
    /// <param name="Missing">Pieces still needed (per-part shortfall) — Have + Missing == Required.</param>
    public record Result(int Percent, Status Status, int Required, int Have, int Missing);

    public static async Task<Dictionary<(string SetId, int SetIndex), Result>> ComputeAsync(
        InventoryContext db, int userId, IReadOnlyCollection<(string SetId, int SetIndex)> copies)
    {
        var results = new Dictionary<(string SetId, int SetIndex), Result>();
        if (copies.Count == 0) return results;

        var setIds = copies.Select(c => c.SetId).Distinct().ToList();

        // Required (part,color) → qty per set: set bricks + each required fig's parts × its count.
        var requiredBySet = new Dictionary<string, Dictionary<(string, string), int>>();
        void AddRequired(string setId, string part, string color, int qty)
        {
            if (!requiredBySet.TryGetValue(setId, out var d)) requiredBySet[setId] = d = new();
            d[(part, color)] = d.GetValueOrDefault((part, color)) + qty;
        }

        foreach (var sb in await db.Set<SetBrick>().AsNoTracking()
                     .Where(x => setIds.Contains(x.SetId))
                     .Select(x => new { x.SetId, x.PartNum, x.ColorId, x.Count }).ToListAsync())
            AddRequired(sb.SetId, sb.PartNum, sb.ColorId, sb.Count);

        foreach (var fp in await db.Set<SetMinifig>().AsNoTracking()
                     .Where(sm => setIds.Contains(sm.SetId))
                     .Join(db.Set<MinifigBrick>().AsNoTracking(), sm => sm.MinifigId, mb => mb.MinifigId,
                         (sm, mb) => new { sm.SetId, mb.PartNum, mb.ColorId, Qty = sm.Count * mb.Count }).ToListAsync())
            AddRequired(fp.SetId, fp.PartNum, fp.ColorId, fp.Qty);

        // In-place (part,color) → qty per copy: the copy's set-brick stock + its tied figs' parts.
        var inPlaceByCopy = new Dictionary<(string, int), Dictionary<(string, string), int>>();
        void AddInPlace(string setId, int idx, string part, string color, int qty)
        {
            var key = (setId, idx);
            if (!inPlaceByCopy.TryGetValue(key, out var d)) inPlaceByCopy[key] = d = new();
            d[(part, color)] = d.GetValueOrDefault((part, color)) + qty;
        }

        foreach (var sbo in await db.Set<SetBrickOwned>().AsNoTracking()
                     .Where(x => x.UserId == userId && setIds.Contains(x.SetId))
                     .Select(x => new { x.SetId, x.SetIndex, x.PartNum, x.ColorId, x.Stock }).ToListAsync())
            AddInPlace(sbo.SetId, sbo.SetIndex, sbo.PartNum, sbo.ColorId, sbo.Stock);

        foreach (var mp in await db.Set<MinifigOwned>().AsNoTracking()
                     .Where(mo => mo.UserId == userId && mo.SetId != null && setIds.Contains(mo.SetId))
                     .Join(db.Set<MinifigBrickOwned>().AsNoTracking(),
                         mo => new { mo.UserId, mo.MinifigId, mo.MinifigIndex },
                         mbo => new { mbo.UserId, mbo.MinifigId, mbo.MinifigIndex },
                         (mo, mbo) => new { mo.SetId, mo.SetIndex, mbo.PartNum, mbo.ColorId, mbo.Stock }).ToListAsync())
            AddInPlace(mp.SetId!, mp.SetIndex!.Value, mp.PartNum, mp.ColorId, mp.Stock);

        // Loose pool (part,color) → qty: loose bricks + the parts of loose figs. Read-only per copy.
        var loosePool = new Dictionary<(string, string), int>();
        void AddLoose(string part, string color, int qty)
            => loosePool[(part, color)] = loosePool.GetValueOrDefault((part, color)) + qty;

        foreach (var bo in await db.Set<BrickOwned>().AsNoTracking()
                     .Where(x => x.UserId == userId)
                     .Select(x => new { x.PartNum, x.ColorId, x.Stock }).ToListAsync())
            AddLoose(bo.PartNum, bo.ColorId, bo.Stock);

        foreach (var mp in await db.Set<MinifigOwned>().AsNoTracking()
                     .Where(mo => mo.UserId == userId && mo.SetId == null)
                     .Join(db.Set<MinifigBrickOwned>().AsNoTracking(),
                         mo => new { mo.UserId, mo.MinifigId, mo.MinifigIndex },
                         mbo => new { mbo.UserId, mbo.MinifigId, mbo.MinifigIndex },
                         (mo, mbo) => new { mbo.PartNum, mbo.ColorId, mbo.Stock }).ToListAsync())
            AddLoose(mp.PartNum, mp.ColorId, mp.Stock);

        foreach (var copy in copies)
        {
            var required = requiredBySet.GetValueOrDefault(copy.SetId);
            if (required is null || required.Count == 0)
            {
                results[copy] = new Result(100, Status.Complete, 0, 0, 0);
                continue;
            }

            var inPlace = inPlaceByCopy.GetValueOrDefault((copy.SetId, copy.SetIndex));
            int totalReq = 0, totalHave = 0;
            bool anyShort = false, anyUncoverable = false;

            foreach (var (key, need) in required)
            {
                totalReq += need;
                var have = inPlace?.GetValueOrDefault(key) ?? 0;
                totalHave += Math.Min(have, need);
                var shortfall = need - have;
                if (shortfall > 0)
                {
                    anyShort = true;
                    if (loosePool.GetValueOrDefault(key) < shortfall) anyUncoverable = true;
                }
            }

            // Floor, not round, so the bar only reads 100% when the copy is genuinely complete
            // (otherwise missing 1 part out of hundreds rounds up to a misleading 100%).
            var percent = totalReq == 0 ? 100 : (int)Math.Floor(100.0 * totalHave / totalReq);
            var status = !anyShort ? Status.Complete : anyUncoverable ? Status.Short : Status.Completable;
            results[copy] = new Result(percent, status, totalReq, totalHave, totalReq - totalHave);
        }

        return results;
    }
}
