using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Klods.Database;

public class InventoryContext : DbContext
{
    public InventoryContext() { }

    public InventoryContext(DbContextOptions<InventoryContext> options) : base(options) { }

    public DbSet<Brick> Bricks { get; set; }
    public DbSet<SetBrick> SetBricks { get; set; }
    public DbSet<SetBrickOwned> SetBrickOwneds { get; set; }
    public DbSet<BrickOwned> BrickOwneds { get; set; }
    public DbSet<Set> Sets { get; set; }
    public DbSet<Minifig> Minifigs { get; set; }
    public DbSet<SetMinifig> SetMinifigs { get; set; }
    public DbSet<MinifigBrick> MinifigBricks { get; set; }
    public DbSet<MinifigOwned> MinifigOwneds { get; set; }
    public DbSet<MinifigBrickOwned> MinifigBrickOwneds { get; set; }
    public DbSet<Color> Colors { get; set; }
    public DbSet<PartCategory> PartCategories { get; set; }
    public DbSet<Theme> Themes { get; set; }
    public DbSet<CatalogImport> CatalogImports { get; set; }
    public DbSet<Setting> Settings { get; set; }
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

        // MINIFIG BRICK (a minifig's parts inventory — mirrors SetBrick)
        modelBuilder.Entity<MinifigBrick>().HasKey(e => new { e.MinifigId, e.PartNum, e.ColorId });

        modelBuilder.Entity<MinifigBrick>()
            .HasOne<Brick>()
            .WithMany()
            .HasForeignKey(s => new { s.PartNum, s.ColorId })
            .IsRequired();

        modelBuilder.Entity<MinifigBrick>()
            .HasOne<Minifig>()
            .WithMany()
            .HasForeignKey(s => s.MinifigId)
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

        // MINIFIG OWNED — one physical fig instance (instanced like SetOwned).
        // PK includes MinifigIndex so a user can own multiple copies of the same fig.
        modelBuilder.Entity<MinifigOwned>().HasKey(e => new { e.UserId, e.MinifigId, e.MinifigIndex });

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

        // Optional link to the owned set copy this fig belongs to (null = loose).
        modelBuilder.Entity<MinifigOwned>()
            .HasOne<SetOwned>()
            .WithMany()
            .HasForeignKey(s => new { s.UserId, s.SetId, s.SetIndex })
            .IsRequired(false);

        // MINIFIG BRICK OWNED — per-instance part stock (mirrors SetBrickOwned)
        modelBuilder.Entity<MinifigBrickOwned>()
            .HasKey(e => new { e.UserId, e.MinifigId, e.MinifigIndex, e.PartNum, e.ColorId });

        modelBuilder.Entity<MinifigBrickOwned>()
            .HasOne<MinifigOwned>()
            .WithMany()
            .HasForeignKey(s => new { s.UserId, s.MinifigId, s.MinifigIndex })
            .IsRequired();

        modelBuilder.Entity<MinifigBrickOwned>()
            .HasOne<MinifigBrick>()
            .WithMany()
            .HasForeignKey(s => new { s.MinifigId, s.PartNum, s.ColorId })
            .IsRequired();

        // COLOR
        modelBuilder.Entity<Color>().HasKey(e => new { e.Id });

        // PART CATEGORY (reference table, synced like Color)
        modelBuilder.Entity<PartCategory>().HasKey(e => e.Id);
        modelBuilder.Entity<PartCategory>().Property(e => e.Id).ValueGeneratedNever();

        // THEME (reference table; Id is Rebrickable's, ParentId is a soft self-reference — no FK)
        modelBuilder.Entity<Theme>().HasKey(e => e.Id);
        modelBuilder.Entity<Theme>().Property(e => e.Id).ValueGeneratedNever();

        // SETTING (key/value app settings)
        modelBuilder.Entity<Setting>().HasKey(e => e.Key);

        // USER — unique index on UserName for login/uniqueness-check lookups
        modelBuilder.Entity<User>()
            .HasIndex(e => e.UserName)
            .IsUnique();

        modelBuilder.Entity<User>()
            .Property(e => e.Role)
            .HasDefaultValue("User");

        modelBuilder.Entity<User>()
            .Property(e => e.Status)
            .HasDefaultValue("Active");

        modelBuilder.Entity<User>()
            .Property(e => e.FontScale)
            .HasDefaultValue(1.0);

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
        if (optionsBuilder.IsConfigured)
            return;

        var host = Environment.GetEnvironmentVariable("POSTGRES_HOST")
            ?? throw new InvalidOperationException("Required environment variable POSTGRES_HOST is not set.");
        var user = Environment.GetEnvironmentVariable("POSTGRES_USER")
            ?? throw new InvalidOperationException("Required environment variable POSTGRES_USER is not set.");
        var password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD")
            ?? throw new InvalidOperationException("Required environment variable POSTGRES_PASSWORD is not set.");
        var db = Environment.GetEnvironmentVariable("POSTGRES_DB")
            ?? throw new InvalidOperationException("Required environment variable POSTGRES_DB is not set.");
        var connectionString = $"Host={host};Database={db};Username={user};Password={password};Pooling=true;MaxPoolSize=100;";
        optionsBuilder.UseNpgsql(connectionString);
        optionsBuilder.ConfigureWarnings(warnings =>
            warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
    }

}
