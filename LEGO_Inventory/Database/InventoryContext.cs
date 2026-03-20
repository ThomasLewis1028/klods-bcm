using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LEGO_Inventory.Database;

public class InventoryContext : DbContext
{
    public DbSet<Brick> Bricks { get; set; }
    public DbSet<SetBrick> SetBricks { get; set; }
    public DbSet<SetBrickOwned> SetBrickOwneds { get; set; }
    public DbSet<BrickOwned> BrickOwneds { get; set; }
    public DbSet<Set> Sets { get; set; }
    public DbSet<Minifig> Minifigs { get; set; }
    public DbSet<SetMinifig> SetMinifigs { get; set; }
    public DbSet<MinifigBrick> MinifigBricks { get; set; }
    public DbSet<MinifigOwned> MinifigOwneds { get; set; }
    public DbSet<Color> Colors { get; set; }
    public DbSet<SetOwned> SetsOwned { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<UserExternalLogin> UserExternalLogins { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // BRICK
        modelBuilder.Entity<Brick>().HasKey(e => new { e.PartNum, e.ColorId });

        // SET
        modelBuilder.Entity<Set>().HasKey(e => new { e.SetId });

        // OWNED SET — PK includes UserId so SetIndex is per-user
        modelBuilder.Entity<SetOwned>().HasKey(e => new { e.UserId, e.SetId, e.SetIndex });

        modelBuilder.Entity<SetOwned>()
            .HasOne<Set>()
            .WithMany()
            .HasForeignKey(s => s.SetId)
            .IsRequired();

        modelBuilder.Entity<SetOwned>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .IsRequired();

        // SET BRICK (BOM — one row per set/part/color, no per-instance data)
        modelBuilder.Entity<SetBrick>().HasKey(e => new { e.SetId, e.PartNum, e.ColorId });

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

        // SET BRICK OWNED — PK includes UserId; FK to SetsOwned uses (UserId, SetId, SetIndex)
        modelBuilder.Entity<SetBrickOwned>().HasKey(e => new { e.UserId, e.SetId, e.SetIndex, e.PartNum, e.ColorId });

        modelBuilder.Entity<SetBrickOwned>()
            .HasOne<SetBrick>()
            .WithMany()
            .HasForeignKey(s => new { s.SetId, s.PartNum, s.ColorId })
            .IsRequired();

        modelBuilder.Entity<SetBrickOwned>()
            .HasOne<SetOwned>()
            .WithMany()
            .HasForeignKey(s => new { s.UserId, s.SetId, s.SetIndex })
            .IsRequired();

        // SET MINIFIG (BOM — one row per set/minifig)
        modelBuilder.Entity<SetMinifig>().HasKey(e => new { e.SetId, e.MinifigId });

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

        // MINIFIG
        modelBuilder.Entity<Minifig>().HasKey(e => new { e.MinifigId });

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

        // BRICK OWNED (user's loose brick stock)
        modelBuilder.Entity<BrickOwned>().HasKey(e => new { e.UserId, e.PartNum, e.ColorId });

        modelBuilder.Entity<BrickOwned>()
            .HasOne<Brick>()
            .WithMany()
            .HasForeignKey(s => new { s.PartNum, s.ColorId })
            .IsRequired();

        modelBuilder.Entity<BrickOwned>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .IsRequired();

        // MINIFIG OWNED (user's minifig stock)
        modelBuilder.Entity<MinifigOwned>().HasKey(e => new { e.UserId, e.MinifigId });

        modelBuilder.Entity<MinifigOwned>()
            .HasOne<Minifig>()
            .WithMany()
            .HasForeignKey(s => s.MinifigId)
            .IsRequired();

        modelBuilder.Entity<MinifigOwned>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .IsRequired();

        // COLOR
        modelBuilder.Entity<Color>().HasKey(e => new { e.Id });

        // USER EXTERNAL LOGIN
        modelBuilder.Entity<UserExternalLogin>().HasKey(e => new { e.Provider, e.ProviderKey });
        modelBuilder.Entity<UserExternalLogin>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .IsRequired();
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var POSTGRES_HOST = Environment.GetEnvironmentVariable("POSTGRES_HOST");
        var POSTGRES_USER = Environment.GetEnvironmentVariable("POSTGRES_USER");
        var POSTGRES_PASSWORD = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD");
        var POSTGRES_DB = Environment.GetEnvironmentVariable("POSTGRES_DB");
        var connectionString = $"Host={POSTGRES_HOST};Database={POSTGRES_DB};Username={POSTGRES_USER};Password={POSTGRES_PASSWORD};Pooling=true;MaxPoolSize=100;";
        optionsBuilder.UseNpgsql(connectionString);
        optionsBuilder.ConfigureWarnings(warnings =>
            warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
    }

}
