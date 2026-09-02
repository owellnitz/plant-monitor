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
    public string? Fw { get; set; }
}

/// <summary>
/// A firmware image cached from a GitHub release. The device has no TLS, so it
/// cannot pull from GitHub itself — the backend fetches each release asset once
/// and serves the bytes over plain HTTP on the local network.
/// </summary>
public class FirmwareImage
{
    public Guid Id { get; set; }
    /// <summary>The release tag, e.g. "firmware-v0.4.0" — what the device reports as its build id.</summary>
    public string Version { get; set; } = "";
    public string Sha256 { get; set; } = "";
    /// <summary>Byte count of Data, stored so the update check can answer without loading the image.</summary>
    public int Size { get; set; }
    public byte[] Data { get; set; } = [];
    public DateTimeOffset FetchedAt { get; set; }
}

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ReadingRow> Readings => Set<ReadingRow>();
    public DbSet<Plant> Plants => Set<Plant>();
    public DbSet<Species> Species => Set<Species>();
    public DbSet<FirmwareImage> FirmwareImages => Set<FirmwareImage>();

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
            e.Property(p => p.MustWaterPercent).HasColumnName("must_water_percent");
            e.Property(p => p.CanWaterPercent).HasColumnName("can_water_percent");
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
            e.Property(r => r.Fw).HasColumnName("fw");
            // Latest-per-device and a device's window query, newest first.
            e.HasIndex(r => new { r.DeviceId, r.ReceivedAt })
                .HasDatabaseName("readings_device_received")
                .IsDescending(false, true);
        });

        model.Entity<FirmwareImage>(e =>
        {
            e.ToTable("firmware_images");
            e.HasKey(f => f.Id);
            e.Property(f => f.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(f => f.Version).HasColumnName("version").IsRequired();
            e.Property(f => f.Sha256).HasColumnName("sha256").IsRequired();
            e.Property(f => f.Size).HasColumnName("size").IsRequired();
            e.Property(f => f.Data).HasColumnName("data").IsRequired();
            e.Property(f => f.FetchedAt).HasColumnName("fetched_at")
                .HasDefaultValueSql("now()").IsRequired();
            // One row per release, and the lookup the device's update check does.
            e.HasIndex(f => f.Version).IsUnique();
        });
    }
}
