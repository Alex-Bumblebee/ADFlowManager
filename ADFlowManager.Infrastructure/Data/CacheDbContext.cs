using ADFlowManager.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ADFlowManager.Infrastructure.Data;

public class CacheDbContext : DbContext
{
    private readonly string _dbPath;

    public DbSet<CachedUser> CachedUsers { get; set; }
    public DbSet<CachedGroup> CachedGroups { get; set; }
    public DbSet<CacheMetadata> CacheMetadata { get; set; }

    public CacheDbContext()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var cacheDir = Path.Combine(appData, "ADFlowManager", "Cache");
        Directory.CreateDirectory(cacheDir);

        _dbPath = Path.Combine(cacheDir, "adflow-cache.db");
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={_dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<CachedUser>()
            .HasIndex(u => u.CachedAt);

        modelBuilder.Entity<CachedGroup>()
            .HasIndex(g => g.CachedAt);
    }
}
