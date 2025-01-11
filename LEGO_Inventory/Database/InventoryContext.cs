using Microsoft.EntityFrameworkCore;

namespace LEGO_Inventory.Database;

public class InventoryContext : DbContext
{
    public DbSet<Brick> Bricks { get; set; }
    public DbSet<SetBrick> SetBricks { get; set; }
    public DbSet<Set> Sets { get; set; }
    public DbSet<Minifig> Minifigs { get; set; }
    public DbSet<SetMinifig> SetMinifigs { get; set; }
    public DbSet<MinifigBrick> MinifigBricks { get; set; }

    public string DbPath { get; }


    public InventoryContext()
    {
        var folder = Environment.SpecialFolder.LocalApplicationData;
        var path = Environment.GetFolderPath(folder);
        DbPath = System.IO.Path.Join(path, "LegoInventory.db");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // BRICK
        modelBuilder.Entity<Brick>().HasKey(e => new { e.PartNum, e.ColorId });

        // SET
        modelBuilder.Entity<Set>().HasKey(e => new { e.SetId });

        // SET BRICK
        modelBuilder.Entity<SetBrick>().HasKey(e => new { e.PartNum, e.ColorId, e.SetId });

        modelBuilder.Entity<SetBrick>()
            .HasOne<Brick>()
            .WithMany()
            .HasForeignKey(s => new { s.PartNum, s.ColorId })
            .IsRequired();

        modelBuilder.Entity<SetBrick>()
            .HasOne<Set>()
            .WithMany()
            .HasForeignKey(s => s.SetId)
            .IsRequired();

        // MINIFIG
        modelBuilder.Entity<Minifig>().HasKey(e => new { e.MinifigId });

        // SET MINIFIG
        modelBuilder.Entity<SetMinifig>().HasKey(e => new { e.MinifigId, e.SetId });

        modelBuilder.Entity<SetMinifig>()
            .HasOne<Minifig>()
            .WithMany()
            .HasForeignKey(s => s.MinifigId)
            .IsRequired();

        modelBuilder.Entity<SetMinifig>()
            .HasOne<Set>()
            .WithMany()
            .HasForeignKey(s => s.SetId)
            .IsRequired();

        // MINIFIG BRICK
        modelBuilder.Entity<MinifigBrick>().HasKey(e => new { e.MinifigID, e.BrickID, e.ColorId });

        modelBuilder.Entity<MinifigBrick>()
            .HasOne<Brick>()
            .WithMany()
            .HasForeignKey(s => new { s.BrickID, s.ColorId })
            .IsRequired();

        modelBuilder.Entity<MinifigBrick>()
            .HasOne<Minifig>()
            .WithMany()
            .HasForeignKey(s => s.MinifigID)
            .IsRequired();
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options
            .UseSqlite($"Data Source={DbPath}");
}