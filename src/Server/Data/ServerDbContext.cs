using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Server.Models;
using Shared.Core.Data;
using Shared.Core.Entities;

namespace Server.Data;

/// <summary>
/// Database context for the server application using PostgreSQL
/// Extends the shared PosDbContext with server-specific entities
/// </summary>
public class ServerDbContext : PosDbContext
{
    public DbSet<Device> Devices { get; set; } = null!;
    public new DbSet<Server.Models.AuditLog> AuditLogs { get; set; } = null!;

    public ServerDbContext(DbContextOptions<ServerDbContext> options) : base(ConvertOptions(options))
    {
    }

    private static DbContextOptions<PosDbContext> ConvertOptions(DbContextOptions<ServerDbContext> options)
    {
        var builder = new DbContextOptionsBuilder<PosDbContext>();
        
        // Copy the configuration from the server options
        foreach (var extension in options.Extensions)
        {
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(extension);
        }
        
        return builder.Options;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Don't call base.OnConfiguring() as it sets up SQLite
        // PostgreSQL configuration will be handled by dependency injection
        optionsBuilder.EnableSensitiveDataLogging(false);
        optionsBuilder.EnableDetailedErrors(true);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Device configuration
        modelBuilder.Entity<Device>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ApiKey).IsUnique();
            entity.HasIndex(e => e.IsActive);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.LastSyncAt);
            
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ApiKey).IsRequired().HasMaxLength(500);
            entity.Property(e => e.LastSyncVersion).HasMaxLength(50);
        });

        // AuditLog configuration - use a separate table name to avoid conflict with Shared.Core's AuditLogs
        modelBuilder.Entity<Server.Models.AuditLog>(entity =>
        {
            entity.ToTable("ServerAuditLogs");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Operation);
            entity.HasIndex(e => e.EntityType);
            entity.HasIndex(e => e.EntityId);
            entity.HasIndex(e => e.DeviceId);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.UserId);
            
            entity.Property(e => e.Operation).IsRequired().HasMaxLength(50);
            entity.Property(e => e.EntityType).IsRequired().HasMaxLength(100);
            entity.Property(e => e.UserId).HasMaxLength(100);
            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.Property(e => e.UserAgent).HasMaxLength(500);
        });

        // Configure PostgreSQL-specific settings for shared entities
        ConfigureForPostgreSQL(modelBuilder);
    }

    private void ConfigureForPostgreSQL(ModelBuilder modelBuilder)
    {
        // Configure decimal precision for PostgreSQL
        modelBuilder.Entity<Product>(entity =>
        {
            entity.Property(e => e.UnitPrice).HasPrecision(18, 2);
            entity.Property(e => e.PurchasePrice).HasPrecision(18, 2);
            entity.Property(e => e.SellingPrice).HasPrecision(18, 2);

            // Fix SQLite-specific filter (PascalCase) → PostgreSQL (quoted PascalCase)
            entity.HasIndex(e => new { e.ExpiryDate, e.ShopId })
                  .HasDatabaseName("IX_Product_Expiry_Shop")
                  .HasFilter("\"ExpiryDate\" IS NOT NULL");
        });

        modelBuilder.Entity<Sale>(entity =>
        {
            entity.Property(e => e.TotalAmount).HasPrecision(18, 2);

            entity.HasIndex(e => new { e.CustomerId, e.CreatedAt })
                  .HasDatabaseName("IX_Sale_Customer_Created")
                  .HasFilter("\"CustomerId\" IS NOT NULL");
        });

        modelBuilder.Entity<SaleItem>(entity =>
        {
            entity.Property(e => e.UnitPrice).HasPrecision(18, 2);

            entity.HasIndex(e => e.BatchNumber)
                  .HasDatabaseName("IX_SaleItem_BatchNumber")
                  .HasFilter("\"BatchNumber\" IS NOT NULL");
        });

        // Fix SQLite-specific index filters that use PascalCase column names.
        // PostgreSQL requires quoted PascalCase column names in filter expressions.
        modelBuilder.Entity<CustomerMembership>(entity =>
        {
            entity.HasIndex(e => new { e.ExpiryDate, e.IsActive })
                  .HasDatabaseName("IX_CustomerMembership_Expiry_Active")
                  .HasFilter("\"ExpiryDate\" IS NOT NULL");
        });

        modelBuilder.Entity<SaleDiscount>(entity =>
        {
            entity.HasIndex(e => e.DiscountId)
                  .HasDatabaseName("IX_SaleDiscount_DiscountId")
                  .HasFilter("\"DiscountId\" IS NOT NULL");
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await LogAuditTrailAsync();
        return await base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        LogAuditTrailAsync().GetAwaiter().GetResult();
        return base.SaveChanges();
    }

    private async Task LogAuditTrailAsync()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Added || 
                       e.State == EntityState.Modified || 
                       e.State == EntityState.Deleted)
            .Where(e => e.Entity is not Server.Models.AuditLog) // Don't audit the audit logs themselves
            .ToList();

        var auditLogs = new List<Server.Models.AuditLog>();

        foreach (var entry in entries)
        {
            var operation = entry.State switch
            {
                EntityState.Added => "CREATE",
                EntityState.Modified => "UPDATE",
                EntityState.Deleted => "DELETE",
                _ => "UNKNOWN"
            };

            var entityType = entry.Entity.GetType().Name;
            var entityId = GetEntityId(entry.Entity);
            var deviceId = GetDeviceId(entry.Entity);

            var auditLog = new Server.Models.AuditLog
            {
                Operation = operation,
                EntityType = entityType,
                EntityId = entityId,
                DeviceId = deviceId,
                CreatedAt = DateTime.UtcNow,
                OldValues = entry.State == EntityState.Modified ? GetEntityValues(entry.OriginalValues) : null,
                NewValues = entry.State != EntityState.Deleted ? GetEntityValues(entry.CurrentValues) : null
            };

            auditLogs.Add(auditLog);
        }

        if (auditLogs.Any())
        {
            AuditLogs.AddRange(auditLogs);
        }
    }

    private Guid GetEntityId(object entity)
    {
        var idProperty = entity.GetType().GetProperty("Id");
        if (idProperty != null && idProperty.PropertyType == typeof(Guid))
        {
            return (Guid)(idProperty.GetValue(entity) ?? Guid.Empty);
        }
        return Guid.Empty;
    }

    private Guid GetDeviceId(object entity)
    {
        var deviceIdProperty = entity.GetType().GetProperty("DeviceId");
        if (deviceIdProperty != null && deviceIdProperty.PropertyType == typeof(Guid))
        {
            return (Guid)(deviceIdProperty.GetValue(entity) ?? Guid.Empty);
        }
        return Guid.Empty;
    }

    private string GetEntityValues(Microsoft.EntityFrameworkCore.ChangeTracking.PropertyValues values)
    {
        var properties = values.Properties
            .ToDictionary(p => p.Name, p => values[p]?.ToString() ?? "null");
        
        return System.Text.Json.JsonSerializer.Serialize(properties);
    }
}