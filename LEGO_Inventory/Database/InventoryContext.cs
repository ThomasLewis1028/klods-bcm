using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LEGO_Inventory.Database;

public class InventoryContext : DbContext
{
    public DbSet<Brick> Bricks { get; set; }
    public DbSet<SetBrick> SetBricks { get; set; }
    public DbSet<Set> Sets { get; set; }
    public DbSet<Minifig> Minifigs { get; set; }
    public DbSet<SetMinifig> SetMinifigs { get; set; }
    public DbSet<MinifigBrick> MinifigBricks { get; set; }

    public InventoryContext()
    {
        
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
    
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var POSTGRES_HOST = Environment.GetEnvironmentVariable("POSTGRES_HOST");
        var POSTGRES_USER = Environment.GetEnvironmentVariable("POSTGRES_USER");
        var POSTGRES_PASSWORD = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD");
        var POSTGRES_DB = Environment.GetEnvironmentVariable("POSTGRES_DB");
        var connectionString = $"Host={POSTGRES_HOST};Database={POSTGRES_DB};Username={POSTGRES_USER};Password={POSTGRES_PASSWORD}";
        optionsBuilder.UseNpgsql(connectionString);
        optionsBuilder.ConfigureWarnings(warnings =>
            warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
    }
    
}