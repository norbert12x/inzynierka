using Inżynierka.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Inżynierka.Data;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<CatalogPlace> Places => Set<CatalogPlace>();
    public DbSet<CatalogCategory> Categories => Set<CatalogCategory>();
    public DbSet<PlaceSource> Sources => Set<PlaceSource>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("catalog");
        modelBuilder.HasPostgresExtension("extensions", "postgis");

        var place = modelBuilder.Entity<CatalogPlace>();
        place.ToTable("places");
        place.HasKey(p => p.Id);
        place.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();
        place.Property(p => p.Name).HasColumnName("name").IsRequired();
        place.Property(p => p.Description).HasColumnName("description");
        place.Property(p => p.City).HasColumnName("city");
        place.Property(p => p.Address).HasColumnName("address");
        place.Property(p => p.Location).HasColumnName("location").HasColumnType("geography (point, 4326)").IsRequired();
        place.Property(p => p.Status).HasColumnName("status").IsRequired();
        place.Property(p => p.Kind).HasColumnName("kind").IsRequired();
        place.Property(p => p.ParentId).HasColumnName("parent_id");
        place.Property(p => p.OpeningHours).HasColumnName("opening_hours");
        place.Property(p => p.OpeningHoursVerification).HasColumnName("opening_hours_verification").IsRequired();
        place.Property(p => p.OpeningHoursConfirmedOn).HasColumnName("opening_hours_confirmed_on");
        place.Property(p => p.Website).HasColumnName("website");
        place.Property(p => p.EstimatedVisitMinutes).HasColumnName("estimated_visit_minutes");
        place.Property(p => p.ManuallyEditedFields).HasColumnName("manually_edited_fields").HasColumnType("text[]").IsRequired();
        place.HasOne(p => p.Parent).WithMany(p => p.Components).HasForeignKey(p => p.ParentId).OnDelete(DeleteBehavior.Restrict);
        place.HasIndex(p => p.Location).HasMethod("gist");

        var category = modelBuilder.Entity<CatalogCategory>();
        category.ToTable("categories");
        category.HasKey(c => c.Code);
        category.Property(c => c.Code).HasColumnName("code");
        category.Property(c => c.Name).HasColumnName("name").IsRequired();

        var assignment = modelBuilder.Entity<PlaceCategory>();
        assignment.ToTable("place_categories");
        assignment.HasKey(c => new { c.PlaceId, c.CategoryCode });
        assignment.Property(c => c.PlaceId).HasColumnName("place_id");
        assignment.Property(c => c.CategoryCode).HasColumnName("category_code");
        assignment.HasOne(c => c.Place).WithMany(p => p.Categories).HasForeignKey(c => c.PlaceId);
        assignment.HasOne(c => c.Category).WithMany(c => c.Places).HasForeignKey(c => c.CategoryCode);

        var source = modelBuilder.Entity<PlaceSource>();
        source.ToTable("place_sources");
        source.HasKey(s => s.Id);
        source.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
        source.Property(s => s.PlaceId).HasColumnName("place_id");
        source.Property(s => s.Provider).HasColumnName("provider").IsRequired();
        source.Property(s => s.ExternalId).HasColumnName("external_id").IsRequired();
        source.Property(s => s.FetchedAtUtc).HasColumnName("fetched_at_utc");
        source.Property(s => s.RawData).HasColumnName("raw_data").HasColumnType("jsonb").IsRequired();
        source.HasIndex(s => new { s.Provider, s.ExternalId }).IsUnique();
        source.HasOne(s => s.Place).WithMany(p => p.Sources).HasForeignKey(s => s.PlaceId);
    }
}
