using System.Text.Json.Nodes;
using Klods.Database;
using Klods.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Klods.Tests;

[TestClass]
public class RebrickableApiTests
{
    [TestMethod]
    public async Task GetSetPartsTest()
    {
        var api = new RebrickableApi();
        var response = await api.GetSetParts("4502-1");
        Assert.IsNotNull(response);
    }

    [TestMethod]
    public async Task GetSetInfoTest()
    {
        var api = new RebrickableApi();
        var response = await api.GetSetInfo("4502-1");
        Assert.IsNotNull(response);
    }

    [TestMethod]
    public async Task GetMinifigPartsTest()
    {
        var api = new RebrickableApi();
        var response = await api.GetMinifigParts("fig-001162");
        Assert.IsNotNull(response);
    }

    [TestMethod]
    public async Task GetSetsModifiedSinceRespectsWatermark()
    {
        var api = new RebrickableApi();

        // A few days back sits well inside one 1000-row page (~65 days of churn), so this is one call.
        var since = DateTime.UtcNow.AddDays(-3);
        var recent = await api.GetSetsModifiedSince(since);
        Assert.IsTrue(recent.All(r => r.LastModified > since),
            "every returned set must be modified strictly after the watermark");

        // A watermark in the future crosses immediately and yields nothing.
        var none = await api.GetSetsModifiedSince(DateTime.UtcNow.AddYears(1));
        Assert.AreEqual(0, none.Count);
    }
}

[TestClass]
public class ImportTests
{
    private static IDbContextFactory<InventoryContext> CreateFactory()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<InventoryContext>();
        return services.BuildServiceProvider().GetRequiredService<IDbContextFactory<InventoryContext>>();
    }

    private static ImageStorageService CreateImageStorage()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        var provider = services.BuildServiceProvider();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MINIO_ROOT_USER"]     = "minioadmin",
                ["MINIO_ROOT_PASSWORD"] = "minioadmin",
            })
            .Build();
        return new ImageStorageService(
            provider.GetRequiredService<IHttpClientFactory>(),
            config,
            NullLogger<ImageStorageService>.Instance);
    }

    private static void CleanupBomData(InventoryContext context, string setId)
    {
        context.Set<SetBrick>().Where(sb => sb.SetId == setId).ExecuteDelete();
        context.Set<Set>().Where(s => s.SetId == setId).ExecuteDelete();
        context.SaveChanges();
    }

    private static void CleanupTestUser(InventoryContext context, int userId)
    {
        context.Set<SetBrickOwned>().Where(sbo => sbo.UserId == userId).ExecuteDelete();
        context.Set<SetOwned>().Where(so => so.UserId == userId).ExecuteDelete();
        context.Users.Where(u => u.UserId == userId).ExecuteDelete();
        context.SaveChanges();
    }

    [TestMethod]
    public async Task ImportSetInfoTest()
    {
        var factory = CreateFactory();
        var importData = new ImportData(factory, NullLogger<ImportData>.Instance, CreateImageStorage(), new RebrickableApi());

        Assert.IsTrue(await importData.ImportSetInfo("4502-1"));

        await using var context = factory.CreateDbContext();
        CleanupBomData(context, "4502-1");
    }

    [TestMethod]
    public async Task ImportSetPartTest()
    {
        var factory = CreateFactory();
        await using var context = factory.CreateDbContext();
        var user = new User { UserName = "test_user", PasswordHash = "test" };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var userId = user.UserId;

        try
        {
            var importData = new ImportData(factory, NullLogger<ImportData>.Instance, CreateImageStorage(), new RebrickableApi());

            Assert.IsTrue(await importData.ImportSetInfo("4502-1"));
            Assert.IsTrue(await importData.AddOwnedSet("4502-1", userId));

            var count    = context.Set<SetBrick>().Where(sb => sb.SetId == "4502-1").Sum(b => b.Count);
            var setCount = context.Set<Set>().First(sb => sb.SetId == "4502-1").NumBricks;

            Assert.AreEqual(count, setCount);
        }
        finally
        {
            CleanupTestUser(context, userId);
            CleanupBomData(context, "4502-1");
        }
    }

    // Re-importing a set whose upstream BOM dropped a part should delete the stale BOM row and
    // return any stock a user had placed against it to their loose inventory. Seeds the DB directly
    // and passes a hand-built parts list, so it needs no network — only a reachable database.
    [TestMethod]
    public async Task ImportSetBomReturnsStockForRemovedPart()
    {
        const string setId = "test-recon-1";
        const string keepPart = "recon-keep";
        const string dropPart = "recon-drop";
        const string colorId = "0";

        var factory = CreateFactory();
        await using var context = factory.CreateDbContext();

        var user = new User { UserName = "recon_user", PasswordHash = "test" };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var userId = user.UserId;

        try
        {
            context.Set<Set>().Add(new Set
            {
                SetId = setId, Name = "Recon Test", NumBricks = 2, ReleaseYear = 2000,
                DateModified = DateTime.UnixEpoch, ManualUrl = "",
            });
            context.Set<Brick>().Add(new Brick { PartNum = keepPart, Name = "Keep", ColorId = colorId });
            context.Set<Brick>().Add(new Brick { PartNum = dropPart, Name = "Drop", ColorId = colorId });
            context.Set<SetBrick>().Add(new SetBrick { SetId = setId, PartNum = keepPart, ColorId = colorId, Count = 1 });
            context.Set<SetBrick>().Add(new SetBrick { SetId = setId, PartNum = dropPart, ColorId = colorId, Count = 1 });
            await context.SaveChangesAsync();

            // The user owns a copy of the set with 3 of the soon-to-be-removed part placed on it.
            context.Set<SetOwned>().Add(new SetOwned { UserId = userId, SetId = setId, SetIndex = 0 });
            await context.SaveChangesAsync();
            context.Set<SetBrickOwned>().Add(new SetBrickOwned
            {
                UserId = userId, SetId = setId, SetIndex = 0, PartNum = dropPart, ColorId = colorId, Stock = 3,
            });
            await context.SaveChangesAsync();

            // Fresh upstream BOM now contains only the surviving part.
            var liveParts = new JsonArray
            {
                new JsonObject
                {
                    ["part"] = new JsonObject { ["part_num"] = keepPart },
                    ["color"] = new JsonObject { ["id"] = colorId },
                    ["is_spare"] = "false",
                    ["quantity"] = 1,
                },
            };

            var importData = new ImportData(factory, NullLogger<ImportData>.Instance, CreateImageStorage(), new RebrickableApi());
            await importData.ImportSetBOM(setId, liveParts);

            await using var verify = factory.CreateDbContext();
            Assert.IsTrue(verify.Set<SetBrick>().Any(sb => sb.SetId == setId && sb.PartNum == keepPart),
                "surviving BOM row should remain");
            Assert.IsFalse(verify.Set<SetBrick>().Any(sb => sb.SetId == setId && sb.PartNum == dropPart),
                "removed BOM row should be deleted");
            Assert.IsFalse(verify.Set<SetBrickOwned>().Any(sbo => sbo.SetId == setId && sbo.PartNum == dropPart),
                "owned row for the removed part should be deleted");

            var loose = verify.Set<BrickOwned>()
                .FirstOrDefault(bo => bo.UserId == userId && bo.PartNum == dropPart && bo.ColorId == colorId);
            Assert.IsNotNull(loose, "placed stock should be returned to loose inventory");
            Assert.AreEqual(3, loose.Stock);
        }
        finally
        {
            context.Set<BrickOwned>().Where(bo => bo.UserId == userId).ExecuteDelete();
            context.Set<SetBrickOwned>().Where(sbo => sbo.UserId == userId).ExecuteDelete();
            context.Set<SetOwned>().Where(so => so.UserId == userId).ExecuteDelete();
            context.Set<SetBrick>().Where(sb => sb.SetId == setId).ExecuteDelete();
            context.Set<Brick>().Where(b => b.PartNum == keepPart || b.PartNum == dropPart).ExecuteDelete();
            context.Set<Set>().Where(s => s.SetId == setId).ExecuteDelete();
            context.Users.Where(u => u.UserId == userId).ExecuteDelete();
            context.SaveChanges();
        }
    }

    // Minifig-parts twin of the set-BOM test: re-linking a fig whose upstream inventory dropped a part
    // should delete the stale MinifigBrick row and return placed per-instance stock to loose inventory.
    [TestMethod]
    public async Task LinkMinifigBricksReturnsStockForRemovedPart()
    {
        const string minifigId = "fig-recon-1";
        const string keepPart = "mfig-keep";
        const string dropPart = "mfig-drop";
        const string colorId = "0";

        var factory = CreateFactory();
        await using var context = factory.CreateDbContext();

        var user = new User { UserName = "mfig_recon_user", PasswordHash = "test" };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var userId = user.UserId;

        try
        {
            context.Set<Minifig>().Add(new Minifig
            {
                MinifigId = minifigId, Name = "Recon Fig", NumParts = 2, DateModified = DateTime.UnixEpoch,
            });
            context.Set<Brick>().Add(new Brick { PartNum = keepPart, Name = "Keep", ColorId = colorId });
            context.Set<Brick>().Add(new Brick { PartNum = dropPart, Name = "Drop", ColorId = colorId });
            context.Set<MinifigBrick>().Add(new MinifigBrick { MinifigId = minifigId, PartNum = keepPart, ColorId = colorId, Count = 1 });
            context.Set<MinifigBrick>().Add(new MinifigBrick { MinifigId = minifigId, PartNum = dropPart, ColorId = colorId, Count = 1 });
            await context.SaveChangesAsync();

            // The user owns an instance of the fig with 2 of the soon-to-be-removed part placed on it.
            context.Set<MinifigOwned>().Add(new MinifigOwned { UserId = userId, MinifigId = minifigId, MinifigIndex = 0 });
            await context.SaveChangesAsync();
            context.Set<MinifigBrickOwned>().Add(new MinifigBrickOwned
            {
                UserId = userId, MinifigId = minifigId, MinifigIndex = 0, PartNum = dropPart, ColorId = colorId, Stock = 2,
            });
            await context.SaveChangesAsync();

            // Fresh upstream inventory now contains only the surviving part.
            var liveParts = new JsonArray
            {
                new JsonObject
                {
                    ["part"] = new JsonObject { ["part_num"] = keepPart },
                    ["color"] = new JsonObject { ["id"] = colorId },
                    ["is_spare"] = "false",
                    ["quantity"] = 1,
                },
            };

            var importData = new ImportData(factory, NullLogger<ImportData>.Instance, CreateImageStorage(), new RebrickableApi());
            await importData.LinkMinifigBricks(minifigId, liveParts);

            await using var verify = factory.CreateDbContext();
            Assert.IsTrue(verify.Set<MinifigBrick>().Any(mb => mb.MinifigId == minifigId && mb.PartNum == keepPart),
                "surviving part row should remain");
            Assert.IsFalse(verify.Set<MinifigBrick>().Any(mb => mb.MinifigId == minifigId && mb.PartNum == dropPart),
                "removed part row should be deleted");
            Assert.IsFalse(verify.Set<MinifigBrickOwned>().Any(mbo => mbo.MinifigId == minifigId && mbo.PartNum == dropPart),
                "owned row for the removed part should be deleted");

            var loose = verify.Set<BrickOwned>()
                .FirstOrDefault(bo => bo.UserId == userId && bo.PartNum == dropPart && bo.ColorId == colorId);
            Assert.IsNotNull(loose, "placed stock should be returned to loose inventory");
            Assert.AreEqual(2, loose.Stock);
        }
        finally
        {
            context.Set<BrickOwned>().Where(bo => bo.UserId == userId).ExecuteDelete();
            context.Set<MinifigBrickOwned>().Where(mbo => mbo.UserId == userId).ExecuteDelete();
            context.Set<MinifigOwned>().Where(mo => mo.UserId == userId).ExecuteDelete();
            context.Set<MinifigBrick>().Where(mb => mb.MinifigId == minifigId).ExecuteDelete();
            context.Set<Brick>().Where(b => b.PartNum == keepPart || b.PartNum == dropPart).ExecuteDelete();
            context.Set<Minifig>().Where(m => m.MinifigId == minifigId).ExecuteDelete();
            context.Users.Where(u => u.UserId == userId).ExecuteDelete();
            context.SaveChanges();
        }
    }

    private static JsonObject Part(string partNum, string colorId, int qty) => new()
    {
        ["part"] = new JsonObject { ["part_num"] = partNum },
        ["color"] = new JsonObject { ["id"] = colorId },
        ["is_spare"] = "false",
        ["quantity"] = qty,
    };

    // Re-importing a set with a changed BOM should surface Added / Removed / QtyChanged entries.
    [TestMethod]
    public async Task ImportSetBomEmitsPartDiff()
    {
        const string setId = "test-diff-1";
        const string colorId = "0";
        const string keep = "diff-keep";   // qty 1 -> 3
        const string drop = "diff-drop";   // removed
        const string add = "diff-add";     // added

        var factory = CreateFactory();
        await using var context = factory.CreateDbContext();

        try
        {
            context.Set<Set>().Add(new Set
            {
                SetId = setId, Name = "Diff Test", NumBricks = 3, ReleaseYear = 2000,
                DateModified = DateTime.UnixEpoch, ManualUrl = "",
            });
            context.Set<Brick>().Add(new Brick { PartNum = keep, Name = "Keep", ColorId = colorId });
            context.Set<Brick>().Add(new Brick { PartNum = drop, Name = "Drop", ColorId = colorId });
            context.Set<Brick>().Add(new Brick { PartNum = add, Name = "Add", ColorId = colorId });
            context.Set<SetBrick>().Add(new SetBrick { SetId = setId, PartNum = keep, ColorId = colorId, Count = 1 });
            context.Set<SetBrick>().Add(new SetBrick { SetId = setId, PartNum = drop, ColorId = colorId, Count = 2 });
            await context.SaveChangesAsync();

            var newParts = new JsonArray { Part(keep, colorId, 3), Part(add, colorId, 1) }; // drop is gone
            var changes = new List<PartChange>();

            var importData = new ImportData(factory, NullLogger<ImportData>.Instance, CreateImageStorage(), new RebrickableApi());
            await importData.ImportSetBOM(setId, newParts, changes);

            Assert.AreEqual(3, changes.Count, "one entry each for changed / added / removed");
            Assert.AreEqual(1, changes.Count(c => c.Kind == PartChangeKind.QtyChanged && c.PartNum == keep && c.OldCount == 1 && c.NewCount == 3));
            Assert.AreEqual(1, changes.Count(c => c.Kind == PartChangeKind.Added && c.PartNum == add && c.OldCount == 0 && c.NewCount == 1));
            Assert.AreEqual(1, changes.Count(c => c.Kind == PartChangeKind.Removed && c.PartNum == drop && c.OldCount == 2 && c.NewCount == 0));
        }
        finally
        {
            context.Set<SetBrick>().Where(sb => sb.SetId == setId).ExecuteDelete();
            context.Set<Brick>().Where(b => b.PartNum == keep || b.PartNum == drop || b.PartNum == add).ExecuteDelete();
            context.Set<Set>().Where(s => s.SetId == setId).ExecuteDelete();
            context.SaveChanges();
        }
    }

    // A recorded set change should fan out one notification (with items) to each current owner.
    [TestMethod]
    public async Task NotificationServiceFansOutToOwners()
    {
        const string setId = "test-notif-1";

        var factory = CreateFactory();
        await using var context = factory.CreateDbContext();

        var user = new User { UserName = "notif_user", PasswordHash = "test" };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var userId = user.UserId;

        try
        {
            context.Set<Set>().Add(new Set
            {
                SetId = setId, Name = "Notif Test", NumBricks = 1, ReleaseYear = 2000,
                DateModified = DateTime.UnixEpoch, ManualUrl = "",
            });
            await context.SaveChangesAsync();
            context.Set<SetOwned>().Add(new SetOwned { UserId = userId, SetId = setId, SetIndex = 0 });
            await context.SaveChangesAsync();

            var svc = new NotificationService(factory, NullLogger<NotificationService>.Instance);
            var changes = new List<PartChange> { new("notif-part", "0", PartChangeKind.Removed, 2, 0) };
            await svc.WriteForSetChangeAsync(setId, changes, DateTime.UtcNow);

            await using var verify = factory.CreateDbContext();
            var notif = verify.Set<SetUpdateNotification>().Include(n => n.Items)
                .FirstOrDefault(n => n.UserId == userId && n.SetId == setId);
            Assert.IsNotNull(notif, "owner should receive a notification");
            Assert.IsNull(notif.ReadAt, "new notification starts unread");
            Assert.AreEqual(1, notif.Items.Count);
            Assert.AreEqual("Removed", notif.Items[0].ChangeKind);
        }
        finally
        {
            context.Set<SetUpdateNotification>().Where(n => n.UserId == userId).ExecuteDelete(); // items cascade
            context.Set<SetOwned>().Where(so => so.UserId == userId).ExecuteDelete();
            context.Set<Set>().Where(s => s.SetId == setId).ExecuteDelete();
            context.Users.Where(u => u.UserId == userId).ExecuteDelete();
            context.SaveChanges();
        }
    }
}

// Substitution-aware completeness: a fill adds to a requirement's Have after exact parts, and its share
// is reported as HaveSubstituted. Seeds directly against a real DB — no network.
[TestClass]
public class SetCompletenessSubstitutionTests
{
    private const string ColorId = "0";

    private static IDbContextFactory<InventoryContext> CreateFactory()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<InventoryContext>();
        return services.BuildServiceProvider().GetRequiredService<IDbContextFactory<InventoryContext>>();
    }

    // Seeds a set that needs `need` of reqPart plus a substitute brick, and one owned copy (index 0).
    private static async Task<int> Seed(InventoryContext context, string setId, string reqPart, string subPart, int need)
    {
        var user = new User { UserName = $"subcomp_{setId}", PasswordHash = "test" };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        context.Set<Set>().Add(new Set
        {
            SetId = setId, Name = "Sub Test", NumBricks = need, ReleaseYear = 2000,
            DateModified = DateTime.UnixEpoch, ManualUrl = "",
        });
        context.Set<Brick>().Add(new Brick { PartNum = reqPart, Name = "Req", ColorId = ColorId });
        if (subPart != reqPart)
            context.Set<Brick>().Add(new Brick { PartNum = subPart, Name = "Sub", ColorId = ColorId });
        context.Set<SetBrick>().Add(new SetBrick { SetId = setId, PartNum = reqPart, ColorId = ColorId, Count = need });
        await context.SaveChangesAsync();

        context.Set<SetOwned>().Add(new SetOwned { UserId = user.UserId, SetId = setId, SetIndex = 0 });
        await context.SaveChangesAsync();
        return user.UserId;
    }

    private static async Task Cleanup(InventoryContext context, string setId, int userId, params string[] parts)
    {
        context.Set<SetBrickSubstitution>().Where(s => s.UserId == userId).ExecuteDelete();
        context.Set<SetBrickOwned>().Where(sbo => sbo.UserId == userId).ExecuteDelete();
        context.Set<SetOwned>().Where(so => so.UserId == userId).ExecuteDelete();
        context.Set<SetBrick>().Where(sb => sb.SetId == setId).ExecuteDelete();
        context.Set<Brick>().Where(b => parts.Contains(b.PartNum)).ExecuteDelete();
        context.Set<Set>().Where(s => s.SetId == setId).ExecuteDelete();
        context.Users.Where(u => u.UserId == userId).ExecuteDelete();
        await context.SaveChangesAsync();
    }

    private static void AddSub(InventoryContext context, int userId, string setId, string reqPart, string subPart, int count) =>
        context.Set<SetBrickSubstitution>().Add(new SetBrickSubstitution
        {
            UserId = userId, SetId = setId, SetIndex = 0,
            ReqPartNum = reqPart, ReqColorId = ColorId,
            SubPartNum = subPart, SubColorId = ColorId, Count = count,
        });

    // A full substitution fill completes the copy; the whole Have is reported as substituted.
    [TestMethod]
    public async Task SubstitutionCompletesShortfall()
    {
        const string setId = "sub-complete"; const string req = "sc-req"; const string sub = "sc-sub";
        var factory = CreateFactory();
        await using var context = factory.CreateDbContext();
        var userId = await Seed(context, setId, req, sub, need: 4);
        try
        {
            AddSub(context, userId, setId, req, sub, 4);
            await context.SaveChangesAsync();

            var r = (await SetCompleteness.ComputeAsync(context, userId, new[] { (setId, 0) }))[(setId, 0)];
            Assert.AreEqual(SetCompleteness.Status.Complete, r.Status);
            Assert.AreEqual(100, r.Percent);
            Assert.AreEqual(4, r.Have);
            Assert.AreEqual(4, r.HaveSubstituted);
            Assert.AreEqual(100, r.SubstitutedPercent);
        }
        finally { await Cleanup(context, setId, userId, req, sub); }
    }

    // Two partial fills sum toward the same requirement.
    [TestMethod]
    public async Task MultiplePartialFillsSum()
    {
        const string setId = "sub-multi"; const string req = "sm-req"; const string sub = "sm-sub";
        var factory = CreateFactory();
        await using var context = factory.CreateDbContext();
        var userId = await Seed(context, setId, req, sub, need: 4);
        try
        {
            AddSub(context, userId, setId, req, sub, 2);
            AddSub(context, userId, setId, req, sub, 1);
            await context.SaveChangesAsync();

            var r = (await SetCompleteness.ComputeAsync(context, userId, new[] { (setId, 0) }))[(setId, 0)];
            Assert.AreEqual(3, r.Have);
            Assert.AreEqual(3, r.HaveSubstituted);
            Assert.AreEqual(1, r.Missing);
            Assert.AreEqual(75, r.Percent);
        }
        finally { await Cleanup(context, setId, userId, req, sub); }
    }

    // Declaring more than needed caps at the requirement.
    [TestMethod]
    public async Task OverfillCapsAtNeed()
    {
        const string setId = "sub-over"; const string req = "so-req"; const string sub = "so-sub";
        var factory = CreateFactory();
        await using var context = factory.CreateDbContext();
        var userId = await Seed(context, setId, req, sub, need: 2);
        try
        {
            AddSub(context, userId, setId, req, sub, 5);
            await context.SaveChangesAsync();

            var r = (await SetCompleteness.ComputeAsync(context, userId, new[] { (setId, 0) }))[(setId, 0)];
            Assert.AreEqual(SetCompleteness.Status.Complete, r.Status);
            Assert.AreEqual(2, r.Have);
            Assert.AreEqual(2, r.HaveSubstituted);
        }
        finally { await Cleanup(context, setId, userId, req, sub); }
    }

    // Exact parts count first: a substitution for a requirement already satisfied by real parts adds nothing.
    [TestMethod]
    public async Task ExactPartsCountedFirst()
    {
        const string setId = "sub-exact"; const string req = "se-req"; const string sub = "se-sub";
        var factory = CreateFactory();
        await using var context = factory.CreateDbContext();
        var userId = await Seed(context, setId, req, sub, need: 2);
        try
        {
            context.Set<SetBrickOwned>().Add(new SetBrickOwned
            {
                UserId = userId, SetId = setId, SetIndex = 0, PartNum = req, ColorId = ColorId, Stock = 2,
            });
            AddSub(context, userId, setId, req, sub, 2); // redundant — real parts already cover the need
            await context.SaveChangesAsync();

            var r = (await SetCompleteness.ComputeAsync(context, userId, new[] { (setId, 0) }))[(setId, 0)];
            Assert.AreEqual(SetCompleteness.Status.Complete, r.Status);
            Assert.AreEqual(2, r.Have);
            Assert.AreEqual(0, r.HaveSubstituted); // the redundant substitution never inflated the total
        }
        finally { await Cleanup(context, setId, userId, req, sub); }
    }

    // A cross-mold substitute (different PartNum) counts toward the requirement just like a same-mold one.
    [TestMethod]
    public async Task CrossMoldSubstituteCounts()
    {
        const string setId = "sub-cross"; const string req = "cx-req"; const string sub = "cx-sub-other-mold";
        var factory = CreateFactory();
        await using var context = factory.CreateDbContext();
        var userId = await Seed(context, setId, req, sub, need: 1);
        try
        {
            AddSub(context, userId, setId, req, sub, 1);
            await context.SaveChangesAsync();

            var r = (await SetCompleteness.ComputeAsync(context, userId, new[] { (setId, 0) }))[(setId, 0)];
            Assert.AreEqual(SetCompleteness.Status.Complete, r.Status);
            Assert.AreEqual(1, r.HaveSubstituted);
        }
        finally { await Cleanup(context, setId, userId, req, sub); }
    }
}
