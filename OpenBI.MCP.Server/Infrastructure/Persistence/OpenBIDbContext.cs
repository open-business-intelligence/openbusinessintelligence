using Microsoft.EntityFrameworkCore;
using OpenBI.MCP.Server.Infrastructure.Persistence.Entities;

namespace OpenBI.MCP.Server.Infrastructure.Persistence;

public class OpenBIDbContext : DbContext
{
    public OpenBIDbContext(DbContextOptions<OpenBIDbContext> options) : base(options) { }

    public DbSet<DbAssetInfo> AssetInfos => Set<DbAssetInfo>();
    public DbSet<DbTable> Tables => Set<DbTable>();
    public DbSet<DbColumn> Columns => Set<DbColumn>();
    public DbSet<DbRelationship> Relationships => Set<DbRelationship>();
    public DbSet<DbPage> Pages => Set<DbPage>();
    public DbSet<DbVisual> Visuals => Set<DbVisual>();
    public DbSet<DbVisualProjection> VisualProjections => Set<DbVisualProjection>();
    public DbSet<DbFilter> Filters => Set<DbFilter>();
    public DbSet<DbRefreshTask> RefreshTasks => Set<DbRefreshTask>();
    public DbSet<DbDataSourceConnection> DataSourceConnections => Set<DbDataSourceConnection>();
    public DbSet<DbAssetDependency> AssetDependencies => Set<DbAssetDependency>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureAssetInfo(modelBuilder);
        ConfigureTable(modelBuilder);
        ConfigureColumn(modelBuilder);
        ConfigureRelationship(modelBuilder);
        ConfigurePage(modelBuilder);
        ConfigureVisual(modelBuilder);
        ConfigureVisualProjection(modelBuilder);
        ConfigureFilter(modelBuilder);
        ConfigureRefreshTask(modelBuilder);
        ConfigureDataSourceConnection(modelBuilder);
        ConfigureAssetDependency(modelBuilder);
    }

    private static void ConfigureAssetDependency(ModelBuilder mb)
    {
        mb.Entity<DbAssetDependency>(e =>
        {
            e.ToTable("AssetDependencies");
            e.HasKey(x => new { x.DependentAssetId, x.DependsOnAssetId });
            e.Property(x => x.DependentAssetId).HasMaxLength(256);
            e.Property(x => x.DependsOnAssetId).HasMaxLength(256);
            e.HasOne(x => x.DependentAsset)
                .WithMany()
                .HasForeignKey(x => x.DependentAssetId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.DependsOnAsset)
                .WithMany()
                .HasForeignKey(x => x.DependsOnAssetId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureAssetInfo(ModelBuilder mb)
    {
        mb.Entity<DbAssetInfo>(e =>
        {
            e.ToTable("AssetInfo");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(256);
            e.Property(x => x.IdSite).HasMaxLength(256);
            e.Property(x => x.Name).HasMaxLength(512);
            e.Property(x => x.ExternalType).HasMaxLength(256);
        });
    }

    private static void ConfigureTable(ModelBuilder mb)
    {
        mb.Entity<DbTable>(e =>
        {
            e.ToTable("Tables");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(256);
            e.Property(x => x.Name).HasMaxLength(512);
            e.HasOne(x => x.AssetInfo)
                .WithMany(x => x.Tables)
                .HasForeignKey(x => x.AssetInfoId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureColumn(ModelBuilder mb)
    {
        mb.Entity<DbColumn>(e =>
        {
            e.ToTable("Columns");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(256);
            e.Property(x => x.Name).HasMaxLength(512);
            e.HasOne(x => x.Table)
                .WithMany(x => x.Columns)
                .HasForeignKey(x => x.TableId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureRelationship(ModelBuilder mb)
    {
        mb.Entity<DbRelationship>(e =>
        {
            e.ToTable("Relationships");
            e.HasKey(x => x.RowId);
            e.Property(x => x.RowId).ValueGeneratedOnAdd();
            e.Property(x => x.IdColumnFrom).HasMaxLength(256);
            e.Property(x => x.IdColumnTo).HasMaxLength(256);
            e.HasOne(x => x.AssetInfo)
                .WithMany(x => x.Relationships)
                .HasForeignKey(x => x.AssetInfoId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigurePage(ModelBuilder mb)
    {
        mb.Entity<DbPage>(e =>
        {
            e.ToTable("Pages");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(256);
            e.Property(x => x.FriendlyName).HasMaxLength(512);
            e.HasOne(x => x.AssetInfo)
                .WithMany(x => x.Pages)
                .HasForeignKey(x => x.AssetInfoId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureVisual(ModelBuilder mb)
    {
        mb.Entity<DbVisual>(e =>
        {
            e.ToTable("Visuals");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(256);
            e.Property(x => x.FriendlyName).HasMaxLength(512);
            e.Property(x => x.Type).HasMaxLength(256);
            e.Property(x => x.OpenBIVisualType).HasMaxLength(256);
            e.HasOne(x => x.Page)
                .WithMany(x => x.Visuals)
                .HasForeignKey(x => x.PageId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ParentVisual)
                .WithMany(x => x.Children)
                .HasForeignKey(x => x.ParentVisualId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureVisualProjection(ModelBuilder mb)
    {
        mb.Entity<DbVisualProjection>(e =>
        {
            e.ToTable("VisualProjections");
            e.HasKey(x => x.RowId);
            e.Property(x => x.RowId).ValueGeneratedOnAdd();
            e.Property(x => x.ProjectionName).HasMaxLength(256);
            e.HasOne(x => x.Visual)
                .WithMany(x => x.VisualProjections)
                .HasForeignKey(x => x.VisualId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureFilter(ModelBuilder mb)
    {
        mb.Entity<DbFilter>(e =>
        {
            e.ToTable("Filters");
            e.HasKey(x => x.RowId);
            e.Property(x => x.RowId).ValueGeneratedOnAdd();
            e.Property(x => x.IdColumn).HasMaxLength(256);
            e.HasOne(x => x.Page)
                .WithMany(x => x.PageLevelFilters)
                .HasForeignKey(x => x.PageId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Visual)
                .WithMany(x => x.VisualLevelFilters)
                .HasForeignKey(x => x.VisualId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureRefreshTask(ModelBuilder mb)
    {
        mb.Entity<DbRefreshTask>(e =>
        {
            e.ToTable("RefreshTasks");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(256);
            e.HasOne(x => x.AssetInfo)
                .WithMany()
                .HasForeignKey(x => x.AssetInfoId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureDataSourceConnection(ModelBuilder mb)
    {
        mb.Entity<DbDataSourceConnection>(e =>
        {
            e.ToTable("DataSourceConnections");
            e.HasKey(x => x.RowId);
            e.Property(x => x.RowId).ValueGeneratedOnAdd();
            e.Property(x => x.ExternalId).HasMaxLength(256);
            e.Property(x => x.Name).HasMaxLength(512);
            e.Property(x => x.Type).HasMaxLength(256);
            e.HasOne(x => x.AssetInfo)
                .WithMany()
                .HasForeignKey(x => x.AssetInfoId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
