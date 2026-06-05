using LEGO_Inventory.Database;
using LEGO_Inventory.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LEGO_Inventory.Tests;

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
        return new ImageStorageService(
            provider.GetRequiredService<IHttpClientFactory>(),
            new ConfigurationBuilder().Build(),
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
}
