using Microsoft.EntityFrameworkCore;

namespace PlantMonitor.Backend;

/// <summary>
/// A stored moisture reading. EF owns the table (writes + migrations);
/// the read API still queries it via raw SQL.
/// </summary>
public class ReadingRow
{
    public Guid Id { get; set; }
    public string DeviceId { get; set; } = "";
    public int Raw { get; set; }
    public int Percent { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ReadingRow> Readings => Set<ReadingRow>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<ReadingRow>(e =>
        {
            e.ToTable("readings");
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(r => r.DeviceId).HasColumnName("device_id").IsRequired();
            e.Property(r => r.Raw).HasColumnName("raw").IsRequired();
            e.Property(r => r.Percent).HasColumnName("percent").IsRequired();
            e.Property(r => r.ReceivedAt).HasColumnName("received_at")
                .HasDefaultValueSql("now()").IsRequired();
            // Latest-per-device and a device's window query, newest first.
            e.HasIndex(r => new { r.DeviceId, r.ReceivedAt })
                .HasDatabaseName("readings_device_received")
                .IsDescending(false, true);
        });
    }
}
