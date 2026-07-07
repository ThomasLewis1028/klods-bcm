using Klods.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Klods;

/// <summary>
/// Loads the full Rebrickable bulk catalog (CSV files) into the catalog tables.
/// <para>
/// All-or-nothing by design: every required file must be present, and the whole load runs in a single
/// transaction over temporary staging tables, so a missing/corrupt file can never leave a half-built
/// catalog. Uses Postgres <c>COPY</c> (not EF) because <c>inventory_parts</c> alone is ~1.5M rows.
/// Each attempt is recorded in <see cref="CatalogImport"/>.
/// </para>
/// </summary>
public class BulkImportService(
    IDbContextFactory<InventoryContext> contextFactory,
    NotificationService notifications,
    ILogger<BulkImportService> logger)
{
    /// <summary>CSV files we consume; all must be supplied for a load to proceed.</summary>
    public static readonly string[] RequiredFiles =
    [
        "colors", "part_categories", "themes", "parts", "elements",
        "sets", "minifigs", "inventories", "inventory_parts", "inventory_minifigs",
    ];

    // Staging tables — all columns TEXT (cast on upsert) and ordered exactly as the CSV columns,
    // since COPY maps positionally. Temporary, so they vanish with the transaction.
    private const string StagingDdl = """
        CREATE TEMP TABLE stg_colors(id text,name text,rgb text,is_trans text,num_parts text,num_sets text,y1 text,y2 text) ON COMMIT DROP;
        CREATE TEMP TABLE stg_themes(id text,name text,parent_id text) ON COMMIT DROP;
        CREATE TEMP TABLE stg_part_categories(id text,name text) ON COMMIT DROP;
        CREATE TEMP TABLE stg_parts(part_num text,name text,part_cat_id text,part_material text) ON COMMIT DROP;
        CREATE TEMP TABLE stg_elements(element_id text,part_num text,color_id text,design_id text) ON COMMIT DROP;
        CREATE TEMP TABLE stg_sets(set_num text,name text,year text,theme_id text,num_parts text,img_url text) ON COMMIT DROP;
        CREATE TEMP TABLE stg_minifigs(fig_num text,name text,num_parts text,img_url text) ON COMMIT DROP;
        CREATE TEMP TABLE stg_inventories(id text,version text,set_num text) ON COMMIT DROP;
        CREATE TEMP TABLE stg_inventory_parts(inventory_id text,part_num text,color_id text,quantity text,is_spare text,img_url text) ON COMMIT DROP;
        CREATE TEMP TABLE stg_inventory_minifigs(inventory_id text,fig_num text,quantity text) ON COMMIT DROP;
        """;

    // Indexes to keep the big joins (everything hangs off inventory_parts) fast.
    private const string StagingIndexes = """
        CREATE INDEX ON stg_inventory_parts(inventory_id);
        CREATE INDEX ON stg_inventory_parts(part_num,color_id);
        CREATE INDEX ON stg_inventories(set_num);
        CREATE INDEX ON stg_inventory_minifigs(inventory_id);
        """;

    // Upserts in FK-safe order: reference tables → catalog roots → BOM. Each is idempotent.
    private static readonly string[] Upserts =
    [
        // Colors
        """
        INSERT INTO "Colors" ("Id","Name","Hex","IsTrans")
        SELECT id, name, rgb, lower(is_trans)='true' FROM stg_colors
        ON CONFLICT ("Id") DO UPDATE SET "Name"=EXCLUDED."Name","Hex"=EXCLUDED."Hex","IsTrans"=EXCLUDED."IsTrans";
        """,
        // Part categories
        """
        INSERT INTO "PartCategories" ("Id","Name")
        SELECT id::int, name FROM stg_part_categories
        ON CONFLICT ("Id") DO UPDATE SET "Name"=EXCLUDED."Name";
        """,
        // Themes — the full theme/subtheme tree (ParentId is a soft self-reference).
        """
        INSERT INTO "Themes" ("Id","Name","ParentId")
        SELECT id::int, name, NULLIF(parent_id,'')::int FROM stg_themes
        ON CONFLICT ("Id") DO UPDATE SET "Name"=EXCLUDED."Name","ParentId"=EXCLUDED."ParentId";
        """,
        // Bricks (part+color). Cover every (part,color) used by inventories OR listed as an element,
        // so downstream BOM FKs always resolve.
        """
        WITH pc AS (
            SELECT part_num, color_id FROM stg_inventory_parts
            UNION
            SELECT part_num, color_id FROM stg_elements
        ),
        el AS (
            SELECT DISTINCT ON (part_num,color_id) part_num, color_id, element_id
            FROM stg_elements ORDER BY part_num, color_id, element_id
        ),
        img AS (
            SELECT DISTINCT ON (part_num,color_id) part_num, color_id, img_url
            FROM stg_inventory_parts WHERE img_url <> '' ORDER BY part_num, color_id
        )
        INSERT INTO "Bricks" ("PartNum","ColorId","Name","ColorName","HexColor","IsTrans","PartCatId","ElementId","PartImg")
        SELECT pc.part_num, pc.color_id, COALESCE(p.name, pc.part_num), c.name, c.rgb,
               COALESCE(lower(c.is_trans)='true', false), NULLIF(p.part_cat_id,'')::int,
               el.element_id, img.img_url
        FROM pc
        LEFT JOIN stg_parts p   ON p.part_num = pc.part_num
        LEFT JOIN stg_colors c  ON c.id = pc.color_id
        LEFT JOIN el            ON el.part_num = pc.part_num AND el.color_id = pc.color_id
        LEFT JOIN img           ON img.part_num = pc.part_num AND img.color_id = pc.color_id
        ON CONFLICT ("PartNum","ColorId") DO UPDATE SET
            "Name"=EXCLUDED."Name","ColorName"=EXCLUDED."ColorName","HexColor"=EXCLUDED."HexColor",
            "IsTrans"=EXCLUDED."IsTrans","PartCatId"=EXCLUDED."PartCatId","ElementId"=EXCLUDED."ElementId",
            "PartImg"=COALESCE("Bricks"."PartImg", EXCLUDED."PartImg");
        """,
        // Sets. DateModified seeded to MinValue so a later API import (which has last_modified_dt) refreshes it.
        // ThemeId is a soft reference into "Themes" (loaded above); the theme name is joined at read time.
        """
        INSERT INTO "Sets" ("SetId","Name","SetURL","SetImg","NumBricks","ReleaseYear","DateModified","ManualUrl","ThemeId")
        SELECT s.set_num, s.name, NULL, NULLIF(s.img_url,''),
               COALESCE(NULLIF(s.num_parts,'')::int,0), COALESCE(NULLIF(s.year,'')::int,0),
               TIMESTAMPTZ '0001-01-01 00:00:00+00',
               'https://www.lego.com/en-us/service/buildinginstructions/' || split_part(s.set_num,'-',1),
               NULLIF(s.theme_id,'')::int
        FROM stg_sets s
        ON CONFLICT ("SetId") DO UPDATE SET
            "Name"=EXCLUDED."Name","NumBricks"=EXCLUDED."NumBricks","ReleaseYear"=EXCLUDED."ReleaseYear",
            "ManualUrl"=EXCLUDED."ManualUrl","ThemeId"=EXCLUDED."ThemeId",
            "SetImg"=COALESCE("Sets"."SetImg", EXCLUDED."SetImg");
        """,
        // Minifigs
        """
        INSERT INTO "Minifigs" ("MinifigId","Name","ImgUrl","Url","NumParts","DateModified")
        SELECT fig_num, name, NULLIF(img_url,''), NULL, COALESCE(NULLIF(num_parts,'')::int,0),
               TIMESTAMPTZ '0001-01-01 00:00:00+00'
        FROM stg_minifigs
        ON CONFLICT ("MinifigId") DO UPDATE SET
            "Name"=EXCLUDED."Name","NumParts"=EXCLUDED."NumParts",
            "ImgUrl"=COALESCE("Minifigs"."ImgUrl", EXCLUDED."ImgUrl");
        """,
        // SetBricks — latest inventory version per set; spare/regular split into SpareCount/Count.
        """
        WITH set_inv AS (
            SELECT DISTINCT ON (set_num) id AS inventory_id, set_num
            FROM stg_inventories
            WHERE set_num IN (SELECT set_num FROM stg_sets)
            ORDER BY set_num, version::int DESC
        )
        INSERT INTO "SetBricks" ("SetId","PartNum","ColorId","Count","SpareCount")
        SELECT si.set_num, ip.part_num, ip.color_id,
               SUM(CASE WHEN lower(ip.is_spare)='true' THEN 0 ELSE ip.quantity::int END),
               SUM(CASE WHEN lower(ip.is_spare)='true' THEN ip.quantity::int ELSE 0 END)
        FROM set_inv si
        JOIN stg_inventory_parts ip ON ip.inventory_id = si.inventory_id
        GROUP BY si.set_num, ip.part_num, ip.color_id
        ON CONFLICT ("SetId","PartNum","ColorId") DO UPDATE SET
            "Count"=EXCLUDED."Count","SpareCount"=EXCLUDED."SpareCount";
        """,
        // MinifigBricks — same shape, from each minifig's own inventory.
        """
        WITH fig_inv AS (
            SELECT DISTINCT ON (set_num) id AS inventory_id, set_num AS fig_num
            FROM stg_inventories
            WHERE set_num IN (SELECT fig_num FROM stg_minifigs)
            ORDER BY set_num, version::int DESC
        )
        INSERT INTO "MinifigBricks" ("MinifigId","PartNum","ColorId","Count","SpareCount")
        SELECT fi.fig_num, ip.part_num, ip.color_id,
               SUM(CASE WHEN lower(ip.is_spare)='true' THEN 0 ELSE ip.quantity::int END),
               SUM(CASE WHEN lower(ip.is_spare)='true' THEN ip.quantity::int ELSE 0 END)
        FROM fig_inv fi
        JOIN stg_inventory_parts ip ON ip.inventory_id = fi.inventory_id
        GROUP BY fi.fig_num, ip.part_num, ip.color_id
        ON CONFLICT ("MinifigId","PartNum","ColorId") DO UPDATE SET
            "Count"=EXCLUDED."Count","SpareCount"=EXCLUDED."SpareCount";
        """,
        // SetMinifig — figs in each set's latest inventory.
        """
        WITH set_inv AS (
            SELECT DISTINCT ON (set_num) id AS inventory_id, set_num
            FROM stg_inventories
            WHERE set_num IN (SELECT set_num FROM stg_sets)
            ORDER BY set_num, version::int DESC
        )
        INSERT INTO "SetMinifig" ("SetId","MinifigId","Count")
        SELECT si.set_num, im.fig_num, SUM(im.quantity::int)
        FROM set_inv si
        JOIN stg_inventory_minifigs im ON im.inventory_id = si.inventory_id
        JOIN stg_minifigs m ON m.fig_num = im.fig_num
        GROUP BY si.set_num, im.fig_num
        ON CONFLICT ("SetId","MinifigId") DO UPDATE SET "Count"=EXCLUDED."Count";
        """,
        // Denormalized popularity: how many sets each part+color appears in (drives the default ordering).
        """
        UPDATE "Bricks" SET "SetCount" = 0;
        UPDATE "Bricks" b SET "SetCount" = sub.cnt
        FROM (SELECT "PartNum", "ColorId", COUNT(*) AS cnt FROM "SetBricks" GROUP BY "PartNum", "ColorId") sub
        WHERE b."PartNum" = sub."PartNum" AND b."ColorId" = sub."ColorId";
        """,
    ];

    // Removal reconcile — the upserts above only add/update, so a part dropped upstream would linger.
    // This deletes BOM rows that aren't in the new snapshot, first returning any owned stock recorded
    // against a removed part to that owner's loose inventory (BrickOwned). The key temp tables reuse the
    // exact "latest inventory version per set/fig" logic as the SetBricks/MinifigBricks upserts, so the
    // surviving keys line up precisely — only genuinely-removed parts get deleted. Runs in the same
    // transaction as the upserts (all-or-nothing).
    private const string ReconcileSql = """
        CREATE TEMP TABLE stg_new_setbrick_keys ON COMMIT DROP AS
        WITH set_inv AS (
            SELECT DISTINCT ON (set_num) id AS inventory_id, set_num
            FROM stg_inventories WHERE set_num IN (SELECT set_num FROM stg_sets)
            ORDER BY set_num, version::int DESC
        )
        SELECT DISTINCT si.set_num, ip.part_num, ip.color_id
        FROM set_inv si JOIN stg_inventory_parts ip ON ip.inventory_id = si.inventory_id;
        CREATE INDEX ON stg_new_setbrick_keys(set_num, part_num, color_id);

        CREATE TEMP TABLE stg_new_minifigbrick_keys ON COMMIT DROP AS
        WITH fig_inv AS (
            SELECT DISTINCT ON (set_num) id AS inventory_id, set_num AS fig_num
            FROM stg_inventories WHERE set_num IN (SELECT fig_num FROM stg_minifigs)
            ORDER BY set_num, version::int DESC
        )
        SELECT DISTINCT fi.fig_num, ip.part_num, ip.color_id
        FROM fig_inv fi JOIN stg_inventory_parts ip ON ip.inventory_id = fi.inventory_id;
        CREATE INDEX ON stg_new_minifigbrick_keys(fig_num, part_num, color_id);

        -- SET PARTS: return owned stock for removed parts, then drop owned rows and the stale BOM rows.
        INSERT INTO "BrickOwned" ("UserId","PartNum","ColorId","Stock")
        SELECT sbo."UserId", sbo."PartNum", sbo."ColorId", SUM(sbo."Stock")
        FROM "SetBrickOwned" sbo
        WHERE sbo."Stock" > 0
          AND sbo."SetId" IN (SELECT set_num FROM stg_sets)
          AND NOT EXISTS (SELECT 1 FROM stg_new_setbrick_keys k
                          WHERE k.set_num = sbo."SetId" AND k.part_num = sbo."PartNum" AND k.color_id = sbo."ColorId")
        GROUP BY sbo."UserId", sbo."PartNum", sbo."ColorId"
        ON CONFLICT ("UserId","PartNum","ColorId") DO UPDATE SET "Stock" = "BrickOwned"."Stock" + EXCLUDED."Stock";

        DELETE FROM "SetBrickOwned" sbo
        WHERE sbo."SetId" IN (SELECT set_num FROM stg_sets)
          AND NOT EXISTS (SELECT 1 FROM stg_new_setbrick_keys k
                          WHERE k.set_num = sbo."SetId" AND k.part_num = sbo."PartNum" AND k.color_id = sbo."ColorId");

        DELETE FROM "SetBricks" sb
        WHERE sb."SetId" IN (SELECT set_num FROM stg_sets)
          AND NOT EXISTS (SELECT 1 FROM stg_new_setbrick_keys k
                          WHERE k.set_num = sb."SetId" AND k.part_num = sb."PartNum" AND k.color_id = sb."ColorId");

        -- MINIFIG PARTS: same reconcile, returning stock to loose inventory.
        INSERT INTO "BrickOwned" ("UserId","PartNum","ColorId","Stock")
        SELECT mbo."UserId", mbo."PartNum", mbo."ColorId", SUM(mbo."Stock")
        FROM "MinifigBrickOwned" mbo
        WHERE mbo."Stock" > 0
          AND mbo."MinifigId" IN (SELECT fig_num FROM stg_minifigs)
          AND NOT EXISTS (SELECT 1 FROM stg_new_minifigbrick_keys k
                          WHERE k.fig_num = mbo."MinifigId" AND k.part_num = mbo."PartNum" AND k.color_id = mbo."ColorId")
        GROUP BY mbo."UserId", mbo."PartNum", mbo."ColorId"
        ON CONFLICT ("UserId","PartNum","ColorId") DO UPDATE SET "Stock" = "BrickOwned"."Stock" + EXCLUDED."Stock";

        DELETE FROM "MinifigBrickOwned" mbo
        WHERE mbo."MinifigId" IN (SELECT fig_num FROM stg_minifigs)
          AND NOT EXISTS (SELECT 1 FROM stg_new_minifigbrick_keys k
                          WHERE k.fig_num = mbo."MinifigId" AND k.part_num = mbo."PartNum" AND k.color_id = mbo."ColorId");

        DELETE FROM "MinifigBricks" mb
        WHERE mb."MinifigId" IN (SELECT fig_num FROM stg_minifigs)
          AND NOT EXISTS (SELECT 1 FROM stg_new_minifigbrick_keys k
                          WHERE k.fig_num = mb."MinifigId" AND k.part_num = mb."PartNum" AND k.color_id = mb."ColorId");
        """;

    /// <summary>
    /// Loads the supplied CSV files into the catalog. <paramref name="files"/> is keyed by logical name
    /// (e.g. "inventory_parts"). Always returns a <see cref="CatalogImport"/> record (Success or Failed).
    /// </summary>
    public async Task<CatalogImport> ImportAsync(
        IReadOnlyDictionary<string, Stream> files, DateTime? snapshotDate, CancellationToken ct = default)
    {
        var missing = RequiredFiles.Where(f => !files.ContainsKey(f)).ToList();
        if (missing.Count > 0)
        {
            var note = $"Missing required files: {string.Join(", ", missing)}";
            logger.LogWarning("Bulk import rejected. {Note}", note);
            return await RecordAsync("BulkUpload", "Failed", snapshotDate, note, ct);
        }

        try
        {
            // Snapshot owned sets' BOMs before the load so we can diff and notify owners afterwards.
            var beforeOwned = await SnapshotOwnedSetBricksAsync(ct);

            await using var csCtx = await contextFactory.CreateDbContextAsync(ct);
            var connString = csCtx.Database.GetConnectionString();

            await using var conn = new NpgsqlConnection(connString);
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            await ExecuteAsync(conn, StagingDdl, ct);

            foreach (var name in RequiredFiles)
                await CopyAsync(conn, $"stg_{name}", files[name], ct);

            await ExecuteAsync(conn, StagingIndexes, ct);

            foreach (var sql in Upserts)
                await ExecuteAsync(conn, sql, ct);

            // Delete BOM rows no longer in the snapshot and return owned stock to loose inventory.
            await ExecuteAsync(conn, ReconcileSql, ct);

            await tx.CommitAsync(ct);

            await NotifyOwnersAsync(beforeOwned, ct);

            var counts = await CountsAsync(ct);
            logger.LogInformation("Bulk import complete. {Counts}", counts);
            return await RecordAsync("BulkUpload", "Success", snapshotDate, counts, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Bulk import failed");
            return await RecordAsync("BulkUpload", "Failed", snapshotDate, ex.Message, ct);
        }
    }

    /// <summary>
    /// Snapshots the current SetBrick BOM (regular counts, keyed by part+color) for every set any user owns.
    /// Small — only owned sets — so it's cheap to hold in memory across the import for a before/after diff.
    /// </summary>
    private async Task<Dictionary<string, Dictionary<(string, string), int>>> SnapshotOwnedSetBricksAsync(CancellationToken ct)
    {
        await using var ctx = await contextFactory.CreateDbContextAsync(ct);

        var ownedSetIds = await ctx.Set<SetOwned>().AsNoTracking()
            .Select(so => so.SetId).Distinct().ToListAsync(ct);
        if (ownedSetIds.Count == 0)
            return new();

        var rows = await ctx.Set<SetBrick>().AsNoTracking()
            .Where(sb => ownedSetIds.Contains(sb.SetId))
            .Select(sb => new { sb.SetId, sb.PartNum, sb.ColorId, sb.Count })
            .ToListAsync(ct);

        return rows.GroupBy(r => r.SetId).ToDictionary(
            g => g.Key,
            g => g.ToDictionary(r => (r.PartNum, r.ColorId), r => r.Count));
    }

    /// <summary>
    /// Diffs each owned set's before/after BOM and fans out a notification per owner for the ones that
    /// changed. Best-effort: the catalog import has already committed, so a notification failure is logged
    /// rather than surfaced as an import failure.
    /// </summary>
    private async Task NotifyOwnersAsync(Dictionary<string, Dictionary<(string, string), int>> before, CancellationToken ct)
    {
        try
        {
            var after = await SnapshotOwnedSetBricksAsync(ct);
            var setIds = before.Keys.Union(after.Keys);
            var detectedAt = DateTime.UtcNow;
            var notified = 0;

            foreach (var setId in setIds)
            {
                var b = before.GetValueOrDefault(setId) ?? new();
                var a = after.GetValueOrDefault(setId) ?? new();

                var changes = new List<PartChange>();
                PartDiff.Collect(b, a, changes);
                if (changes.Count == 0)
                    continue;

                await notifications.WriteForSetChangeAsync(setId, changes, detectedAt, ct);
                notified++;
            }

            if (notified > 0)
                logger.LogInformation("Bulk import: wrote update notifications for {Count} changed owned set(s)", notified);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Bulk import: writing owner notifications failed (catalog import already committed)");
        }
    }

    private static async Task ExecuteAsync(NpgsqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.CommandTimeout = 0; // bulk operations can run long
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task CopyAsync(NpgsqlConnection conn, string table, Stream csv, CancellationToken ct)
    {
        await using var importer = await conn.BeginTextImportAsync(
            $"COPY {table} FROM STDIN (FORMAT csv, HEADER true)", ct);
        using var reader = new StreamReader(csv);
        var buffer = new char[81920];
        int n;
        while ((n = await reader.ReadAsync(buffer, ct)) > 0)
            await importer.WriteAsync(buffer.AsMemory(0, n), ct);
    }

    private async Task<string> CountsAsync(CancellationToken ct)
    {
        await using var ctx = await contextFactory.CreateDbContextAsync(ct);
        return $"sets={await ctx.Set<Set>().CountAsync(ct)}, bricks={await ctx.Set<Brick>().CountAsync(ct)}, " +
               $"minifigs={await ctx.Set<Minifig>().CountAsync(ct)}, themes={await ctx.Set<Theme>().CountAsync(ct)}, " +
               $"set-bricks={await ctx.Set<SetBrick>().CountAsync(ct)}, set-minifigs={await ctx.Set<SetMinifig>().CountAsync(ct)}";
    }

    private async Task<CatalogImport> RecordAsync(
        string source, string status, DateTime? snapshotDate, string? notes, CancellationToken ct)
    {
        await using var ctx = await contextFactory.CreateDbContextAsync(ct);
        var record = new CatalogImport
        {
            ImportedAt = DateTime.UtcNow,
            SnapshotDate = snapshotDate,
            Source = source,
            Status = status,
            Notes = notes,
        };
        ctx.Set<CatalogImport>().Add(record);
        await ctx.SaveChangesAsync(ct);
        return record;
    }
}
