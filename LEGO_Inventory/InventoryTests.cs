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
        [TestMethod]
        public void ImportSetInfoTest()
        {
            var ImportData = new ImportData();
            
            Assert.IsTrue(ImportData.ImportSetInfo("4502-1"));

            var context = new InventoryContext();
            var setBrickContext = context.Set<SetBrick>();
            var setContext = context.Set<Set>();
            var brickContext = context.Set<Brick>();

            setBrickContext.ExecuteDelete();
            brickContext.ExecuteDelete();
            setContext.ExecuteDelete();
            
            context.SaveChanges();
        }
        
        [TestMethod]
        public void ImportSetPartTest()
        {
            var importData = new ImportData();
            
            Assert.IsTrue(importData.ImportSetInfo("4502-1"));
            Assert.IsTrue(importData.AddOwnedSet("4502-1"));
            
            var context = new InventoryContext();
            var setBrickContext = context.Set<SetBrick>();
            var setContext = context.Set<Set>();
            var brickContext = context.Set<Brick>();

            var count = setBrickContext.Where(sb => sb.SetId == "4502-1").Sum(b => b.Count);
            var setCount = setContext.First(sb => sb.SetId == "4502-1").NumBricks;
            
            Assert.AreEqual(count, setCount);
            
            setBrickContext.ExecuteDelete();
            brickContext.ExecuteDelete();
            setContext.ExecuteDelete();

            context.SaveChanges();

        }
    }
}