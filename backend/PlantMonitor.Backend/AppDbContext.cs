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
    public DbSet<Plant> Plants => Set<Plant>();
    public DbSet<Species> Species => Set<Species>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Species>(e =>
        {
            e.ToTable("plant_species");
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(s => s.Name).HasColumnName("name").IsRequired();
            e.HasIndex(s => s.Name).IsUnique();
        });

        model.Entity<Plant>(e =>
        {
            e.ToTable("plants");
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(p => p.Name).HasColumnName("name").IsRequired();
            e.Property(p => p.SpeciesId).HasColumnName("species_id");
            e.Property(p => p.Location).HasColumnName("location");
            e.Property(p => p.SunExposure).HasColumnName("sun_exposure");
            e.Property(p => p.DeviceId).HasColumnName("device_id");
            e.Property(p => p.CreatedAt).HasColumnName("created_at")
                .HasDefaultValueSql("now()").IsRequired();
            e.HasOne(p => p.Species).WithMany().HasForeignKey(p => p.SpeciesId);
            e.HasIndex(p => p.DeviceId).IsUnique();
        });

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
