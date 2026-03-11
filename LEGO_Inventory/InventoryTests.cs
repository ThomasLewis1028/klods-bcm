using LEGO_Inventory.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LEGO_Inventory;

public class InventoryTests
{

    [TestClass]
    public class RebrickableApiTests
    {
        [TestMethod]
        public void GetSetPartsTest()
        {
            RebrickableApi api = new RebrickableApi();

            var response = api.GetSetParts("4502-1");

            Assert.IsNotNull(response.Result);
        }

        [TestMethod]
        public void GetSetInfoTest()
        {
            RebrickableApi api = new RebrickableApi();

            var response = api.GetSetInfo("4502-1");

            Assert.IsNotNull(response.Result);
        }

        [TestMethod]
        public void GetSetPartInfoTest()
        {
            RebrickableApi api = new RebrickableApi();

            var response = api.GetPartInfo("3001");

            Assert.IsNotNull(response.Result);

            Console.WriteLine(response.Result);
        }
    }

    [TestClass]
    public class ImportTests
    {
        private static int CreateTestUser(InventoryContext context)
        {
            var user = new User { UserName = "test_user", PasswordHash = "test" };
            context.Users.Add(user);
            context.SaveChanges();
            return user.UserId;
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
        public void ImportSetInfoTest()
        {
            var importData = new ImportData();

            Assert.IsTrue(importData.ImportSetInfo("4502-1"));

            using var context = new InventoryContext();
            CleanupBomData(context, "4502-1");
        }

        [TestMethod]
        public void ImportSetPartTest()
        {
            using var context = new InventoryContext();
            var userId = CreateTestUser(context);

            try
            {
                var importData = new ImportData();

                Assert.IsTrue(importData.ImportSetInfo("4502-1"));
                Assert.IsTrue(importData.AddOwnedSet("4502-1", userId));

                var count = context.Set<SetBrick>().Where(sb => sb.SetId == "4502-1").Sum(b => b.Count);
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
}
