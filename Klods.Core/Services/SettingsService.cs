using Klods.Database;
using Microsoft.EntityFrameworkCore;

namespace Klods.Services;

/// <summary>Thin get/set over the <see cref="Setting"/> key/value table.</summary>
public class SettingsService(IDbContextFactory<InventoryContext> contextFactory)
{
    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await db.Set<Setting>().AsNoTracking()
            .Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync(ct);
    }

    public async Task<bool> GetBoolAsync(string key, bool fallback = false, CancellationToken ct = default)
        => await GetAsync(key, ct) is { } v ? v == "true" : fallback;

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var existing = await db.Set<Setting>().FirstOrDefaultAsync(s => s.Key == key, ct);
        if (existing == null)
            db.Set<Setting>().Add(new Setting { Key = key, Value = value });
        else
            existing.Value = value;
        await db.SaveChangesAsync(ct);
    }
}
