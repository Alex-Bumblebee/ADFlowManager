using ADFlowManager.Infrastructure.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ADFlowManager.Infrastructure.Data;

/// <summary>
/// DbContext pour la base de données audit SQLite (local ou réseau).
/// Supporte les chemins réseau (UNC) avec WAL mode et busy_timeout.
/// </summary>
public class AuditDbContext : DbContext
{
    private readonly string _dbPath;
    private readonly bool _isNetworkPath;

    public DbSet<AuditLogEntity> AuditLogs { get; set; }

    public AuditDbContext(string dbPath)
    {
        _dbPath = dbPath;
        _isNetworkPath = dbPath.StartsWith(@"\\") || dbPath.StartsWith("//");
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = _isNetworkPath ? SqliteCacheMode.Shared : SqliteCacheMode.Default
        };

        optionsBuilder.UseSqlite(connStr.ToString(), opts =>
        {
            opts.CommandTimeout(30);
        });
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AuditLogEntity>()
            .HasIndex(a => a.Timestamp);

        modelBuilder.Entity<AuditLogEntity>()
            .HasIndex(a => a.Username);

        modelBuilder.Entity<AuditLogEntity>()
            .HasIndex(a => a.EntityId);
    }

    /// <summary>
    /// Configure WAL mode et busy_timeout après ouverture de la connexion.
    /// Doit être appelé après EnsureCreated().
    /// </summary>
    public void ConfigureForConcurrency()
    {
        var conn = Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA synchronous=NORMAL;";
        cmd.ExecuteNonQuery();
    }
}
